using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Access.Entities;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
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
            Stream blobStream,
            string name,
            [CosmosDB(ConnectionStringSetting = "cdbconnectionstring", PreferredLocations = "%location%")] DocumentClient client,
            ILogger log)
        {
            if (_config["ismain"] != "true")
            {
                return;
            }
            client.ConnectionPolicy.UseMultipleWriteLocations = _config.GetValue<bool>("multimasterdatabase");
#if DEBUG
            var db = new Database
            {
                Id = "access"
            };
            await client.CreateDatabaseIfNotExistsAsync(db);


            var col = new DocumentCollection
            {
                Id = "Users",
                PartitionKey = new PartitionKeyDefinition
                {
                    Version = PartitionKeyDefinitionVersion.V2,
                    Paths = new System.Collections.ObjectModel.Collection<string>(new List<string>() { "/Partition" })
                }
            };
            await client.CreateDocumentCollectionIfNotExistsAsync(UriFactory.CreateDatabaseUri("access"), col);

            col = new DocumentCollection
            {
                Id = "OrganisationAccesses",
                PartitionKey = new PartitionKeyDefinition
                {
                    Version = PartitionKeyDefinitionVersion.V2,
                    Paths = new System.Collections.ObjectModel.Collection<string>(new List<string>() { "/Partition" })
                }
            };
            await client.CreateDocumentCollectionIfNotExistsAsync(UriFactory.CreateDatabaseUri("access"), col);

            col = new DocumentCollection
            {
                Id = "Organisations",
                PartitionKey = new PartitionKeyDefinition
                {
                    Version = PartitionKeyDefinitionVersion.V2,
                    Paths = new System.Collections.ObjectModel.Collection<string>(new List<string>() { "/Partition" })
                }
            };
            await client.CreateDocumentCollectionIfNotExistsAsync(UriFactory.CreateDatabaseUri("access"), col);
#endif

            var usersCollection = UriFactory.CreateDocumentCollectionUri("access", "Users");

            var noUsersExistInDb = (await client.CreateDocumentQuery<UserEntity>(usersCollection, new FeedOptions
            {
                PartitionKey = new PartitionKey("UserEntity")
            }).CountAsync()) == 0;

            var orgsCollection = UriFactory.CreateDocumentCollectionUri("access", "Organisations");
            var noOrgsExistInDb = (await client.CreateDocumentQuery<OrganisationEntity>(orgsCollection, new FeedOptions
            {
                PartitionKey = new PartitionKey("OrganisationEntity")
            }).CountAsync()) == 0;

            if (noUsersExistInDb && noOrgsExistInDb)
            {
                return;
            }

            log.LogInformation($"C# Blob trigger function Processed blob\n Name:{name} \n Size: Bytes");
            await Task.CompletedTask;
        }
    }
}
