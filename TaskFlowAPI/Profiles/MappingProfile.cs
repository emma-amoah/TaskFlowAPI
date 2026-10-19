using AutoMapper;
using System.Linq;
using TaskFlowAPI.DTOs;
using TaskFlowAPI.Models;

namespace TaskFlowAPI.Profiles;

public class MappingProfile : Profile
{
    public MappingProfile()
    {

        // 1. PROJECT MAPPINGS
        CreateMap<SaveProjectDto, Project>();
        CreateMap<Project, ProjectDto>()
            .ForMember(dest => dest.TotalTasksCount, opt => opt.MapFrom(src => src.Tasks != null ? src.Tasks.Count : 0))
            .ForMember(dest => dest.Members, opt => opt.MapFrom(src => src.ProjectMembers));

        // 2. CATEGORY MAPPINGS
        CreateMap<SaveCategoryDto, Category>();
        CreateMap<Category, CategoryDto>();

        // 3. PROJECTTASK MAPPINGS
        CreateMap<CreateTaskDto, ProjectTask>()
            // Ensures strings coming from inbound DTOs map to strongly typed Enums
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => System.Enum.Parse<TaskFlowAPI.Models.TaskStatus>(src.Status, true)))
            .ForMember(dest => dest.Priority, opt => opt.MapFrom(src => System.Enum.Parse<TaskPriority>(src.Priority, true)));

        CreateMap<UpdateTaskDto, ProjectTask>();

        CreateMap<ProjectTask, TaskDto>()
            // Convert strongly typed Enums back to readable JSON strings for clients
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status.ToString()))
            .ForMember(dest => dest.Priority, opt => opt.MapFrom(src => src.Priority.ToString()))
            .ForMember(dest => dest.ProjectName, opt => opt.MapFrom(src => src.Project != null ? src.Project.Name : string.Empty))
            .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Category != null ? src.Category.Name : string.Empty))
            .ForMember(dest => dest.AssignedToName, opt => opt.MapFrom(src => src.AssignedTo != null ? src.AssignedTo.UserName : string.Empty))
            .ForMember(dest => dest.Comments, opt => opt.MapFrom(src => src.Comments))
            .ForMember(dest => dest.Attachments, opt => opt.MapFrom(src => src.Attachments))
            .ForMember(dest => dest.CreatedById, opt => opt.MapFrom(src => src.CreatedBy))
            // Looks up creator context strings cleanly or falls back safely
            .ForMember(dest => dest.CreatedByName, opt => opt.MapFrom((src, dest, destMember, context) =>
            {
                if (src.CreatedBy != null
                    && context.Items.TryGetValue("CreatorsMap", out var mapObj)
                    && mapObj is Dictionary<string, string> map
                    && map.TryGetValue(src.CreatedBy, out var name))
                {
                    return name;
                }
                return "System";
            }));


        // 4. COMMENT MAPPINGS
        CreateMap<CreateCommentDto, Comment>();
        CreateMap<Comment, CommentDto>()
            .ForMember(dest => dest.AuthorName, opt => opt.MapFrom(src => src.Author != null ? src.Author.UserName : string.Empty));

        // 5. ATTACHMENT MAPPINGS
        CreateMap<AttachmentFile, AttachmentFileDto>()
            .ForMember(dest => dest.DownloadUrl, opt => opt.MapFrom((src, dest, destMember, context) =>
            {
                // Safely extract the active base URL domain context passed from the controller runtime
                if (context.Items.TryGetValue("BaseUrl", out var baseUrl))
                {
                    return $"{baseUrl}/api/Attachments/download/{src.Id}";
                }

                // Fallback relative path pattern if no context string was supplied
                return $"/api/Attachments/download/{src.Id}";
            }));


        // 6. MEMBER MAPPINGS
        CreateMap<ProjectMember, ProjectMemberDto>()
            .ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.User != null ? src.User.UserName : string.Empty));

        // 7. USER MAPPINGS

        CreateMap<User, UserDto>()
            .ForMember(dest => dest.Username, opt => opt.MapFrom(src => src.UserName));
        // Maps IdentityUser's native 'UserName' property to the DTO's 'Username' field

    }
}
