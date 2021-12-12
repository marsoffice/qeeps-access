using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Access.Entities;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace MarsOffice.Qeeps.Access
{
    public class PopulateAccessData
    {
        private readonly GraphServiceClient _graphClient;
        private readonly IConfiguration _config;

        public PopulateAccessData(GraphServiceClient graphClient, IConfiguration config)
        {
            _graphClient = graphClient;
            _config = config;
        }

        [FunctionName("PopulateAccessData")]
        public async Task Run([TimerTrigger("%cron%", RunOnStartup = true)] TimerInfo timerInfo,
        [Blob("graph-api/delta_access.json", FileAccess.Read)] string deltaFile,
        [Blob("graph-api/delta_access.json", FileAccess.Write)] TextWriter deltaFileWriter,
        [CosmosDB(ConnectionStringSetting = "cdbconnectionstring", PreferredLocations = "%location%")] DocumentClient client
        )
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
                Id = "Organisations",
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
#endif

            var orgsCollection = UriFactory.CreateDocumentCollectionUri("access", "Organisations");
            var isDbEmpty = (await client.CreateDocumentQuery<OrganisationEntity>(orgsCollection, new FeedOptions
            {
                PartitionKey = new PartitionKey("OrganisationEntity")
            }).CountAsync()) == 0;

            var lastDelta = "latest";

            if (!isDbEmpty && !string.IsNullOrEmpty(deltaFile))
            {
                var deserialized = JsonConvert.DeserializeObject<DeltaFile>(deltaFile, new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });
                if (deserialized != null)
                {
                    lastDelta = deserialized.Delta;
                }
            }

            if (isDbEmpty)
            {
                await PopulateAllForGroupRecursively(client, _config["adgroupid"]);
                await PopulateDelta(client, deltaFileWriter, lastDelta);
            }
            else
            {
                await PopulateDelta(client, deltaFileWriter, lastDelta);
            }
        }

        private async Task PopulateAllForGroupRecursively(DocumentClient client, string id, string prefix = "")
        {
            var orgCol = UriFactory.CreateDocumentCollectionUri("access", "Organisations");
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

            await client.UpsertDocumentAsync(orgCol, newOrg, new RequestOptions
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

                    var newAccessEntity = new OrganisationAccessEntity
                    {
                        OrganisationId = id,
                        FullOrganisationId = $"{prefix}_{id}",
                        UserId = casted.Id,
                        Id = id + "_" + casted.Id
                    };
                    await client.UpsertDocumentAsync(orgAccessesCol, newAccessEntity, new RequestOptions
                    {
                        PartitionKey = new PartitionKey("OrganisationAccessEntity")
                    }, true);
                }
                foreach (var child in response.CurrentPage.Where(x => x.ODataType == "#microsoft.graph.group").ToList())
                {
                    await PopulateAllForGroupRecursively(client, child.Id, $"{prefix}_{id}");
                }
                membersRequest = response.NextPageRequest;
            }
        }

        private async Task PopulateDelta(DocumentClient client, TextWriter stream, string lastDelta)
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
                    nextDelta = response.AdditionalData["@odata.deltaLink"].ToString();
                }

                foreach (var g in response.CurrentPage)
                {
                    // DELETE GROUP
                    if (g.AdditionalData != null && g.AdditionalData.ContainsKey("@removed"))
                    {
                        var docUri = UriFactory.CreateDocumentUri("access", "Organisations", g.Id);

                        try
                        {
                            await client.DeleteDocumentAsync(docUri, new RequestOptions
                            {
                                PartitionKey = new PartitionKey("OrganisationEntity")
                            });
                        }
                        catch (Exception) { }

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
                                await client.UpsertDocumentAsync(orgCol, d, new RequestOptions
                                {
                                    PartitionKey = new PartitionKey("OrganisationEntity")
                                }, true);
                            }
                        }

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
                                var dUri = UriFactory.CreateDocumentUri("access", "OrganisationAccesses", d.Id);
                                try
                                {
                                    await client.DeleteDocumentAsync(dUri, new RequestOptions
                                    {
                                        PartitionKey = new PartitionKey("OrganisationAccessEntity")
                                    });
                                }
                                catch (Exception)
                                {

                                }
                            }
                        }
                        continue;
                    }

                    var docIdUri = UriFactory.CreateDocumentUri("access", "Organisations", g.Id);

                    OrganisationEntity groupEntity = null;
                    try
                    {
                        groupEntity = (await client.ReadDocumentAsync<OrganisationEntity>(docIdUri, new RequestOptions
                        {
                            PartitionKey = new PartitionKey("OrganisationEntity")
                        }))?.Document;
                    }
                    catch (Exception) { }

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
                    }
                    else
                    {
                        groupEntity.Name = g.DisplayName;
                        await client.UpsertDocumentAsync(orgCol, groupEntity, new RequestOptions
                        {
                            PartitionKey = new PartitionKey("OrganisationEntity")
                        }, true);
                    }
                }
                lastDeltaRequest = response.NextPageRequest;
            }



            // PART 2 - Group Members
            lastDeltaRequest = _graphClient
                .Groups.Delta()
                .Request();
            lastDeltaRequest.QueryOptions.Add(new QueryOption("$deltaToken", lastDelta));


            while (lastDeltaRequest != null)
            {
                var response = await lastDeltaRequest.GetAsync();

                foreach (var g in response.CurrentPage)
                {
                    if (g.AdditionalData != null && g.AdditionalData.ContainsKey("members@delta"))
                    {
                        var docIdUri = UriFactory.CreateDocumentUri("access", "Organisations", g.Id);
                        OrganisationEntity groupEntity = null;
                        try
                        {
                            groupEntity = (await client.ReadDocumentAsync<OrganisationEntity>(docIdUri, new RequestOptions
                            {
                                PartitionKey = new PartitionKey("OrganisationEntity")
                            }))?.Document;
                        }
                        catch (Exception) { }

                        if (groupEntity == null)
                        {
                            continue;
                        }

                        var memberChanges = (JsonElement)g.AdditionalData["members@delta"];
                        for (var i = 0; i < memberChanges.GetArrayLength(); i++)
                        {
                            var jObj = memberChanges[i];
                            if (jObj.GetProperty("@odata.type").GetString() != "#microsoft.graph.group")
                            {
                                continue;
                            }
                            var memberId = jObj.GetProperty("id").GetString();

                            if (jObj.TryGetProperty("@removed", out _))
                            {
                                var docsToRenameQuery = client.CreateDocumentQuery<OrganisationEntity>(orgCol, new FeedOptions
                                {
                                    PartitionKey = new PartitionKey("OrganisationEntity")
                                })
                                .Where(x => x.FullId.Contains(groupEntity.FullId + "_" + memberId))
                                .AsDocumentQuery();
                                while (docsToRenameQuery.HasMoreResults)
                                {
                                    var toRename = await docsToRenameQuery.ExecuteNextAsync<OrganisationEntity>();
                                    foreach (var child in toRename)
                                    {
                                        child.FullId = child.FullId.Replace(groupEntity.FullId, "");
                                        await client.UpsertDocumentAsync(orgCol, child, new RequestOptions
                                        {
                                            PartitionKey = new PartitionKey("OrganisationEntity")
                                        }, true);
                                    }
                                }

                                var accessDocsToRenameQuery = client.CreateDocumentQuery<OrganisationAccessEntity>(orgAccessesCol, new FeedOptions
                                {
                                    PartitionKey = new PartitionKey("OrganisationAccessEntity")
                                })
                                .Where(x => x.FullOrganisationId.Contains(groupEntity.FullId + "_" + memberId))
                                .AsDocumentQuery();
                                while (accessDocsToRenameQuery.HasMoreResults)
                                {
                                    var toRename = await accessDocsToRenameQuery.ExecuteNextAsync<OrganisationAccessEntity>();
                                    foreach (var child in toRename)
                                    {
                                        child.FullOrganisationId = child.FullOrganisationId.Replace(groupEntity.FullId, "");
                                        await client.UpsertDocumentAsync(orgAccessesCol, child, new RequestOptions
                                        {
                                            PartitionKey = new PartitionKey("OrganisationAccessEntity")
                                        }, true);
                                    }
                                }
                            }
                            else
                            {
                                var docsToRenameQuery = client.CreateDocumentQuery<OrganisationEntity>(orgCol, new FeedOptions
                                {
                                    PartitionKey = new PartitionKey("OrganisationEntity")
                                })
                                .Where(x => x.FullId.Contains("_" + memberId))
                                .AsDocumentQuery();
                                while (docsToRenameQuery.HasMoreResults)
                                {
                                    var toRename = await docsToRenameQuery.ExecuteNextAsync<OrganisationEntity>();
                                    foreach (var child in toRename)
                                    {
                                        child.FullId = $"{groupEntity.FullId}{child.FullId}";
                                        await client.UpsertDocumentAsync(orgCol, child, new RequestOptions
                                        {
                                            PartitionKey = new PartitionKey("OrganisationEntity")
                                        }, true);
                                    }
                                }

                                var accessDocsToRenameQuery = client.CreateDocumentQuery<OrganisationAccessEntity>(orgAccessesCol, new FeedOptions
                                {
                                    PartitionKey = new PartitionKey("OrganisationAccessEntity")
                                })
                                .Where(x => x.FullOrganisationId.Contains("_" + memberId))
                                .AsDocumentQuery();
                                while (accessDocsToRenameQuery.HasMoreResults)
                                {
                                    var toRename = await accessDocsToRenameQuery.ExecuteNextAsync<OrganisationAccessEntity>();
                                    foreach (var child in toRename)
                                    {
                                        child.FullOrganisationId = $"{groupEntity.FullId}{child.FullOrganisationId}";
                                        await client.UpsertDocumentAsync(orgAccessesCol, child, new RequestOptions
                                        {
                                            PartitionKey = new PartitionKey("OrganisationAccessEntity")
                                        }, true);
                                    }
                                }
                            }
                        }
                    }
                }
                lastDeltaRequest = response.NextPageRequest;
            }

            // PART 3 - Group User Members
            lastDeltaRequest = _graphClient
                .Groups.Delta()
                .Request();
            lastDeltaRequest.QueryOptions.Add(new QueryOption("$deltaToken", lastDelta));

            while (lastDeltaRequest != null)
            {
                var response = await lastDeltaRequest.GetAsync();

                foreach (var g in response.CurrentPage)
                {
                    if (g.AdditionalData != null && g.AdditionalData.ContainsKey("members@delta"))
                    {
                        var docIdUri = UriFactory.CreateDocumentUri("access", "Organisations", g.Id);
                        OrganisationEntity groupEntity = null;
                        try
                        {
                            groupEntity = (await client.ReadDocumentAsync<OrganisationEntity>(docIdUri, new RequestOptions
                            {
                                PartitionKey = new PartitionKey("OrganisationEntity")
                            }))?.Document;
                        }
                        catch (Exception) { }

                        if (groupEntity == null)
                        {
                            continue;
                        }

                        var memberChanges = (JsonElement)g.AdditionalData["members@delta"];
                        for (var i = 0; i < memberChanges.GetArrayLength(); i++)
                        {
                            var jObj = memberChanges[i];
                            if (jObj.GetProperty("@odata.type").GetString() != "#microsoft.graph.user")
                            {
                                continue;
                            }
                            var memberId = jObj.GetProperty("id").GetString();

                            if (jObj.TryGetProperty("@removed", out _))
                            {
                                var accessDocUri = UriFactory.CreateDocumentUri("access", "OrganisationAccesses", g.Id + "_" + memberId);
                                try
                                {
                                    await client.DeleteDocumentAsync(accessDocUri, new RequestOptions
                                    {
                                        PartitionKey = new PartitionKey("OrganisationAccessEntity")
                                    });
                                }
                                catch (Exception) { }
                            }
                            else
                            {
                                var newAccessEntity = new OrganisationAccessEntity
                                {
                                    OrganisationId = g.Id,
                                    FullOrganisationId = groupEntity.FullId,
                                    UserId = memberId,
                                    Id = g.Id + "_" + memberId
                                };
                                await client.UpsertDocumentAsync(orgAccessesCol, newAccessEntity, new RequestOptions
                                {
                                    PartitionKey = new PartitionKey("OrganisationAccessEntity")
                                }, true);
                            }
                        }
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
            await stream.WriteAsync(deltaFileJson);
        }
    }
}