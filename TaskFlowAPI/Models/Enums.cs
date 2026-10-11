namespace TaskFlowAPI.Models;

public enum TaskStatus
{
    Backlog,
    Todo,
    InProgress,
    InReview,
    Completed
}

public enum TaskPriority
{
    Low,
    Medium,
    High,
    Critical
}
