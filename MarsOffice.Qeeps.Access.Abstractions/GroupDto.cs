using System;
using System.Collections.Generic;

namespace MarsOffice.Qeeps.Access.Abstractions
{
    public class GroupDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public IEnumerable<GroupDto> Children { get; set; }
    }
}
