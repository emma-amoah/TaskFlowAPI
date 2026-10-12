using System;
using System.ComponentModel.DataAnnotations;

namespace TaskFlowAPI.DTOs;

public class CommentDto
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public string AuthorId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
}

public class CreateCommentDto
{
    [Required]
    [StringLength(100, ErrorMessage = "Comments cannot exceed 100 characters.")]
    public string Content { get; set; } = string.Empty;

    [Required]
    public int TaskId { get; set; }
}
