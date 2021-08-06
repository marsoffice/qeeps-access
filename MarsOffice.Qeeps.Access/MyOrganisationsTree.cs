using System.Linq;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Microfunction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using StackExchange.Redis;

namespace MarsOffice.Qeeps.Access
{
    public class MyOrganisationsTree
    {
        private readonly IServer _server;
        private readonly IConfiguration _config;
        public MyOrganisationsTree(ConnectionMultiplexer mux, IConfiguration config)
        {
            _server = mux.GetServer(mux.GetEndPoints()[0]);
            _config = config;
        }

        [FunctionName("MyOrganisationsTree")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/myOrganisationsTree")] HttpRequest req)
        {
            var principal = QeepsPrincipal.Parse(req);
            if (!principal.Identity.IsAuthenticated) {
                return new UnauthorizedResult();
            }
            var groupIds = principal.FindAll(x => x.Type == "groups").Select(x => x.Value).Distinct().ToList();
            foreach (var id in groupIds) {
                var keys = _server.Keys(_config.GetValue<int>("redisdatabase"), $"*_{id}").ToList();
            }
            return new OkObjectResult(null);
        }

        private void PopulateChildren(OrganisationDto rootGroup)
        {
            
        }
    }
}
