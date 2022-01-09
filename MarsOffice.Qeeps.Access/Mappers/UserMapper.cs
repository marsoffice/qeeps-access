using AutoMapper;

namespace MarsOffice.Qeeps.Access.Mappers
{
    public class UserMapper : Profile
    {
        public UserMapper() {
            CreateMap<Dto.UserDto, Abstractions.UserDto>().PreserveReferences()
                .ForMember(x => x.Name, y => y.MapFrom(z => z.FirstName + " " + z.LastName));
        }
    }
}