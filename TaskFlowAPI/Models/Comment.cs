namespace TaskFlowAPI.Models
{
    public class Comment : BaseEntity
    {
        public int Id { get; set; }
        public string Content { get; set; } = string.Empty;

        // Required Foreign Keys
        public int TaskId { get; set; }
        public string AuthorId { get; set; } = string.Empty;

        // Navigation properties
        public ProjectTask? Task { get; set; }
        public User? Author { get; set; }
    }
}
