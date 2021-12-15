using System.Collections.Generic;
using Newtonsoft.Json;

namespace MarsOffice.Qeeps.Access.Entities
{
    public class UserEntity
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public IEnumerable<string> Roles { get; set; }
        public bool IsDisabled { get; set; }
        public string Partition { get; set; } = "UserEntity";
        public UserPreferencesEntity UserPreferences { get; set; }
    }
}