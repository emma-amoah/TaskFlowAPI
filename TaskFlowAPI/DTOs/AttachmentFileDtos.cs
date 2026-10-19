namespace TaskFlowAPI.DTOs;

public class AttachmentFileDto
{
    public int Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public long FileSizeInBytes { get; set; }
    public DateTime CreatedAt { get; set; }

    // The URL to download the file, which can be constructed based on the server's base URL and the file's unique storage name.
    public string DownloadUrl { get; set; } = string.Empty;
}
