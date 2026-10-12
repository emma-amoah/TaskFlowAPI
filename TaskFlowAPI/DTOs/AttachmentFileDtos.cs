namespace TaskFlowAPI.DTOs;

public class AttachmentFileDto
{
    public int Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public long FileSizeInBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}
