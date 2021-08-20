using Newtonsoft.Json;

namespace MarsOffice.Qeeps.Access.Entities
{
    public class OrganisationAccessEntity
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        public string UserId { get; set; }
        public string OrganisationId {get;set;}
        public string FullOrganisationId { get; set; }
        public string Partition {get;set;} = "OrganisationAccessEntity";
    }
}
