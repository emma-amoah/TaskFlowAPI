using System;

namespace TaskFlowAPI.DTOs;

public class ErrorResponse
{
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty; // Populated only during local development testing
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
