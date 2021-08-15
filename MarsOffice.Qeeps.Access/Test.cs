using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Microfunction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;

namespace MarsOffice.Qeeps.Access
{
    public class Test
    {
        public Test()
        {
        }

        [FunctionName("Test")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/test")] HttpRequest req,
            ClaimsPrincipal principal)
        {
            var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Development";
            if (env != "Development" && principal.FindFirstValue("roles") != "Application")
            {
                return new StatusCodeResult(401);
            }
            await Task.CompletedTask;
            return new OkObjectResult(new OrganisationDto
            {
                Id = "1",
                Name = $"test {string.Join("|", principal?.Claims.Select(x => x.Type + "=" + x.Value).ToList())}"
            });
        }
    }
}
