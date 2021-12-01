using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Access.Entities;
using MarsOffice.Qeeps.Microfunction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MarsOffice.Qeeps.Access
{
    public class Organisations
    {
        private readonly IMapper _mapper;
        private readonly IConfiguration _config;

        public Organisations(IMapper mapper, IConfiguration config)
        {
            _mapper = mapper;
            _config = config;
        }

        [FunctionName("MyOrganisationsTree")]
        public async Task<IActionResult> GetMyOrganisationsTree(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/myOrganisationsTree")] HttpRequest req,
            [CosmosDB(ConnectionStringSetting = "cdbconnectionstring", PreferredLocations = "%location%")] DocumentClient client,
            ILogger log
            )
        {
            try
            {
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
                client.ConnectionPolicy.UseMultipleWriteLocations = _config.GetValue<bool>("multimasterdatabase");
                var principal = QeepsPrincipal.Parse(req);
                var uid = principal.FindFirstValue("id");
                var groupIds = principal.FindAll(x => x.Type == "groups").Select(x => x.Value).Distinct().ToList();
                var orgAccessesCollection = UriFactory.CreateDocumentCollectionUri("access", "OrganisationAccesses");
                var foundAccessesQuery = client.CreateDocumentQuery<OrganisationAccessEntity>(orgAccessesCollection, new FeedOptions
                {
                    PartitionKey = new PartitionKey("OrganisationAccessEntity")
                })
                .Where(x => x.UserId == uid && groupIds.Contains(x.OrganisationId))
                .AsDocumentQuery();

                var entities = new List<OrganisationAccessEntity>();
                while (foundAccessesQuery.HasMoreResults)
                {
                    entities.AddRange(await foundAccessesQuery.ExecuteNextAsync<OrganisationAccessEntity>());
                }
                var orgIds = entities.Select(x => x.OrganisationId).Distinct().ToList();
                var orgsCollection = UriFactory.CreateDocumentCollectionUri("access", "Organisations");
                var foundOrgsQuery = client.CreateDocumentQuery<OrganisationEntity>(orgsCollection, new FeedOptions
                {
                    PartitionKey = new PartitionKey("OrganisationEntity")
                })
                .Where(x => orgIds.Contains(x.Id))
                .AsDocumentQuery();

                var dtos = new List<OrganisationDto>();

                while (foundOrgsQuery.HasMoreResults)
                {
                    dtos.AddRange(_mapper.Map<IEnumerable<OrganisationDto>>(await foundOrgsQuery.ExecuteNextAsync<OrganisationEntity>()));
                }

                return new OkObjectResult(dtos);
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }

        [FunctionName("MyFullOrganisationsTree")]
        public async Task<IActionResult> GetMyFullOrganisationsTree(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/myFullOrganisationsTree")] HttpRequest req,
            [CosmosDB(ConnectionStringSetting = "cdbconnectionstring", PreferredLocations = "%location%")] DocumentClient client,
            ILogger log
            )
        {
            try
            {
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
                client.ConnectionPolicy.UseMultipleWriteLocations = _config.GetValue<bool>("multimasterdatabase");
                var principal = QeepsPrincipal.Parse(req);
                var uid = principal.FindFirstValue("id");
                var groupIds = principal.FindAll(x => x.Type == "groups").Select(x => x.Value).Distinct().ToList();
                var orgAccessesCollection = UriFactory.CreateDocumentCollectionUri("access", "OrganisationAccesses");
                var foundAccessesQuery = client.CreateDocumentQuery<OrganisationAccessEntity>(orgAccessesCollection, new FeedOptions
                {
                    PartitionKey = new PartitionKey("OrganisationAccessEntity")
                })
                .Where(x => x.UserId == uid && groupIds.Contains(x.OrganisationId))
                .AsDocumentQuery();

                var entities = new List<OrganisationAccessEntity>();
                while (foundAccessesQuery.HasMoreResults)
                {
                    entities.AddRange(await foundAccessesQuery.ExecuteNextAsync<OrganisationAccessEntity>());
                }


                var dtos = new List<OrganisationDto>();
                var orgIds = entities.Select(x => x.OrganisationId).Distinct().ToList();
                var orgsCollection = UriFactory.CreateDocumentCollectionUri("access", "Organisations");

                foreach (var orgId in orgIds)
                {
                    var foundOrgsQuery = client.CreateDocumentQuery<OrganisationEntity>(orgsCollection, new FeedOptions
                    {
                        PartitionKey = new PartitionKey("OrganisationEntity")
                    })
                    .Where(x => x.FullId.Contains("_" + orgId))
                    .AsDocumentQuery();

                    while (foundOrgsQuery.HasMoreResults)
                    {
                        dtos.AddRange(_mapper.Map<IEnumerable<OrganisationDto>>(await foundOrgsQuery.ExecuteNextAsync<OrganisationEntity>()));
                    }
                }
                dtos = dtos.DistinctBy(x => x.FullId).ToList();
                return new OkObjectResult(dtos);
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }


        [FunctionName("GetFullOrganisationsTreeForUser")]
        public async Task<IActionResult> GetFullOrganisationsTreeForUser(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/getFullOrganisationsTree/{userId}")] HttpRequest req,
            [CosmosDB(ConnectionStringSetting = "cdbconnectionstring", PreferredLocations = "%location%")] DocumentClient client,
            ILogger log,
            ClaimsPrincipal principal
            )
        {
            try
            {
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
                client.ConnectionPolicy.UseMultipleWriteLocations = _config.GetValue<bool>("multimasterdatabase");

                var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Development";
                if (env != "Development" && principal.FindFirstValue("roles") != "Application")
                {
                    return new StatusCodeResult(401);
                }
                var uid = req.RouteValues["userId"].ToString();

                var orgAccessesCollection = UriFactory.CreateDocumentCollectionUri("access", "OrganisationAccesses");
                var foundAccessesQuery = client.CreateDocumentQuery<OrganisationAccessEntity>(orgAccessesCollection, new FeedOptions
                {
                    PartitionKey = new PartitionKey("OrganisationAccessEntity")
                })
                .Where(x => x.UserId == uid)
                .AsDocumentQuery();

                var entities = new List<OrganisationAccessEntity>();
                while (foundAccessesQuery.HasMoreResults)
                {
                    entities.AddRange(await foundAccessesQuery.ExecuteNextAsync<OrganisationAccessEntity>());
                }


                var dtos = new List<OrganisationDto>();
                var orgIds = entities.Select(x => x.OrganisationId).Distinct().ToList();
                var orgsCollection = UriFactory.CreateDocumentCollectionUri("access", "Organisations");

                foreach (var orgId in orgIds)
                {
                    var foundOrgsQuery = client.CreateDocumentQuery<OrganisationEntity>(orgsCollection, new FeedOptions
                    {
                        PartitionKey = new PartitionKey("OrganisationEntity")
                    })
                    .Where(x => x.FullId.Contains("_" + orgId))
                    .AsDocumentQuery();

                    while (foundOrgsQuery.HasMoreResults)
                    {
                        dtos.AddRange(_mapper.Map<IEnumerable<OrganisationDto>>(await foundOrgsQuery.ExecuteNextAsync<OrganisationEntity>()));
                    }
                }
                dtos = dtos.DistinctBy(x => x.FullId).ToList();
                return new OkObjectResult(dtos);
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }

        [FunctionName("GetAccessibleOrganisationsForUser")]
        public async Task<IActionResult> GetAccessibleOrganisationsForUser(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/getAccessibleOrganisations/{userId}")] HttpRequest req,
            [CosmosDB(ConnectionStringSetting = "cdbconnectionstring", PreferredLocations = "%location%")] DocumentClient client,
            ILogger log,
            ClaimsPrincipal principal
            )
        {
            try
            {
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
                client.ConnectionPolicy.UseMultipleWriteLocations = _config.GetValue<bool>("multimasterdatabase");

                var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Development";
                if (env != "Development" && principal.FindFirstValue("roles") != "Application")
                {
                    return new StatusCodeResult(401);
                }
                var uid = req.RouteValues["userId"].ToString();

                var orgAccessesCollection = UriFactory.CreateDocumentCollectionUri("access", "OrganisationAccesses");
                var foundAccessesQuery = client.CreateDocumentQuery<OrganisationAccessEntity>(orgAccessesCollection, new FeedOptions
                {
                    PartitionKey = new PartitionKey("OrganisationAccessEntity")
                })
                .Where(x => x.UserId == uid)
                .AsDocumentQuery();

                var entities = new List<OrganisationAccessEntity>();
                while (foundAccessesQuery.HasMoreResults)
                {
                    entities.AddRange(await foundAccessesQuery.ExecuteNextAsync<OrganisationAccessEntity>());
                }


                var dtos = new List<OrganisationDto>();
                var orgIds = entities.Select(x => x.OrganisationId).Distinct().ToList();
                var orgsCollection = UriFactory.CreateDocumentCollectionUri("access", "Organisations");

                foreach (var orgId in orgIds)
                {
                    var foundOrgsQuery = client.CreateDocumentQuery<OrganisationEntity>(orgsCollection, new FeedOptions
                    {
                        PartitionKey = new PartitionKey("OrganisationEntity")
                    })
                    .Where(x => x.FullId.Contains("_" + orgId))
                    .AsDocumentQuery();

                    while (foundOrgsQuery.HasMoreResults)
                    {
                        dtos.AddRange(_mapper.Map<IEnumerable<OrganisationDto>>(await foundOrgsQuery.ExecuteNextAsync<OrganisationEntity>()));
                    }
                }
                dtos = dtos.DistinctBy(x => x.FullId).ToList();
                return new OkObjectResult(dtos);
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }
    }
}
