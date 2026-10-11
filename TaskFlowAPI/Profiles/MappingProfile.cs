using AutoMapper;
using TaskFlowAPI.DTOs;
using TaskFlowAPI.Models;

namespace TaskFlowAPI.Profiles;

public class MappingProfile : Profile
{
    // This must be a constructor method, NOT a nested class!
    public MappingProfile()
    {
        // 1. Inbound: Request DTO -> Database Entity
        CreateMap<CreateTaskDto, ProjectTask>();

        // 2. Outbound: Database Entity -> Output DTO
        CreateMap<ProjectTask, TaskDto>()
            .ForMember(dest => dest.ProjectName, opt => opt.MapFrom(src => src.Project != null ? src.Project.Name : string.Empty))
            .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Category != null ? src.Category.Name : string.Empty))
            .ForMember(dest => dest.AssigneeName, opt => opt.MapFrom(src => src.AssignedTo != null ? src.AssignedTo.UserName : string.Empty));
    }
}
