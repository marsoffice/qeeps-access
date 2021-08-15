using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Access.Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using StackExchange.Redis;

namespace MarsOffice.Qeeps.Access
{
    public class PopulateRedisUsers
    {
        private readonly GraphServiceClient _graphClient;
        private readonly IDatabase _redisDb;
        private readonly IServer _server;
        private readonly IConfiguration _config;
        public PopulateRedisUsers(GraphServiceClient graphClient, Lazy<IConnectionMultiplexer> mux, IConfiguration config)
        {
            _graphClient = graphClient;
            _redisDb = mux.Value.GetDatabase(config.GetValue<int>("redisdatabase_users"));
            _server = mux.Value.GetServer(mux.Value.GetEndPoints()[0]);
            _config = config;
        }

        [FunctionName("PopulateRedisUsers")]
        public async Task Run([TimerTrigger("0 */15 * * * *",RunOnStartup = true
        )] TimerInfo _,
        [Blob("graph-api/delta_users.json", FileAccess.Read)] Stream deltaFile,
        [Blob("graph-api/delta_users.json", FileAccess.Write)] Stream deltaFileWrite)
        {
            var lastDelta = "latest";
            var isRedisEmpty = !await _redisDb.KeyExistsAsync("dummy");

            if (!isRedisEmpty && deltaFile != null && deltaFile.CanRead)
            {
                using var streamReader = new StreamReader(deltaFile);
                var json = await streamReader.ReadToEndAsync();
                var deserialized = JsonConvert.DeserializeObject<DeltaFile>(json, new JsonSerializerSettings {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });
                lastDelta = deserialized.Delta;
            }

            if (isRedisEmpty)
            {
                await PopulateAllUsers();
                await PopulateUsersDelta(deltaFileWrite, lastDelta);
                await _redisDb.StringSetAsync($"dummy", "dummy");
            }
            else
            {
                await PopulateUsersDelta(deltaFileWrite, lastDelta);
            }
        }

        private async Task PopulateAllUsers()
        {
            var usersRequest = _graphClient
                .Users
                .Request()
                .Select(x => new { x.Id, x.DisplayName, x.GivenName, x.CompanyName, x.Mail, x.Surname, x.UserPrincipalName });

            while (usersRequest != null)
            {
                var usersResponse = await usersRequest.GetAsync();
                foreach (var u in usersResponse)
                {
                    var dto = new UserDto {
                        Id = u.Id,
                        Name = u.DisplayName,
                        Email = u.UserPrincipalName
                    };
                    var json = JsonConvert.SerializeObject(dto, new JsonSerializerSettings
                    {
                        ContractResolver = new CamelCasePropertyNamesContractResolver()
                    });
                    await _redisDb.StringSetAsync($"user_" + u.Id, json);
                }
                usersRequest = usersResponse.NextPageRequest;
            }
        }

        private async Task PopulateUsersDelta(Stream stream, string lastDelta)
        {
            var lastDeltaRequest = _graphClient
                .Users
                .Delta()
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
                foreach (var user in response.CurrentPage)
                {
                    var dto = new UserDto {
                        Id = user.Id,
                        Name = user.DisplayName,
                        Email = user.UserPrincipalName
                    };
                    var foundKeys = _server.Keys(_config.GetValue<int>("redisdatabase_users"), $"user_{user.Id}");
                    if (foundKeys.Any())
                    {
                        if (user.AdditionalData != null && user.AdditionalData.ContainsKey("@removed"))
                        {
                            await _redisDb.KeyDeleteAsync(foundKeys.ToArray());
                        }
                        else
                        {
                            var key = foundKeys.First();
                            var json = JsonConvert.SerializeObject(dto, new JsonSerializerSettings
                            {
                                ContractResolver = new CamelCasePropertyNamesContractResolver()
                            });
                            await _redisDb.StringSetAsync(key, json);
                        }
                    }
                    else
                    {
                        var json = JsonConvert.SerializeObject(dto, new JsonSerializerSettings
                        {
                            ContractResolver = new CamelCasePropertyNamesContractResolver()
                        });
                        await _redisDb.StringSetAsync($"user_{user.Id}", json);
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
