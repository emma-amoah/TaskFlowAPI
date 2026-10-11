using TaskFlowAPI.Interfaces;

namespace TaskFlowAPI.Models;

public class Project : BaseEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public ICollection<ProjectTask> Tasks { get; set; } = new List<ProjectTask>();
    public ICollection<ProjectMember> ProjectMembers { get; set; } = new List<ProjectMember>();
}
