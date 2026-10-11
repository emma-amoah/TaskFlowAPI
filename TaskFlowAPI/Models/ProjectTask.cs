using TaskFlowAPI.Interfaces;

namespace TaskFlowAPI.Models;

public class ProjectTask : BaseEntity
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Enums replace free strings and boolean flags
    public TaskStatus Status { get; set; } = TaskStatus.Todo;
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    public DateTime? DueDate { get; set; }

    // Relationships
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    // Nullable string allows unassigned pipeline states safely
    public string? AssignedToId { get; set; }
    public User? AssignedTo { get; set; }

    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<AttachmentFile> Attachments { get; set; } = new List<AttachmentFile>();
}
