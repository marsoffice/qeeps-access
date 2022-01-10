using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using MarsOffice.Dto;
using MarsOffice.Microfunction;
using MarsOffice.Qeeps.Access.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MarsOffice.Qeeps.Access
{

    public class Organisations
    {
        private readonly IMapper _mapper;
        private readonly HttpClient _opaClient;
        public Organisations(IHttpClientFactory httpClientFactory, IMapper mapper)
        {
            _opaClient = httpClientFactory.CreateClient("OPA");
            _mapper = mapper;
        }

        [FunctionName("MyOrganisationsTree")]
        public async Task<IActionResult> GetMyOrganisationsTree(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/myOrganisationsTree")] HttpRequest req,
            ILogger log
            )
        {
            try
            {
                var principal = MarsOfficePrincipal.Parse(req);
                var uid = principal.FindFirstValue("id");

                var opaPayload = new StringContent(JsonConvert.SerializeObject(new OpaInputDto<OpaIdDto>
                {
                    Input = new OpaIdDto
                    {
                        Id = uid
                    }
                }, new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() }));
                var opaResponse = await _opaClient.PostAsync("/v1/data/grp/getUserGroupsByUserId", opaPayload);
                opaResponse.EnsureSuccessStatusCode();
                var opaJson = await opaResponse.Content.ReadAsStringAsync();
                log.LogInformation("OPARESPONSE: " + opaJson);
                var opaResponseData = JsonConvert.DeserializeObject<OpaResponseDto<IEnumerable<GroupDto>>>(opaJson, new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });

                return new OkObjectResult(_mapper.Map<IEnumerable<OrganisationDto>>(opaResponseData.Result));
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
            ILogger log
            )
        {
            try
            {
                var principal = MarsOfficePrincipal.Parse(req);
                var uid = principal.FindFirstValue("id");
                var opaPayload = new StringContent(JsonConvert.SerializeObject(new OpaInputDto<OpaIdDto>
                {
                    Input = new OpaIdDto
                    {
                        Id = uid
                    }
                }, new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() }));
                var opaResponse = await _opaClient.PostAsync("/v1/data/grp/getUserGroupsWithChildrenByUserId", opaPayload);
                opaResponse.EnsureSuccessStatusCode();
                var opaJson = await opaResponse.Content.ReadAsStringAsync();
                var opaResponseData = JsonConvert.DeserializeObject<OpaResponseDto<IEnumerable<GroupDto>>>(opaJson, new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });
                return new OkObjectResult(_mapper.Map<IEnumerable<OrganisationDto>>(opaResponseData.Result));
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
            ILogger log,
            ClaimsPrincipal principal
            )
        {
            try
            {
                var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Development";
                if (env != "Development" && principal.FindFirstValue("roles") != "Application")
                {
                    return new StatusCodeResult(401);
                }
                var uid = req.RouteValues["userId"].ToString();

                var opaPayload = new StringContent(JsonConvert.SerializeObject(new OpaInputDto<OpaIdDto>
                {
                    Input = new OpaIdDto
                    {
                        Id = uid
                    }
                }, new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() }));
                var opaResponse = await _opaClient.PostAsync("/v1/data/grp/getUserGroupsWithChildrenByUserId", opaPayload);
                opaResponse.EnsureSuccessStatusCode();
                var opaJson = await opaResponse.Content.ReadAsStringAsync();
                var opaResponseData = JsonConvert.DeserializeObject<OpaResponseDto<IEnumerable<GroupDto>>>(opaJson, new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });
                return new OkObjectResult(_mapper.Map<IEnumerable<OrganisationDto>>(opaResponseData.Result));
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }
    }
}