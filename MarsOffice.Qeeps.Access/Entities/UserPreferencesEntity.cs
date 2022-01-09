using Newtonsoft.Json;

namespace MarsOffice.Qeeps.Access.Entities
{
    public class UserPreferencesEntity
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        public string PreferredLanguage { get; set; }
        public bool? UseDarkTheme { get; set; }
        public string Partition { get; set; } = "UserPreferencesEntity";
    }
}