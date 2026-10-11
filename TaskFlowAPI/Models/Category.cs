using TaskFlowAPI.Interfaces;

namespace TaskFlowAPI.Models
{
    public class Category : BaseEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        // Navigation property
        public ICollection<ProjectTask> Tasks { get; set; } = new List<ProjectTask>();
    }
}
