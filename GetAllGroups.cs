using System.Linq;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Microfunction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;

namespace MarsOffice.Qeeps.Access
{
    public class GetAllGroups
    {
        private readonly GraphServiceClient _graphClient;
        public GetAllGroups(GraphServiceClient graphClient)
        {
            _graphClient = graphClient;
        }

        [FunctionName("GetAllGroups")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            var allGroups = await _graphClient.Groups.Request().GetAsync();
            return new OkObjectResult(allGroups);
        }
    }
}
