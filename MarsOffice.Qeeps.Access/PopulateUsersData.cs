using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Access.Entities;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MarsOffice.Qeeps.Access
{
    public class PopulateUsersData
    {
        private readonly GraphServiceClient _graphClient;
        private readonly IConfiguration _config;

        public PopulateUsersData(GraphServiceClient graphClient, IConfiguration config)
        {
            _graphClient = graphClient;
            _config = config;
        }

        [FunctionName("PopulateUsersData")]
        public async Task Run([TimerTrigger("%cron%", RunOnStartup = false)] TimerInfo timerInfo,
        [Blob("graph-api/delta_users.json", FileAccess.Read)] Stream deltaFile,
        [Blob("graph-api/delta_users.json", FileAccess.Write)] Stream deltaFileWrite,
        [CosmosDB(ConnectionStringSetting = "cdbconnectionstring")] DocumentClient client
        )
        {
            if (_config["ismain"] != "true")
            {
                return;
            }
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
                Id = "OrganisationAccesses",
                PartitionKey = new PartitionKeyDefinition
                {
                    Version = PartitionKeyDefinitionVersion.V1,
                    Paths = new System.Collections.ObjectModel.Collection<string>(new List<string>() { "/Partition" })
                }
            };
            await client.CreateDocumentCollectionIfNotExistsAsync(UriFactory.CreateDatabaseUri("access"), col);
#endif

            var usersCollection = UriFactory.CreateDocumentCollectionUri("access", "Users");

            var isDbEmpty = (await client.CreateDocumentQuery<UserEntity>(usersCollection, new FeedOptions
            {
                PartitionKey = new PartitionKey("UserEntity")
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
                await PopulateAllUsers(client);
                await PopulateDelta(client, deltaFileWrite, lastDelta);
            }
            else
            {
                await PopulateDelta(client, deltaFileWrite, lastDelta);
            }
        }

        private async Task PopulateAllUsers(DocumentClient client)
        {
            var usersCollectionUri = UriFactory.CreateDocumentCollectionUri("access", "Users");

            var usersRequest = _graphClient
                .Users
                .Request()
                .Select(x => new { x.Id, x.DisplayName, x.GivenName, x.CompanyName, x.Mail, x.Surname, x.UserPrincipalName });

            while (usersRequest != null)
            {
                var usersResponse = await usersRequest.GetAsync();
                foreach (var u in usersResponse)
                {
                    var newEntity = new UserEntity
                    {
                        Id = u.Id,
                        Name = u.DisplayName,
                        Email = u.Mail
                    };
                    await client.UpsertDocumentAsync(usersCollectionUri, newEntity, new RequestOptions
                    {
                        PartitionKey = new PartitionKey("UserEntity")
                    }, true);
                }
                usersRequest = usersResponse.NextPageRequest;
            }
        }

        private async Task PopulateDelta(DocumentClient client, Stream stream, string lastDelta)
        {
            var usersCollection = UriFactory.CreateDocumentCollectionUri("access", "Users");
            var orgAccessesCollection = UriFactory.CreateDocumentCollectionUri("access", "OrganisationAccesses");

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
                    var userUri = UriFactory.CreateDocumentUri("access", "Users", user.Id);
                    if (user.AdditionalData != null && user.AdditionalData.ContainsKey("@removed"))
                    {
                        try
                        {
                            await client.DeleteDocumentAsync(userUri, new RequestOptions
                            {
                                PartitionKey = new PartitionKey("UserEntity"),
                            });
                        }
                        catch (Exception)
                        {

                        }

                        var allAccessesToDeleteQuery = client.CreateDocumentQuery<OrganisationAccessEntity>(orgAccessesCollection, new FeedOptions
                        {
                            PartitionKey = new PartitionKey("OrganisationAccessEntity")
                        })
                        .Where(x => x.UserId == user.Id)
                        .AsDocumentQuery();

                        while (allAccessesToDeleteQuery.HasMoreResults)
                        {
                            var r = await allAccessesToDeleteQuery.ExecuteNextAsync<OrganisationAccessEntity>();
                            foreach (var tbd in r)
                            {
                                var uri = UriFactory.CreateDocumentUri("access", "OrganisationAccesses", tbd.Id);
                                await client.DeleteDocumentAsync(uri, new RequestOptions
                                {
                                    PartitionKey = new PartitionKey("OrganisationAccessEntity")
                                });
                            }
                        }
                    }
                    else
                    {
                        UserEntity existingUser = null;
                        try
                        {
                            existingUser = (await client.ReadDocumentAsync<UserEntity>(userUri, new RequestOptions
                            {
                                PartitionKey = new PartitionKey("UserEntity")
                            }))?.Document;
                        }
                        catch (Exception) { }

                        if (existingUser != null)
                        {
                            existingUser.Email = user.Mail;
                            existingUser.Name = user.DisplayName;
                        }
                        else
                        {
                            existingUser = new UserEntity
                            {
                                Email = user.Mail,
                                Name = user.DisplayName,
                                Id = user.Id
                            };
                        }
                        await client.UpsertDocumentAsync(usersCollection, existingUser, new RequestOptions
                        {
                            PartitionKey = new PartitionKey("UserEntity")
                        });
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