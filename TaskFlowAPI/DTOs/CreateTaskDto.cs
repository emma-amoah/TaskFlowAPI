namespace TaskFlowAPI.DTOs
{
    public class CreateTaskDto
    {
        public string Title { get; set; } = string.Empty;
        public int ProjectId { get; set; }
        public int CategoryId { get; set; }
        public int AssigneeId { get; set; }
    }
}
