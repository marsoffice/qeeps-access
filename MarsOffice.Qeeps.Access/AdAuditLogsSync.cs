using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MarsOffice.Qeeps.Access
{
    public class AdAuditLogsSync
    {
        private readonly IConfiguration _config;

        public AdAuditLogsSync(IConfiguration config)
        {
            _config = config;
        }

        [FunctionName("AdAuditLogsSync")]
        public async Task Run(
            [BlobTrigger("insights-logs-auditlogs/{name}", Connection = "marsofficesaconnectionstring")]
            ICloudBlob blob,
            string name,
            ILogger log)
        {
            if (_config["ismain"] != "true")
            {
                return;
            }
            var metadata = blob.Metadata;
            log.LogInformation($"C# Blob trigger function Processed blob\n Name:{name} \n Size: Bytes");
            await Task.CompletedTask;
        }
    }
}
