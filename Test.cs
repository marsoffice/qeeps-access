using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Microfunction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MarsOffice.Qeeps.Access
{
    public static class Test
    {
        [FunctionName("Test")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            var cp = QeepsPrincipal.Parse(req.Headers["x-ms-client-principal"]);
            var res = string.Join("\r\n",

            req.Headers.Select(x => x.Key + ": " + string.Join(",", x.Value)).ToList());

            var cl = string.Join("\r\n", cp.Claims.Select(x => x.Type + ": " + x.Value));

            log.LogInformation("C# HTTP trigger function processed a request.");
            return new OkObjectResult(new { test = cp?.Identity?.Name, res = res, cl = cl });
        }
    }
}
