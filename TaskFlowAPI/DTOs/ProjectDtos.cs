using System.ComponentModel.DataAnnotations;

namespace TaskFlowAPI.DTOs;

// Outbound Response payload
public class ProjectDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Flat metadata counts or basic lists to avoid massive JSON nesting depth
    public int TotalTasksCount { get; set; }
    public List<ProjectMemberDto> Members { get; set; } = new();
}

// Inbound Request payload (Creates/Updates)
public class SaveProjectDto
{
    [Required]
    [StringLength(50, ErrorMessage = "Project name cannot exceed 50 characters.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters.")]
    public string Description { get; set; } = string.Empty;
}
