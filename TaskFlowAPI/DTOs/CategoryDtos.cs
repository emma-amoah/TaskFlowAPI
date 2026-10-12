using System.ComponentModel.DataAnnotations;

namespace TaskFlowAPI.DTOs;

public class CategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class SaveCategoryDto
{
    [Required]
    [StringLength(50, ErrorMessage = "Category name cannot exceed 50 characters.")]
    public string Name { get; set; } = string.Empty;
}
