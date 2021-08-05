using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;

namespace MarsOffice.Qeeps.Access
{
    class DeltaFile {
        public string Delta {get;set;}
    }

    public class PopulateRedisGroups
    {
        private readonly GraphServiceClient _graphClient;
        public PopulateRedisGroups(GraphServiceClient graphClient)
        {
            _graphClient = graphClient;
        }

        [FunctionName("PopulateRedisGroups")]
        public async Task Run([TimerTrigger("0 */5 * * * *")]TimerInfo myTimer,
        [Blob("graph-api/delta.json", FileAccess.Read)] Stream deltaFile,
        ILogger log)
        {
            string lastDelta = null;
            if (deltaFile != null && deltaFile.CanRead) {
                var deserialized = await JsonSerializer.DeserializeAsync<DeltaFile>(deltaFile);
                lastDelta = deserialized.Delta;
            }
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            await Task.CompletedTask;
        }
    }
}
