namespace MarsOffice.Qeeps.Access.Abstractions
{
    public class UserPreferencesDto
    {
        public string Id { get; set; }
        public string UserId {get;set;}
        public bool? UseDarkTheme {get;set;}
        public string PreferredLanguage {get;set;}
    }
}