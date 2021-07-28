using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace MarsOffice.Qeeps.Access
{
    public static class Test
    {
        [Function("test")]
        public static async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req,
            FunctionContext executionContext, ClaimsPrincipal identity)
        {
            var logger = executionContext.GetLogger("test5");
            logger.LogInformation("C# HTTP trigger function processed a request.");
            var res = "";

            var enumer = req.Headers.GetEnumerator();

            while (enumer.MoveNext()) {
                res += "\r\n" + enumer.Current.Key + ": " + string.Join(", ", enumer.Current.Value);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new {exe = res});
            return response;
        }
    }
}
