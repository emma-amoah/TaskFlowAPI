using System;
using System.ComponentModel.DataAnnotations;
using TaskFlowAPI.Models;

namespace TaskFlowAPI.DTOs;

// Outbound Response payload
public class TaskDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Express enums as strings automatically in JSON serialization
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }

    // Flattened relation parameters
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;

    // Handled as nullable string to align with our unassigned user decision
    public string? AssignedToId { get; set; }
    public string? AssignedToName { get; set; }

    public List<CommentDto> Comments { get; set; } = new();
    public List<AttachmentFileDto> Attachments { get; set; } = new();
}

// Inbound payload when spawning a task
public class CreateTaskDto
{
    [Required]
    [StringLength(200, ErrorMessage = "Title cannot exceed 200 characters.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(4000, ErrorMessage = "Description cannot exceed 4000 characters.")]
    public string Description { get; set; } = string.Empty;

    [Required]
    [EnumDataType(typeof(TaskFlowAPI.Models.TaskStatus))]
    public string Status { get; set; } = "Todo";

    [Required]
    [EnumDataType(typeof(TaskFlowAPI.Models.TaskPriority))]
    public string Priority { get; set; } = "Medium";

    public DateTime? DueDate { get; set; }

    [Required]
    public int ProjectId { get; set; }

    [Required]
    public int CategoryId { get; set; }

    public string? AssignedToId { get; set; }
}
