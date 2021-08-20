using AutoMapper;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Access.Entities;

namespace MarsOffice.Qeeps.Access.Mappers
{
    public class UserMapper : Profile
    {
        public UserMapper()
        {
            CreateMap<UserEntity, UserDto>()
                .PreserveReferences()
                .ReverseMap()
                .PreserveReferences();
        }
    }
}