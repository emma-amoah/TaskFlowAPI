namespace TaskFlowAPI.DTOs
{
    public class TaskDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string AssigneeName { get; set; } = string.Empty;
    }
}
