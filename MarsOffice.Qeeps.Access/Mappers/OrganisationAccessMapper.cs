using AutoMapper;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Access.Entities;

namespace MarsOffice.Qeeps.Access.Mappers
{
    public class OrganisationAccessMapper : Profile
    {
        public OrganisationAccessMapper()
        {
            CreateMap<OrganisationAccessEntity, OrganisationAccessDto>()
                .PreserveReferences()
                .ReverseMap()
                .PreserveReferences();
        }
    }
}