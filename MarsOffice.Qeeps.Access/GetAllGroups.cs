using System.Linq;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Microfunction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
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

        [FunctionName("MyGroups")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req)
        {
            var principal = QeepsPrincipal.Parse(req);
            var groupIds = principal.FindAll(x => x.Type == "groups").Select(x => x.Value).Distinct().ToList();
            var odataGroupsFilter = $"({string.Join(",", groupIds.Select(x => "'" + x + "'").ToList())})";
            var myGroups = await _graphClient.Groups.Request()
                .Filter($"id in {odataGroupsFilter}")
                .Expand(x => x.MemberOf)
                .Select(x => new {x.Id, x.DisplayName, x.MemberOf})
                .GetAsync();
            return new OkObjectResult(myGroups);
        }
    }
}
