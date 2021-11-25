using Newtonsoft.Json;

namespace MarsOffice.Qeeps.Access.Entities
{
    public class DocumentEntity
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        public string Content { get; set; }
        public string Partition { get; set; } = "DocumentEntity";
    }
}