using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Access.Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using StackExchange.Redis;

namespace MarsOffice.Qeeps.Access
{
    public class PopulateRedisRels
    {
        private readonly GraphServiceClient _graphClient;
        private readonly IConnectionMultiplexer _mux;
        private readonly IDatabase _redisDb;
        private readonly IServer _server;
        private readonly IConfiguration _config;
        public PopulateRedisRels(GraphServiceClient graphClient, Lazy<IConnectionMultiplexer> mux, IConfiguration config)
        {
            _graphClient = graphClient;
            _mux = mux.Value;
            _redisDb = mux.Value.GetDatabase(config.GetValue<int>("redisdatabase_rels"));
            _server = mux.Value.GetServer(mux.Value.GetEndPoints()[0]);
            _config = config;
        }

        [FunctionName("PopulateRedisRels")]
        public async Task Run([TimerTrigger("0 */15 * * * *", RunOnStartup = true)] TimerInfo timerInfo,
        [Blob("graph-api/delta_rels.json", FileAccess.Read)] Stream deltaFile,
        [Blob("graph-api/delta_rels.json", FileAccess.Write)] Stream deltaFileWrite)
        {
            var lastDelta = "latest";
            var isRedisEmpty = !await _redisDb.KeyExistsAsync("dummy");

            if (!isRedisEmpty && deltaFile != null && deltaFile.CanRead)
            {
                using var streamReader = new StreamReader(deltaFile);
                var json = await streamReader.ReadToEndAsync();
                var deserialized = JsonConvert.DeserializeObject<DeltaFile>(json, new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });
                lastDelta = deserialized.Delta;
            }

            if (isRedisEmpty)
            {
                var req = _graphClient
                    .Groups
                    .Request()
                    .Select(x => new { x.Id, x.DisplayName });
                while (req != null)
                {
                    var resp = await req.GetAsync();
                    foreach (var g in resp.CurrentPage)
                    {
                        await PopulateRelsRecursively(g.Id);
                    }
                    req = resp.NextPageRequest;
                }
                await PopulateRelsDelta(deltaFileWrite, lastDelta);
                await _redisDb.StringSetAsync($"dummy", "dummy");
            }
            else
            {
                await PopulateRelsDelta(deltaFileWrite, lastDelta);
            }
        }

        private async Task PopulateRelsRecursively(string id)
        {
            var group = (await _graphClient
                .Groups
                .Request()
                .Filter($"id eq '{id}'")
                .Select(x => new { x.Id, x.DisplayName }).GetAsync()).CurrentPage[0];

            var membersRequest = _graphClient.Groups[id]
             .Members
             .Request();

            while (membersRequest != null)
            {
                var response = await membersRequest.GetAsync();
                var childGroups = response.CurrentPage.Where(x => x.ODataType == "#microsoft.graph.group").ToList();
                var users = response.CurrentPage.Where(x => x.ODataType == "#microsoft.graph.user").ToList();
                if (childGroups == null || !childGroups.Any())
                {
                    foreach (var u in users)
                    {
                        await _redisDb.StringSetAsync($"{u.Id}_{group.Id}", "");
                    }
                }
                foreach (var child in childGroups)
                {
                    await PopulateRelsRecursively(child.Id);
                }
                membersRequest = response.NextPageRequest;
            }
        }

        private async Task PopulateRelsDelta(Stream stream, string lastDelta)
        {
            var lastDeltaRequest = _graphClient
                .Groups.Delta()
                .Request();
            lastDeltaRequest.QueryOptions.Add(new QueryOption("$deltaToken", lastDelta));
            string nextDelta = null;
            while (lastDeltaRequest != null)
            {
                var response = await lastDeltaRequest.GetAsync();
                if (nextDelta == null)
                {
                    nextDelta = response.AdditionalData["@odata.deltaLink"] as string;
                }
                foreach (var group in response.CurrentPage)
                {
                    if (group.AdditionalData != null && group.AdditionalData.ContainsKey("@removed"))
                    {
                        var keysToDelete = _server.Keys(_config.GetValue<int>("redisdatabase_rels"), $"*_{group.Id}");
                        await _redisDb.KeyDeleteAsync(keysToDelete.ToArray());
                        return;
                    }
                    if (group.AdditionalData != null && group.AdditionalData.ContainsKey("members@delta"))
                    {
                        var memberChanges = group.AdditionalData["members@delta"] as JArray;
                        foreach (JObject jObj in memberChanges)
                        {
                            if (jObj.GetValue("@odata.type").ToString() != "#microsoft.graph.user")
                            {
                                continue;
                            }
                            var uid = jObj.GetValue("id").ToString();
                            if (jObj.ContainsKey("@removed"))
                            {
                                await _redisDb.KeyDeleteAsync($"{uid}_{group.Id}");
                            }
                            else
                            {
                                await _redisDb.StringSetAsync($"{uid}_{group.Id}", "");
                            }
                        }
                    }
                }
                lastDeltaRequest = response.NextPageRequest;
            }

            var obj = new DeltaFile
            {
                Delta = nextDelta.Split("?")[1].Replace("$deltatoken=", "")
            };
            var deltaFileJson = JsonConvert.SerializeObject(obj, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
            using var streamWriter = new StreamWriter(stream);
            await streamWriter.WriteAsync(deltaFileJson);
        }
    }
}
