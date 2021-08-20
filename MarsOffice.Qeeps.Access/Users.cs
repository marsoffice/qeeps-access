using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Access.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using StackExchange.Redis;

namespace MarsOffice.Qeeps.Access
{
    public class Users
    {
        private readonly IDatabase _database;
        public Users(Lazy<IConnectionMultiplexer> mux, IConfiguration config)
        {
            _database = mux.Value.GetDatabase(config.GetValue<int>("redisdatabase_users"));
        }

        [FunctionName("GetUsers")]
        public async Task<IActionResult> GetUsers(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/access/users")] HttpRequest req,
            ClaimsPrincipal principal
            )
        {
            var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Development";
            if (env != "Development" && principal.FindFirstValue("roles") != "Application")
            {
                return new StatusCodeResult(401);
            }

            using var streamReader = new StreamReader(req.Body);
            var json = await streamReader.ReadToEndAsync();
            var ids = JsonConvert.DeserializeObject<IEnumerable<string>>(json);

            var userDtos = new List<UserDto>();
            foreach (var id in ids)
            {
                var value = await _database.StringGetAsync(id);
                if (!string.IsNullOrEmpty(value))
                {
                    userDtos.Add(
                        JsonConvert.DeserializeObject<UserDto>(value, new JsonSerializerSettings
                        {
                            ContractResolver = new CamelCasePropertyNamesContractResolver()
                        })
                    );
                }
            }
            return new OkObjectResult(userDtos);
        }

        [FunctionName("GetUser")]
        public async Task<IActionResult> GetUser(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/user/{id}")] HttpRequest req,
            ClaimsPrincipal principal
            )
        {
            var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Development";
            if (env != "Development" && principal.FindFirstValue("roles") != "Application")
            {
                return new StatusCodeResult(401);
            }
            var id = req.RouteValues["id"].ToString();
            var value = await _database.StringGetAsync(id);
            if (string.IsNullOrEmpty(value))
            {
                return new NotFoundResult();
            }
            var userDto = JsonConvert.DeserializeObject<UserDto>(value, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
            return new OkObjectResult(userDto);
        }
    }
}
