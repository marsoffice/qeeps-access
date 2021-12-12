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
        public async Task Run([TimerTrigger("%cron%", RunOnStartup = 
        #if DEBUG
        false
        #else
        true
        #endif
        )] TimerInfo timerInfo,
        [Blob("graph-api/delta_users.json", FileAccess.Read)] string deltaFile,
        [Blob("graph-api/delta_users.json", FileAccess.Write)] TextWriter deltaFileWriter,
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
#endif

            var usersCollection = UriFactory.CreateDocumentCollectionUri("access", "Users");

            var isDbEmpty = (await client.CreateDocumentQuery<UserEntity>(usersCollection, new FeedOptions
            {
                PartitionKey = new PartitionKey("UserEntity")
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

            var adAppRequest = _graphClient
                .Applications
                .Request()
                .Filter($"appId eq '{_config["adappid"]}'");

            var adAppResponse = await adAppRequest.GetAsync();
            var adApp = adAppResponse.Single();

            if (isDbEmpty)
            {
                await PopulateAllUsers(client, adApp);
                await PopulateDelta(client, deltaFileWriter, lastDelta, adApp);
            }
            else
            {
                await PopulateDelta(client, deltaFileWriter, lastDelta, adApp);
            }
        }

        private async Task PopulateAllUsers(DocumentClient client, Application adApp)
        {
            var allValidRoleIds = adApp.AppRoles.Select(x => x.Id.Value.ToString()).Distinct().ToList();
            var usersCollectionUri = UriFactory.CreateDocumentCollectionUri("access", "Users");

            var usersRequest = _graphClient
                .Users
                .Request()
                .Expand(x => x.AppRoleAssignments)
                .Select(x => new { x.Id, x.DisplayName, x.AccountEnabled, x.GivenName, x.CompanyName, x.Mail, x.Surname, x.UserPrincipalName, x.AppRoleAssignments });

            var tasks = new List<Task<ResourceResponse<Document>>>();

            while (usersRequest != null)
            {
                var usersResponse = await usersRequest.GetAsync();
                foreach (var u in usersResponse)
                {
                    var foundRoles = u.AppRoleAssignments?.Where(ara => allValidRoleIds.Contains(ara.AppRoleId.Value.ToString()))
                    .Select(x => adApp.AppRoles.First(z => z.Id.Value.ToString() == x.AppRoleId.Value.ToString()).DisplayName)
                    .Distinct()
                    .ToList();
                    var newEntity = new UserEntity
                    {
                        Id = u.Id,
                        Name = u.DisplayName,
                        Email = u.Mail,
                        IsDisabled = u.AccountEnabled != true || foundRoles == null || !foundRoles.Any(),
                        Roles = foundRoles
                    };
                    tasks.Add(client.UpsertDocumentAsync(usersCollectionUri, newEntity, new RequestOptions
                    {
                        PartitionKey = new PartitionKey("UserEntity")
                    }, true));
                }
                usersRequest = usersResponse.NextPageRequest;
            }
            await Task.WhenAll(tasks);
        }

        private async Task PopulateDelta(DocumentClient client, TextWriter stream, string lastDelta, Application adApp)
        {
            var allValidRoleIds = adApp.AppRoles.Select(x => x.Id.Value.ToString()).Distinct().ToList();

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
                    nextDelta = response.AdditionalData["@odata.deltaLink"].ToString();
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

                        // TODO get app roles for user
                        var fullUserRequest = _graphClient
                            .Users
                            .Request()
                            .Filter($"id eq '{user.Id}'")
                            .Expand(x => x.AppRoleAssignments)
                            .Select(x => new { x.AppRoleAssignments });

                        var fullUserResponse = await fullUserRequest.GetAsync();

                        var userAppRoles = fullUserResponse.First().AppRoleAssignments;

                        var foundRoles = userAppRoles?.Where(ara => allValidRoleIds.Contains(ara.AppRoleId.Value.ToString()))
                            .Select(x => adApp.AppRoles.First(z => z.Id.Value.ToString() == x.AppRoleId.Value.ToString()).DisplayName)
                            .Distinct()
                            .ToList();
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
                            existingUser.IsDisabled = user.AccountEnabled != true || foundRoles == null || !foundRoles.Any();
                            existingUser.Roles = foundRoles;
                        }
                        else
                        {
                            existingUser = new UserEntity
                            {
                                Email = user.Mail,
                                Name = user.DisplayName,
                                Id = user.Id,
                                HasSignedContract = false,
                                IsDisabled = user.AccountEnabled != true || foundRoles == null || !foundRoles.Any(),
                                Partition = "UserEntity",
                                Roles = foundRoles
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
            await stream.WriteAsync(deltaFileJson);
        }
    }
}