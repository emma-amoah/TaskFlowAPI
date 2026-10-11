namespace TaskFlowAPI.Models
{
    public class ProjectMember
    {
        public int ProjectId { get; set; }
        public Project? Project { get; set; }

        public string UserId { get; set; } = string.Empty;
        public User? User { get; set; }

        public string Role { get; set; } = "Member";
    }

}
