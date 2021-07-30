using System.Linq;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Microfunction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace MarsOffice.Qeeps.Access
{
    public class Test
    {
        public Test()
        {

        }

        [FunctionName("Test")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            var cp = QeepsPrincipal.Parse(req);
            var res = string.Join("\r\n",

            req.Headers.Select(x => x.Key + ": " + string.Join(",", x.Value)).ToList());

            var cl = string.Join("\r\n", cp.Claims.Select(x => x.Type + ": " + x.Value));

            log.LogInformation("C# HTTP trigger function processed a request.");
            return new OkObjectResult(new { test = cp?.Identity?.Name, res = res, cl = cl });
        }
    }
}
