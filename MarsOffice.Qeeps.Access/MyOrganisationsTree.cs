using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Microfunction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace MarsOffice.Qeeps.Access
{
    public class MyOrganisationsTree
    {
        private readonly IServer _server;
        private readonly IDatabase _database;
        private readonly IConfiguration _config;
        public MyOrganisationsTree(ConnectionMultiplexer mux, IConfiguration config)
        {
            _server = mux.GetServer(mux.GetEndPoints()[0]);
            _database = mux.GetDatabase(config.GetValue<int>("redisdatabase"));
            _config = config;
        }

        [FunctionName("MyOrganisationsTree")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/myOrganisationsTree")] HttpRequest req)
        {
            var principal = QeepsPrincipal.Parse(req);
            var groupIds = principal.FindAll(x => x.Type == "groups").Select(x => x.Value).Distinct().ToList();
            var ids = new Dictionary<string, string>();
            foreach (var id in groupIds)
            {
                var foundKeys = _server.Keys(_config.GetValue<int>("redisdatabase"), $"*_{id}").ToList();
                if (!foundKeys.Any())
                {
                    continue;
                }
                var key = foundKeys.First().ToString();
                var value = await _database.StringGetAsync(key);
                if (!value.HasValue)
                {
                    continue;
                }
                var strValue = value.ToString();
                ids[key] = strValue;
            }

            OrganisationDto rootGroup = null;
            
            if (ids.Any())
            {
                rootGroup = new OrganisationDto
                {
                    Id = _config["adgroupid"],
                    Name = ids[$"_{_config["adgroupid"]}"]
                };

                ids = ids.Where(x => x.Key != $"_{_config["adgroupid"]}").ToDictionary(x => x.Key, x => x.Value);
                PopulateChildren(rootGroup, ids);
            }
            return new OkObjectResult(rootGroup);
        }

        private void PopulateChildren(OrganisationDto rootGroup, Dictionary<string, string> ids)
        {
            var foundChildrenDict = ids.Where(x => x.Key.StartsWith($"_{rootGroup.Id}")).ToDictionary(x => x.Key.Replace($"_{rootGroup.Id}", ""), x => x.Value);
            if (!foundChildrenDict.Any())
            {
                return;
            }
            rootGroup.Children = foundChildrenDict.Where(x => x.Key.Count(x => x == '_') == 1).Select(x => new OrganisationDto
            {
                Id = x.Key[1..],
                Name = x.Value
            }).ToList();
            foreach (var kid in rootGroup.Children)
            {
                PopulateChildren(kid, foundChildrenDict);
            }
        }
    }
}
