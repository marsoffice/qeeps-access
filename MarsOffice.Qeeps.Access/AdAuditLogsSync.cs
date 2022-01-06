using System;
using System.IO;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace MarsOffice.Qeeps.Access
{
    public class AdAuditLogsSync
    {
        [FunctionName("AdAuditLogsSync")]
        public void Run([BlobTrigger("insights-logs-auditlogs/{name}", Connection = "marsofficesaconnectionstring")] Stream myBlob, string name, ILogger log)
        {
            log.LogInformation($"C# Blob trigger function Processed blob\n Name:{name} \n Size: {myBlob.Length} Bytes");
        }
    }
}
