using System;
using System.Collections.Generic;
using System.IO;
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
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MarsOffice.Qeeps.Access
{
    public class Users
    {
        private readonly IMapper _mapper;
        private readonly IConfiguration _config;

        public Users(IMapper mapper, IConfiguration config)
        {
            _mapper = mapper;
            _config = config;
        }

        [FunctionName("GetUsers")]
        public async Task<IActionResult> GetUsers(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/access/users")] HttpRequest req,
            ClaimsPrincipal principal,
            [CosmosDB(
                ConnectionStringSetting = "cdbconnectionstring", PreferredLocations = "%location%")] DocumentClient client,
            ILogger log
            )
        {
            try
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

                var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Development";
                if (env != "Development" && principal.FindFirstValue("roles") != "Application")
                {
                    return new StatusCodeResult(401);
                }

                using var streamReader = new StreamReader(req.Body);
                var json = await streamReader.ReadToEndAsync();
                var ids = JsonConvert.DeserializeObject<IEnumerable<string>>(json);
                var colUri = UriFactory.CreateDocumentCollectionUri("access", "Users");
                var query = client.CreateDocumentQuery(colUri, new FeedOptions
                {
                    PartitionKey = new PartitionKey("UserEntity")
                })
                .Where(x => ids.Contains(x.Id))
                .AsDocumentQuery();

                var userDtos = new List<UserDto>();
                while (query.HasMoreResults)
                {
                    var userEntities = await query.ExecuteNextAsync<UserEntity>();
                    userDtos.AddRange(
                        _mapper.Map<IEnumerable<UserDto>>(userEntities)
                    );
                }

                return new OkObjectResult(userDtos);
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }

        [FunctionName("GetUser")]
        public async Task<IActionResult> GetUser(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/user/{id}")] HttpRequest req,
            ClaimsPrincipal principal,
            [CosmosDB(
                ConnectionStringSetting = "cdbconnectionstring", PreferredLocations = "%location%")] DocumentClient client,
            ILogger log
            )
        {
            try
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

                var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Development";
                if (env != "Development" && principal.FindFirstValue("roles") != "Application")
                {
                    return new StatusCodeResult(401);
                }
                var id = req.RouteValues["id"].ToString();
                UserEntity found = null;
                try
                {
                    var uri = UriFactory.CreateDocumentUri("access", "Users", id);
                    found = (
                        await client.ReadDocumentAsync<UserEntity>(uri, new RequestOptions
                        {
                            PartitionKey = new PartitionKey("UserEntity")
                        })
                    )?.Document;
                }
                catch (Exception) { }

                if (found == null)
                {
                    return new StatusCodeResult(404);
                }

                return new OkObjectResult(
                    _mapper.Map<UserDto>(found)
                );
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }

        [FunctionName("MyProfile")]
        public async Task<IActionResult> MyProfile(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/myProfile")] HttpRequest req,
            [CosmosDB(
                ConnectionStringSetting = "cdbconnectionstring", PreferredLocations = "%location%")] DocumentClient client,
            ILogger log
            )
        {
            try
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

                var principal = QeepsPrincipal.Parse(req);
                var id = principal.FindFirstValue("id");
                UserEntity found = null;
                try
                {
                    var collection = UriFactory.CreateDocumentCollectionUri("access", "Users");
                    var entityQuery = client.CreateDocumentQuery<UserEntity>(collection, new FeedOptions
                    {
                        PartitionKey = new PartitionKey("UserEntity")
                    }).Where(x => x.Id == id)
                    .Select(x => new UserEntity
                    {
                        Email = x.Email,
                        HasSignedContract = x.HasSignedContract,
                        Id = x.Id,
                        Name = x.Name
                    }).AsDocumentQuery();
                    var response = await entityQuery.ExecuteNextAsync<UserEntity>();
                    found = response.FirstOrDefault();
                }
                catch (Exception) { }

                if (found == null)
                {
                    return new StatusCodeResult(404);
                }
                found.UserPreferences = null;
                return new OkObjectResult(
                    _mapper.Map<UserDto>(found)
                );
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }

        [FunctionName("UpdateMyProfile")]
        public async Task<IActionResult> UpdateMyProfile(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "api/access/myProfile")] HttpRequest req,
            [CosmosDB(
                ConnectionStringSetting = "cdbconnectionstring", PreferredLocations = "%location%")] DocumentClient client,
            ILogger log
            )
        {
            try
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

                var principal = QeepsPrincipal.Parse(req);
                var userId = principal.FindFirst("id").Value;

                var docId = UriFactory.CreateDocumentUri("access", "Users", userId);

                UserEntity existingUser = null;
                try
                {
                    existingUser = (await client.ReadDocumentAsync<UserEntity>(docId, new RequestOptions
                    {
                        PartitionKey = new PartitionKey("UserEntity")
                    }))?.Document;
                }
                catch (Exception) { }

                if (existingUser == null)
                {
                    return new StatusCodeResult(400);
                }

                var json = string.Empty;
                using (var streamReader = new StreamReader(req.Body))
                {
                    json = await streamReader.ReadToEndAsync();
                }
                var payload = JsonConvert.DeserializeObject<UserDto>(json, new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });

                existingUser.HasSignedContract = payload.HasSignedContract;
                var collection = UriFactory.CreateDocumentCollectionUri("access", "Users");
                await client.UpsertDocumentAsync(collection, existingUser, new RequestOptions
                {
                    PartitionKey = new PartitionKey("UserEntity")
                }, true);

                return new OkResult();
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }
    }
}
