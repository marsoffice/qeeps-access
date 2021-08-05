using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
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
        private readonly IDatabase _redisDb;
        public PopulateRedisGroups(GraphServiceClient graphClient, IDatabase redisDb)
        {
            _graphClient = graphClient;
            _redisDb = redisDb;
        }

        [FunctionName("PopulateRedisGroups")]
        public async Task Run([TimerTrigger("0 */5 * * * *", RunOnStartup = true)] TimerInfo myTimer,
        [Blob("graph-api/delta.json", FileAccess.Read)] Stream deltaFile,
        ILogger log)
        {
            string lastDelta = null;
            var isRedisEmpty = !await _redisDb.KeyExistsAsync("dummy");

            if (!isRedisEmpty && deltaFile != null && deltaFile.CanRead)
            {
                var deserialized = await JsonSerializer.DeserializeAsync<DeltaFile>(deltaFile);
                lastDelta = deserialized.Delta;
            }

            var groups = await _graphClient.Groups.Request()
                .GetAsync();
            
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            await Task.CompletedTask;
        }
    }
}
