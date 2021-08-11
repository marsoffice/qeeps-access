using System.Threading.Tasks;
using MarsOffice.Qeeps.Access.Abstractions;
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
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/test")] HttpRequest req)
        {
            
            return new OkObjectResult(new OrganisationDto {
                Id = "1",
                Name = "test"
            });
        }
    }
}
