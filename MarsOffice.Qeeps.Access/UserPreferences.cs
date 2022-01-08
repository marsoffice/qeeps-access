using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AutoMapper;
using MarsOffice.Microfunction;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Access.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
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
                    return new StatusCodeResult(400);
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
                var entity = _mapper.Map<UserPreferencesEntity>(payload);

                _mapper.Map(payload, entity);

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
    }
}
