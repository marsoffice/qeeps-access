using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using MarsOffice.Dto;
using MarsOffice.Microfunction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MarsOffice.Qeeps.Access
{
    public class Users
    {
        private readonly IMapper _mapper;
        private readonly HttpClient _opaClient;

        public Users(IMapper mapper, IHttpClientFactory httpClientFactory)
        {
            _mapper = mapper;
            _opaClient = httpClientFactory.CreateClient("OPA");
        }

        [FunctionName("GetUsers")]
        public async Task<IActionResult> GetUsers(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/access/users")] HttpRequest req,
            ClaimsPrincipal principal,
            ILogger log
            )
        {
            try
            {
                var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Development";
                if (env != "Development" && principal.FindFirstValue("roles") != "Application")
                {
                    return new StatusCodeResult(401);
                }

                using var streamReader = new StreamReader(req.Body);
                var json = await streamReader.ReadToEndAsync();
                var ids = JsonConvert.DeserializeObject<IEnumerable<string>>(json);

                var opaResponse = await _opaClient.PostAsJsonAsync("/v1/data/usr/getUsersByIds", new {
                    ids
                });
                var opaJson = await opaResponse.Content.ReadAsStringAsync();
                var opaResponseData = JsonConvert.DeserializeObject<IEnumerable<UserDto>>(opaJson);

                return new OkObjectResult(opaResponseData);
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
            ILogger log
            )
        {
            try
            {
                var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Development";
                if (env != "Development" && principal.FindFirstValue("roles") != "Application")
                {
                    return new StatusCodeResult(401);
                }
                var id = req.RouteValues["id"].ToString();

                var opaResponse = await _opaClient.PostAsJsonAsync("/v1/data/usr/getUsersByIds", new
                {
                    ids = new [] { id }
                });
                var opaJson = await opaResponse.Content.ReadAsStringAsync();
                var opaResponseData = JsonConvert.DeserializeObject<IEnumerable<UserDto>>(opaJson);

                return new OkObjectResult(
                    _mapper.Map<UserDto>(opaResponseData.ElementAt(0))
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
            ILogger log
            )
        {
            try
            {
               
                var principal = MarsOfficePrincipal.Parse(req);
                var id = principal.FindFirstValue("id");
                var opaResponse = await _opaClient.PostAsJsonAsync("/v1/data/usr/getUsersByIds", new
                {
                    ids = new[] { id }
                });
                var opaJson = await opaResponse.Content.ReadAsStringAsync();
                var opaResponseData = JsonConvert.DeserializeObject<IEnumerable<UserDto>>(opaJson);

                return new OkObjectResult(
                    _mapper.Map<UserDto>(opaResponseData.ElementAt(0))
                );
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }

        [FunctionName("GetUsersByOrganisationId")]
        public async Task<IActionResult> GetUsersByOrganisationId(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/getUsersByOrganisationId/{organisationId}")] HttpRequest req,
            ClaimsPrincipal principal,
            ILogger log
            )
        {
            try {
                var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Development";
                if (env != "Development" && principal.FindFirstValue("roles") != "Application")
                {
                    return new StatusCodeResult(401);
                }
                var organisationId = req.RouteValues["organisationId"].ToString();
                var opaResponse = await _opaClient.PostAsJsonAsync("/v1/data/grp/getUsersByGroupId", new
                {
                    id = organisationId
                });
                var opaJson = await opaResponse.Content.ReadAsStringAsync();
                var opaResponseData = JsonConvert.DeserializeObject<IEnumerable<UserDto>>(opaJson);
                return new OkObjectResult(opaResponseData);
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }
    }
}