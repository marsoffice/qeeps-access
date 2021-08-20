using Newtonsoft.Json;

namespace MarsOffice.Qeeps.Access.Entities
{
    public class UserPreferencesEntity
    {
        public bool? UseDarkTheme { get; set; }
        public string PreferredLanguage { get; set; }
    }
}