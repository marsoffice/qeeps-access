using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;

namespace MarsOffice.Qeeps.Access
{
    public class PopulateRedisGroups
    {
        private readonly GraphServiceClient _graphClient;
        public PopulateRedisGroups(GraphServiceClient graphClient)
        {
            _graphClient = graphClient;
        }

        [FunctionName("PopulateRedisGroups")]
        public async Task Run([TimerTrigger("0 */5 * * * *")]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            await Task.CompletedTask;
        }
    }
}
