using System.Linq;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Microfunction;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Graph;

namespace MarsOffice.Qeeps.Access
{
    public class MyOrganisationsTree
    {
        private readonly GraphServiceClient _graphClient;
        public MyOrganisationsTree(GraphServiceClient graphClient)
        {
            _graphClient = graphClient;
        }

        [FunctionName("MyOrganisationsTree")]
        public async Task<OrganisationDto> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/access/myOrganisationsTree")] HttpRequest req)
        {
            var principal = QeepsPrincipal.Parse(req);
            var groupIds = principal.FindAll(x => x.Type == "groups").Select(x => x.Value).Distinct().ToList();
            var odataGroupsFilter = $"({string.Join(",", groupIds.Select(x => "'" + x + "'").ToList())})";
            var myGroups = await _graphClient.Groups.Request()
                .Filter($"id in {odataGroupsFilter}")
                .Expand($"memberOf($select=id)")
                .Select(x => new {x.Id, x.DisplayName, x.MemberOf})
                .GetAsync();
            var rootGroup = myGroups.Where(x => x.MemberOf == null || !x.MemberOf.Any()).Select(x => new OrganisationDto {
                Id = x.Id,
                Name = x.DisplayName
            }).Single();
            PopulateChildren(rootGroup, myGroups);
            return rootGroup;
        }

        private void PopulateChildren(OrganisationDto rootGroup, IGraphServiceGroupsCollectionPage myGroups)
        {
            var foundChildren = myGroups.Where(x => x.MemberOf.Any(y => y.Id == rootGroup.Id)).Distinct().ToList();
            rootGroup.Children = foundChildren.Select(x => new OrganisationDto {
                Id = x.Id,
                Name = x.DisplayName
            }).ToList();
            foreach (var child in rootGroup.Children) {
                PopulateChildren(child, myGroups);
            }
        }
    }
}
