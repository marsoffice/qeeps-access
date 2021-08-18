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
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MarsOffice.Qeeps.Access
{
    public class UserPreferences
    {
        private readonly IMapper _mapper;
        public UserPreferences(IMapper mapper)
        {
            _mapper = mapper;
        }

        [FunctionName("GetUserPreferences")]
        public async Task<IActionResult> GetUserPreferences(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/userPreferences")] HttpRequest req,
            [CosmosDB(
                databaseName: "access",
                collectionName: "UserPreferences",
                ConnectionStringSetting = "cdbconnectionstring")] DocumentClient client
            )
        {
            #if DEBUG
            var db = new Database
            {
                Id = "access"
            };
            await client.CreateDatabaseIfNotExistsAsync(db);

            var col = new DocumentCollection {
                Id = "UserPreferences"
            };
            await client.CreateDocumentCollectionIfNotExistsAsync(UriFactory.CreateDatabaseUri("access"), col);
            #endif
            var principal = QeepsPrincipal.Parse(req);
            var userId = principal.FindFirst("id").Value;
            var collectionUri = UriFactory.CreateDocumentUri("access", "UserPreferences", userId);
            var foundSettingsResponse = await client.ReadDocumentAsync<UserPreferencesEntity>(collectionUri, new RequestOptions
            {
                PartitionKey = new PartitionKey(userId)
            });
            if (foundSettingsResponse.Document == null)
            {
                return new JsonResult(null);
            }
            return new JsonResult(_mapper.Map<UserPreferencesDto>(foundSettingsResponse.Document), new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
        }

        [FunctionName("SaveUserPreferences")]
        public async Task<IActionResult> SaveUserPreferences(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/access/userPreferences")] HttpRequest req,
            [CosmosDB(
                databaseName: "access",
                collectionName: "UserPreferences",
                #if DEBUG
                CreateIfNotExists = true,
                PartitionKey = "/UserId",
                #endif
                ConnectionStringSetting = "cdbconnectionstring")] IAsyncCollector<UserPreferencesEntity> userPreferencesOut
            )
        {
            var principal = QeepsPrincipal.Parse(req);
            var userId = principal.FindFirst("id").Value;
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
            entity.UserId = userId;
            entity.Id = userId;
            await userPreferencesOut.AddAsync(entity);
            return new OkResult();
        }


        [FunctionName("GetPrefencesForUsers")]
        public async Task<IActionResult> GetPrefencesForUsers(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/access/preferences")] HttpRequest req,
            [CosmosDB(
                databaseName: "access",
                collectionName: "UserPreferences",
                ConnectionStringSetting = "cdbconnectionstring")] DocumentClient client,
                ClaimsPrincipal principal
            )
        {
            #if DEBUG
            var db = new Database
            {
                Id = "access"
            };
            await client.CreateDatabaseIfNotExistsAsync(db);

            var col = new DocumentCollection {
                Id = "UserPreferences"
            };
            await client.CreateDocumentCollectionIfNotExistsAsync(UriFactory.CreateDatabaseUri("access"), col);
            #endif
            var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Development";
            if (env != "Development" && principal.FindFirstValue("roles") != "Application")
            {
                return new StatusCodeResult(401);
            }
            var collectionUri = UriFactory.CreateDocumentCollectionUri("access", "UserPreferences");

            using var streamReader = new StreamReader(req.Body);
            var json = await streamReader.ReadToEndAsync();
            var ids = JsonConvert.DeserializeObject<IEnumerable<string>>(json);
            var preferences = new List<UserPreferencesDto>();
            foreach (var uid in ids)
            {
                var response = client.CreateDocumentQuery<UserPreferencesEntity>(collectionUri, new FeedOptions {
                    PartitionKey = new PartitionKey(uid)
                })
                    .Where(x => x.Id == uid)
                    .AsDocumentQuery();

                while (response.HasMoreResults)
                {
                    var data = await response.ExecuteNextAsync<UserPreferencesEntity>();
                    if (data != null)
                    {
                        preferences.AddRange(_mapper.Map<IEnumerable<UserPreferencesDto>>(data.ToList()));
                    }
                }
            }
            return new JsonResult(_mapper.Map<IEnumerable<UserPreferencesDto>>(preferences), new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
        }
    }
}
