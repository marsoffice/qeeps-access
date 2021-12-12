using System.Collections.Generic;

namespace MarsOffice.Qeeps.Access.Abstractions
{
    public class UserDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public IEnumerable<string> Roles { get; set; }
        public bool IsDisabled { get; set; }
        public bool HasSignedContract { get; set; }
        public UserPreferencesDto UserPreferences { get; set; }
    }
}