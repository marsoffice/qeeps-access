using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;

namespace MarsOffice.Qeeps.Access
{
    class DeltaFile
    {
        public string Delta { get; set; }
    }

    public class PopulateRedisGroups
    {
        private readonly GraphServiceClient _graphClient;
        private readonly ConnectionMultiplexer _mux;
        private readonly IDatabase _redisDb;
        private readonly IServer _server;
        private readonly IConfiguration _config;
        public PopulateRedisGroups(GraphServiceClient graphClient, ConnectionMultiplexer mux, IConfiguration config)
        {
            _graphClient = graphClient;
            _mux = mux;
            _redisDb = mux.GetDatabase(config.GetValue<int>("redisdatabase"));
            _server = mux.GetServer(mux.GetEndPoints()[0]);
            _config = config;
        }

        [FunctionName("PopulateRedisGroups")]
        public async Task Run([TimerTrigger("0 */15 * * * *", RunOnStartup = false)] TimerInfo myTimer,
        [Blob("graph-api/delta.json", FileAccess.Read)] Stream deltaFile,
        [Blob("graph-api/delta.json", FileAccess.Write)] Stream deltaFileWrite,
        ILogger log)
        {
            var lastDelta = "latest";
            var isRedisEmpty = !await _redisDb.KeyExistsAsync("dummy");

            if (!isRedisEmpty && deltaFile != null && deltaFile.CanRead)
            {
                var deserialized = await JsonSerializer.DeserializeAsync<DeltaFile>(deltaFile);
                lastDelta = deserialized.Delta;
            }

            if (isRedisEmpty)
            {
                await PopulateGroupsRecursively(_config["adgroupid"]);
                await PopulateGroupsDelta(deltaFileWrite, lastDelta);
                await _redisDb.StringSetAsync($"dummy", "dummy");
            }
            else
            {
                await PopulateGroupsDelta(deltaFileWrite, lastDelta);
            }
        }

        private async Task PopulateGroupsRecursively(string id, string prefix = "")
        {
            var group = (await _graphClient
                .Groups
                .Request()
                .Filter($"id eq '{id}'")
                .Select(x => new { x.Id, x.DisplayName }).GetAsync()).CurrentPage[0];
            await _redisDb.StringSetAsync($"{prefix}_{group.Id}", group.DisplayName);

            var membersRequest = _graphClient.Groups[id]
             .Members
             .Request();

            while (membersRequest != null)
            {
                var response = await membersRequest.GetAsync();
                foreach (var child in response.CurrentPage.Where(x => x.ODataType == "#microsoft.graph.group").ToList())
                {
                    await PopulateGroupsRecursively(child.Id, $"{prefix}_{id}");
                }
                membersRequest = response.NextPageRequest;
            }
        }

        private async Task PopulateGroupsDelta(Stream stream, string lastDelta)
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
                foreach (var group in response.CurrentPage.OrderBy(x => x.AdditionalData).ToList())
                {
                    var foundKeys = _server.Keys(_config.GetValue<int>("redisdatabase"), $"*_{group.Id}");
                    if (foundKeys.Any())
                    {
                        if (group.AdditionalData != null && group.AdditionalData.ContainsKey("@removed"))
                        {
                            await _redisDb.KeyDeleteAsync(foundKeys.ToArray());
                            var allKeysToDelete = _server.Keys(_config.GetValue<int>("redisdatabase"), $"*_{group.Id}_*");
                            foreach (var keyToDelete in allKeysToDelete)
                            {
                                var newKey = keyToDelete.ToString().Split("_").Last();
                                await _redisDb.KeyRenameAsync(keyToDelete, "_" + newKey);
                            }
                        }
                        else
                        {
                            var key = foundKeys.First();
                            await _redisDb.StringSetAsync(key, group.DisplayName);

                            // members
                            if (group.AdditionalData != null && group.AdditionalData.ContainsKey("members@delta"))
                            {
                                var memberChanges = group.AdditionalData["members@delta"] as JArray;
                                foreach (JObject jObj in memberChanges)
                                {
                                    if (jObj.GetValue("@odata.type").ToString() != "#microsoft.graph.group")
                                    {
                                        continue;
                                    }
                                    var id = jObj.GetValue("id").ToString();
                                    if (jObj.ContainsKey("@removed"))
                                    {
                                        var foundKeysWithParent = _server.Keys(_config.GetValue<int>("redisdatabase"), $"*_{id}*");
                                        foreach (var k in foundKeysWithParent)
                                        {
                                            var strK = k.ToString();
                                            var parentsRemoved = strK[strK.IndexOf($"_{id}")..];
                                            await _redisDb.KeyRenameAsync(k, parentsRemoved);
                                        }
                                    }
                                    else
                                    {
                                        var foundKeyWithParent = _server.Keys(_config.GetValue<int>("redisdatabase"), $"*_{id}");
                                        if (foundKeyWithParent.Any())
                                        {
                                            var singleKey = foundKeyWithParent.First();
                                            var foundParentKeys = _server.Keys(_config.GetValue<int>("redisdatabase"), $"*_{group.Id}");
                                            if (foundParentKeys.Any())
                                            {
                                                var parentKey = foundParentKeys.First();
                                                await _redisDb.KeyRenameAsync(singleKey, $"{parentKey}_{id}");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        if (group.AdditionalData == null || !group.AdditionalData.ContainsKey("@removed"))
                        {
                            await _redisDb.StringSetAsync($"_{group.Id}", group.DisplayName);
                        }
                    }
                }



                lastDeltaRequest = response.NextPageRequest;
            }
            var obj = new DeltaFile
            {
                Delta = nextDelta.Split("?")[1].Replace("$deltatoken=", "")
            };
            await JsonSerializer.SerializeAsync(stream, obj);
        }
    }
}
