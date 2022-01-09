using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using MarsOffice.Microfunction;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Access.Entities;
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
    public class UserPreferences
    {
        private readonly IMapper _mapper;
        private readonly IConfiguration _config;

        public UserPreferences(IMapper mapper, IConfiguration config)
        {
            _config = config;
            _mapper = mapper;
        }

        [FunctionName("GetUserPreferences")]
        public async Task<IActionResult> GetUserPreferences(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/userPreferences")] HttpRequest req,
            [CosmosDB(ConnectionStringSetting = "cdbconnectionstring", PreferredLocations = "%location%")] DocumentClient client,
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
                    Id = "UserPreferences",
                    PartitionKey = new PartitionKeyDefinition
                    {
                        Version = PartitionKeyDefinitionVersion.V2,
                        Paths = new System.Collections.ObjectModel.Collection<string>(new List<string>() { "/Partition" })
                    }
                };
                await client.CreateDocumentCollectionIfNotExistsAsync(UriFactory.CreateDatabaseUri("access"), col);
#endif
                var principal = MarsOfficePrincipal.Parse(req);
                var userId = principal.FindFirst("id").Value;
                var docId = UriFactory.CreateDocumentUri("access", "UserPreferences", userId);

                UserPreferencesEntity foundSettingsResponse = null;
                try
                {
                    foundSettingsResponse = (await client.ReadDocumentAsync<UserPreferencesEntity>(docId, new RequestOptions
                    {
                        PartitionKey = new PartitionKey("UserPreferencesEntity")
                    }))?.Document;
                }
                catch (Exception) { }
                if (foundSettingsResponse == null)
                {
                    return new JsonResult(null);
                }
                return new JsonResult(_mapper.Map<UserPreferencesDto>(foundSettingsResponse), new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }

        [FunctionName("SaveUserPreferences")]
        public async Task<IActionResult> SaveUserPreferences(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/access/userPreferences")] HttpRequest req,
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
                    Id = "UserPreferences",
                    PartitionKey = new PartitionKeyDefinition
                    {
                        Version = PartitionKeyDefinitionVersion.V2,
                        Paths = new System.Collections.ObjectModel.Collection<string>(new List<string>() { "/Partition" })
                    }
                };
                await client.CreateDocumentCollectionIfNotExistsAsync(UriFactory.CreateDatabaseUri("access"), col);
#endif

                var principal = MarsOfficePrincipal.Parse(req);
                var userId = principal.FindFirst("id").Value;

                var docId = UriFactory.CreateDocumentUri("access", "UserPreferences", userId);

                UserPreferencesEntity existingPrefs = null;
                try
                {
                    existingPrefs = (await client.ReadDocumentAsync<UserPreferencesEntity>(docId, new RequestOptions
                    {
                        PartitionKey = new PartitionKey("UserPreferencesEntity")
                    }))?.Document;
                }
                catch (Exception) { }

                if (existingPrefs == null)
                {
                    existingPrefs = new UserPreferencesEntity();
                }

                var json = string.Empty;
                using (var streamReader = new StreamReader(req.Body))
                {
                    json = await streamReader.ReadToEndAsync();
                }
                var payload = JsonConvert.DeserializeObject<UserPreferencesDto>(json, new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });

                existingPrefs.Id = userId;
                existingPrefs.PreferredLanguage = payload.PreferredLanguage;
                existingPrefs.UseDarkTheme = payload.UseDarkTheme;

                var collection = UriFactory.CreateDocumentCollectionUri("access", "UserPreferences");
                await client.UpsertDocumentAsync(collection, existingPrefs, new RequestOptions
                {
                    PartitionKey = new PartitionKey("UserPreferencesEntity")
                }, true);

                return new OkResult();
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }

        [FunctionName("GetUserPreferencesByUserIds")]
        public async Task<IActionResult> GetUserPreferencesByUserIds(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/access/userPreferencesByUserIds")] HttpRequest req,
            [CosmosDB(ConnectionStringSetting = "cdbconnectionstring", PreferredLocations = "%location%")] DocumentClient client,
            ILogger log,
            ClaimsPrincipal principal
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
                    Id = "UserPreferences",
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

                var json = string.Empty;
                using (var streamReader = new StreamReader(req.Body))
                {
                    json = await streamReader.ReadToEndAsync();
                }
                var payload = JsonConvert.DeserializeObject<IEnumerable<string>>(json, new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });

                var userPrefsCol = UriFactory.CreateDocumentCollectionUri("access", "userPreferences");
                var query = client.CreateDocumentQuery<UserPreferencesEntity>(userPrefsCol, new FeedOptions
                {
                    PartitionKey = new PartitionKey("UserPreferencesEntity")
                })
                .Where(x => payload.Contains(x.Id))
                .AsDocumentQuery();

                var dtos = new List<UserPreferencesDto>();
                while (query.HasMoreResults) {
                    var r = await query.ExecuteNextAsync<UserPreferencesEntity>();
                    dtos.AddRange(
                        _mapper.Map<IEnumerable<UserPreferencesDto>>(r)
                    );
                }
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
