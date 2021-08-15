using System.Text.Json;
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
        public async Task<UserPreferencesDto> GetUserPreferences(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/userPreferences")] HttpRequest req,
            [CosmosDB(
                databaseName: "access",
                collectionName: "UserPreferences",
                #if DEBUG
                CreateIfNotExists = true,
                PartitionKey = "UserId",
                #endif
                ConnectionStringSetting = "cdbconnectionstring")] DocumentClient client
            )
        {
            var userId = QeepsPrincipal.Parse(req).FindFirst("id").Value;
            var collectionUri = UriFactory.CreateDocumentUri("access", "UserPreferences", userId);
            var foundSettingsResponse = await client.ReadDocumentAsync<UserPreferencesEntity>(collectionUri, new RequestOptions {
                PartitionKey = new PartitionKey(userId)
            });
            if (foundSettingsResponse.Document == null)
            {
                return null;
            }
            return _mapper.Map<UserPreferencesDto>(foundSettingsResponse.Document);
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
            var userId = QeepsPrincipal.Parse(req).FindFirst("id").Value;
            var payload = await JsonSerializer.DeserializeAsync<UserPreferencesDto>(req.Body);
            var entity = _mapper.Map<UserPreferencesEntity>(payload);
            entity.UserId = userId;
            entity.Id = userId;
            await userPreferencesOut.AddAsync(entity);
            return new OkResult();
        }
    }
}
