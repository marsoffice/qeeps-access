using AutoMapper;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Access.Entities;

namespace MarsOffice.Qeeps.Access.Mappers
{
    public class OrganisationMapper : Profile
    {
        public OrganisationMapper()
        {
            CreateMap<OrganisationEntity, OrganisationDto>()
                .PreserveReferences()
                .ReverseMap()
                .PreserveReferences();
        }
    }
}