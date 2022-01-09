using AutoMapper;

namespace MarsOffice.Qeeps.Access.Mappers
{
    public class OrganisationMapper : Profile
    {
        public OrganisationMapper() {
            CreateMap<Dto.GroupDto, Abstractions.OrganisationDto>().PreserveReferences();
        }
    }
}