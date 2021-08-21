using AutoMapper;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Access.Entities;

namespace MarsOffice.Qeeps.Access.Mappers
{
    public class UserPreferencesMapper : Profile
    {
        public UserPreferencesMapper()
        {
            CreateMap<UserPreferencesEntity, UserPreferencesDto>()
                .PreserveReferences()
                .ReverseMap()
                .PreserveReferences();
        }
    }
}