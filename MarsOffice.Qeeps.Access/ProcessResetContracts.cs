using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Access.Entities;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MarsOffice.Qeeps.Access
{
    public class ProcessResetContracts
    {
        private readonly IConfiguration _config;

        public ProcessResetContracts(IConfiguration config)
        {
            _config = config;
        }

        [FunctionName("ProcessResetContracts")]
        public async Task Run(
            [ServiceBusTrigger(
                #if DEBUG
                "reset-contracts-dev",
                #else
                "reset-contracts",
                #endif
                Connection = "sbconnectionstring")] ResetContractsRequestDto dto,
            [CosmosDB(
                databaseName: "access",
                collectionName: "Users",
                ConnectionStringSetting = "cdbconnectionstring", PreferredLocations = "%location%")] DocumentClient client
        )
        {
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
#endif

            var usersCollection = UriFactory.CreateDocumentCollectionUri("access", "Users");
            var getAllUsersQuery = client.CreateDocumentQuery<UserEntity>(usersCollection, new FeedOptions
            {
                PartitionKey = new PartitionKey("UserEntity")
            })
            .Where(x => x.HasSignedContract)
            .AsDocumentQuery();

            var tasks = new List<Task<ResourceResponse<Document>>>();

            while (getAllUsersQuery.HasMoreResults)
            {
                var results = await getAllUsersQuery.ExecuteNextAsync<UserEntity>();
                foreach (var result in results)
                {
                    result.HasSignedContract = false;
                    var task = client.UpsertDocumentAsync(usersCollection, result, new RequestOptions
                    {
                        PartitionKey = new PartitionKey("UserEntity")
                    });
                    tasks.Add(task);
                }
            }

            await Task.WhenAll(tasks);
        }
    }
}
