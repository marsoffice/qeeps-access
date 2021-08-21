using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AutoMapper;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Access.Entities;
using MarsOffice.Qeeps.Microfunction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
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
                ConnectionStringSetting = "cdbconnectionstring")] DocumentClient client
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
#endif
            var principal = QeepsPrincipal.Parse(req);
            var userId = principal.FindFirst("id").Value;
            var docId = UriFactory.CreateDocumentUri("access", "Users", userId);

            UserEntity foundSettingsResponse = null;
            try
            {
                foundSettingsResponse = (await client.ReadDocumentAsync<UserEntity>(docId, new RequestOptions
                {
                    PartitionKey = new PartitionKey("UserEntity")
                }))?.Document;
            }
            catch (Exception) { }
            if (foundSettingsResponse?.UserPreferences == null)
            {
                return new JsonResult(null);
            }
            return new JsonResult(_mapper.Map<UserPreferencesDto>(foundSettingsResponse.UserPreferences), new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
        }

        [FunctionName("SaveUserPreferences")]
        public async Task<IActionResult> SaveUserPreferences(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/access/userPreferences")] HttpRequest req,
            [CosmosDB(
                ConnectionStringSetting = "cdbconnectionstring")] DocumentClient client
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
            var payload = JsonConvert.DeserializeObject<UserPreferencesDto>(json, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
            var entity = _mapper.Map<UserPreferencesEntity>(payload);

            existingUser.UserPreferences = entity;

            await client.UpsertDocumentAsync(docId, existingUser, new RequestOptions {
                PartitionKey = new PartitionKey("UserEntity")
            }, true);

            return new OkResult();
        }
    }
}
