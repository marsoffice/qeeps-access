using Newtonsoft.Json;

namespace MarsOffice.Qeeps.Access.Entities
{
    public class OrganisationEntity
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        public string Name { get; set; }
        public string ParentIds { get; set; }
        public string Partition {get;set;} = "OrganisationEntity";
    }
}