using System.IO;
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
                #if DEBUG
                CreateIfNotExists = true,
                PartitionKey = "UserId",
                #endif
                ConnectionStringSetting = "cdbconnectionstring")] DocumentClient client,
                ClaimsPrincipal principal
            )
        {
            if (!principal.HasClaim(x => x.Type == "id"))
            {
                principal = QeepsPrincipal.Parse(req);
            }
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
                ConnectionStringSetting = "cdbconnectionstring")] IAsyncCollector<UserPreferencesEntity> userPreferencesOut,
                ClaimsPrincipal principal
            )
        {
            if (!principal.HasClaim(x => x.Type == "id"))
            {
                principal = QeepsPrincipal.Parse(req);
            }
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
    }
}
