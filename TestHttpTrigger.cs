using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace MarsOffice.Qeeps.AccessService
{
    public static class TestHttpTrigger
    {
        [Function("test")]
        public static async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req,
            FunctionContext executionContext, ClaimsPrincipal principal)
        {
            var logger = executionContext.GetLogger("test");
            logger.LogInformation("C# HTTP trigger function processed a request.");

            var response = req.CreateResponse(HttpStatusCode.OK);

            await response.WriteAsJsonAsync(new {Hi = "Alin"});

            return response;
        }
    }
}
