using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Access.Entities;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Graph;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MarsOffice.Qeeps.Access
{
    public class PopulateAccessData
    {
        private readonly GraphServiceClient _graphClient;

        public PopulateAccessData()
        {

        }

        //[FunctionName("PopulateAccessData")]
        public async Task Run([TimerTrigger("0 */15 * * * *", RunOnStartup = true)] TimerInfo timerInfo,
        [Blob("graph-api/delta_access.json", FileAccess.Read)] Stream deltaFile,
        [Blob("graph-api/delta_access.json", FileAccess.Write)] Stream deltaFileWrite,
        [CosmosDB(ConnectionStringSetting = "cdbconnectionstring")] DocumentClient client
        )
        {
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
                    Version = PartitionKeyDefinitionVersion.V1,
                    Paths = new System.Collections.ObjectModel.Collection<string>(new List<string>() { "/Partition" })
                }
            };
            await client.CreateDocumentCollectionIfNotExistsAsync(UriFactory.CreateDatabaseUri("access"), col);

            col = new DocumentCollection
            {
                Id = "Organisations",
                PartitionKey = new PartitionKeyDefinition
                {
                    Version = PartitionKeyDefinitionVersion.V1,
                    Paths = new System.Collections.ObjectModel.Collection<string>(new List<string>() { "/Partition" })
                }
            };
            await client.CreateDocumentCollectionIfNotExistsAsync(UriFactory.CreateDatabaseUri("access"), col);

            col = new DocumentCollection
            {
                Id = "OrganisationAccesses",
                PartitionKey = new PartitionKeyDefinition
                {
                    Version = PartitionKeyDefinitionVersion.V1,
                    Paths = new System.Collections.ObjectModel.Collection<string>(new List<string>() { "/Partition" })
                }
            };
            await client.CreateDocumentCollectionIfNotExistsAsync(UriFactory.CreateDatabaseUri("access"), col);
#endif

            var orgsCollection = UriFactory.CreateDocumentCollectionUri("access", "Organisations");
            var isDbEmpty = (await client.CreateDocumentQuery<OrganisationEntity>(orgsCollection, new FeedOptions
            {
                PartitionKey = new PartitionKey("OrganisationEntity")
            }).CountAsync()) == 0;

            var lastDelta = "latest";

            if (!isDbEmpty && deltaFile != null && deltaFile.CanRead)
            {
                using var streamReader = new StreamReader(deltaFile);
                var json = await streamReader.ReadToEndAsync();
                var deserialized = JsonConvert.DeserializeObject<DeltaFile>(json, new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });
                lastDelta = deserialized.Delta;
            }

            if (isDbEmpty)
            {
                await PopulateAll(client);
                await PopulateDelta(client, deltaFileWrite, lastDelta);
            }
            else
            {
                await PopulateDelta(client, deltaFileWrite, lastDelta);
            }
        }

        private async Task PopulateAll(DocumentClient client)
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
                    await PopulateAllForGroupRecursively(client, g.Id);
                }
                req = resp.NextPageRequest;
            }
        }

        private async Task PopulateAllForGroupRecursively(DocumentClient client, string id, string prefix = "")
        {
            var orgCol = UriFactory.CreateDocumentCollectionUri("access", "Organisations");
            var usersCol = UriFactory.CreateDocumentCollectionUri("access", "Users");
            var orgAccessesCol = UriFactory.CreateDocumentCollectionUri("access", "OrganisationAccesses");

            var group = (await _graphClient
                .Groups
                .Request()
                .Filter($"id eq '{id}'")
                .Select(x => new { x.Id, x.DisplayName }).GetAsync()).CurrentPage[0];

            var newOrg = new OrganisationEntity
            {
                Id = group.Id,
                Name = group.DisplayName,
                FullId = $"{prefix}_{group.Id}"
            };


            await client.CreateDocumentAsync(orgCol, newOrg, new RequestOptions
            {
                PartitionKey = new PartitionKey("OrganisationEntity")
            }, true);

            var membersRequest = _graphClient.Groups[id]
             .Members
             .Request();

            while (membersRequest != null)
            {
                var response = await membersRequest.GetAsync();
                foreach (var user in response.CurrentPage.Where(x => x.ODataType == "#microsoft.graph.user").ToList())
                {
                    var casted = user as Microsoft.Graph.User;
                    var newUserEntity = new UserEntity
                    {
                        Id = casted.Id,
                        Email = casted.UserPrincipalName,
                        Name = casted.DisplayName,
                        UserPreferences = new UserPreferencesEntity()
                    };
                    await client.UpsertDocumentAsync(usersCol, newUserEntity, new RequestOptions
                    {
                        PartitionKey = new PartitionKey("UserEntity")
                    }, true);

                    var newAccessEntity = new OrganisationAccessEntity
                    {
                        OrganisationId = id,
                        FullOrganisationId = $"{prefix}_{id}",
                        UserId = casted.Id
                    };
                    await client.UpsertDocumentAsync(orgAccessesCol, newAccessEntity, new RequestOptions
                    {
                        PartitionKey = new PartitionKey("OrganisationAccessEntity")
                    });
                }
                foreach (var child in response.CurrentPage.Where(x => x.ODataType == "#microsoft.graph.group").ToList())
                {
                    await PopulateAllForGroupRecursively(client, child.Id, $"{prefix}_{id}");
                }
                membersRequest = response.NextPageRequest;
            }
        }

        private async Task PopulateDelta(DocumentClient client, Stream stream, string lastDelta)
        {
            var orgCol = UriFactory.CreateDocumentCollectionUri("access", "Organisations");
            var usersCol = UriFactory.CreateDocumentCollectionUri("access", "Users");
            var orgAccessesCol = UriFactory.CreateDocumentCollectionUri("access", "OrganisationAccesses");

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

                foreach (var g in response.CurrentPage)
                {
                    // DELETE GROUP
                    if (g.AdditionalData != null && g.AdditionalData.ContainsKey("@removed"))
                    {
                        var docUri = UriFactory.CreateDocumentUri("access", "Organisations", g.Id);
                        await client.DeleteDocumentAsync(docUri, new RequestOptions
                        {
                            PartitionKey = new PartitionKey("OrganisationEntity")
                        });

                        var docsToChangeParentQuery = client.CreateDocumentQuery<OrganisationEntity>(orgCol, new FeedOptions
                        {
                            PartitionKey = new PartitionKey("OrganisationEntity")
                        })
                        .Where(x => x.FullId.Contains($"_{g.Id}_"))
                        .AsDocumentQuery();
                        while (docsToChangeParentQuery.HasMoreResults)
                        {
                            var docs = await docsToChangeParentQuery.ExecuteNextAsync<OrganisationEntity>();
                            foreach (var d in docs)
                            {
                                d.FullId = d.FullId.Replace($"_{g.Id}", "");
                                var dUri = UriFactory.CreateDocumentUri("access", "Organisations", d.Id);
                                await client.UpsertDocumentAsync(dUri, d, new RequestOptions
                                {
                                    PartitionKey = new PartitionKey("OrganisationEntity")
                                }, true);
                            }
                        }

                        var userIdsToCheck = new HashSet<string>();
                        var accessesToDeleteQuery = client.CreateDocumentQuery<OrganisationAccessEntity>(orgAccessesCol, new FeedOptions
                        {
                            PartitionKey = new PartitionKey("OrganisationAccessEntity")
                        })
                        .Where(x => x.OrganisationId == g.Id)
                        .AsDocumentQuery();
                        while (accessesToDeleteQuery.HasMoreResults)
                        {
                            var docs = await accessesToDeleteQuery.ExecuteNextAsync<OrganisationAccessEntity>();
                            foreach (var d in docs)
                            {
                                userIdsToCheck.Add(d.UserId);
                                var dUri = UriFactory.CreateDocumentUri("access", "OrganisationAccesses", d.Id);
                                await client.DeleteDocumentAsync(dUri, new RequestOptions
                                {
                                    PartitionKey = new PartitionKey("OrganisationAccessEntity")
                                });
                            }
                        }

                        if (userIdsToCheck.Any())
                        {
                            var okUserIds = new HashSet<string>();
                            var remainingAccessesForUsersQuery = client.CreateDocumentQuery<OrganisationAccessEntity>(orgAccessesCol, new FeedOptions
                            {
                                PartitionKey = new PartitionKey("OrganisationAccessEntity")
                            })
                            .Where(x => userIdsToCheck.Contains(x.UserId))
                            .Select(x => new OrganisationAccessEntity
                            {
                                UserId = x.UserId
                            })
                            .Distinct()
                            .AsDocumentQuery();
                            while (remainingAccessesForUsersQuery.HasMoreResults)
                            {
                                var usersResult = await remainingAccessesForUsersQuery.ExecuteNextAsync<OrganisationAccessEntity>();
                                foreach (var x in usersResult)
                                {
                                    okUserIds.Add(x.UserId);
                                }
                            }

                            var userIdsToDelete = userIdsToCheck.Where(tc => !okUserIds.Any(z => z != tc)).ToList();
                            foreach (var uid in userIdsToDelete)
                            {
                                var dUri = UriFactory.CreateDocumentUri("access", "Users", uid);
                                await client.DeleteDocumentAsync(dUri, new RequestOptions
                                {
                                    PartitionKey = new PartitionKey("UserEntity")
                                });
                            }
                        }
                        continue;
                    }

                    var docIdUri = UriFactory.CreateDocumentUri("access", "Organisations", g.Id);
                    var groupEntity = (await client.ReadDocumentAsync<OrganisationEntity>(docIdUri, new RequestOptions {
                        PartitionKey = new PartitionKey("OrganisationEntity")
                    })).Document;

                    if (groupEntity == null)
                    {
                        groupEntity = new OrganisationEntity
                        {
                            Id = g.Id,
                            FullId = $"_{g.Id}",
                            Name = g.DisplayName
                        };
                        
                        await client.UpsertDocumentAsync(orgCol, groupEntity, new RequestOptions
                        {
                            PartitionKey = new PartitionKey("OrganisationEntity")
                        }, true);
                    } else {
                        groupEntity.Name = g.DisplayName;
                        await client.UpsertDocumentAsync(orgCol, groupEntity, new RequestOptions
                        {
                            PartitionKey = new PartitionKey("OrganisationEntity")
                        }, true);
                    }

                    // TODO Members


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