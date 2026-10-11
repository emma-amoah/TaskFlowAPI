using Microsoft.AspNetCore.Identity;

namespace TaskFlowAPI.Models;


public class User : IdentityUser
{
    // Custom Account State Flag
    public bool IsActive { get; set; } = true;

    // Custom Navigation properties for your business logic
    public ICollection<ProjectTask> AssignedTasks { get; set; } = new List<ProjectTask>();
    public ICollection<ProjectMember> ProjectMembers { get; set; } = new List<ProjectMember>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    // Inside User.cs
    public ICollection<Comment> AuthoredComments { get; set; } = new List<Comment>();

}
