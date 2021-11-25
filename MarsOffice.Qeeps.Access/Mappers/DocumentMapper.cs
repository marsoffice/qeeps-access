using AutoMapper;
using MarsOffice.Qeeps.Access.Abstractions;
using MarsOffice.Qeeps.Access.Entities;

namespace MarsOffice.Qeeps.Access.Mappers
{
    public class DocumentMapper : Profile
    {
        public DocumentMapper()
        {
            CreateMap<DocumentEntity, DocumentDto>()
                .PreserveReferences()
                .ReverseMap()
                .PreserveReferences();
        }
    }
}