using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Access.Entities;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;

namespace MarsOffice.Qeeps.Access
{
    public class SeedAccessDataFromAd
    {
        private readonly GraphServiceClient _graphClient;
        private readonly IConfiguration _config;

        public SeedAccessDataFromAd(GraphServiceClient graphClient, IConfiguration config)
        {
            _graphClient = graphClient;
            _config = config;
        }

        [FunctionName("SeedAccessDataFromAd")]
        public async Task Run([TimerTrigger("%adseedcron%", RunOnStartup = true
        )] TimerInfo timerInfo,
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

            var adAppRequest = _graphClient
                .Applications
                .Request()
                .Filter($"appId eq '{_config["adappid"]}'");

            var adAppResponse = await adAppRequest.GetAsync();
            var adApp = adAppResponse.Single();

            if (noUsersExistInDb && noOrgsExistInDb)
            {
                await PopulateAllUsers(client, adApp);
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
                    if (foundRoles == null || !foundRoles.Any())
                    {
                        continue;
                    }

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
    }
}