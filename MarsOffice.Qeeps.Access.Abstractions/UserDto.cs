using System.Collections.Generic;

namespace MarsOffice.Qeeps.Access.Abstractions
{
    public class UserDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public bool IsDisabled { get; set; }
    }
}