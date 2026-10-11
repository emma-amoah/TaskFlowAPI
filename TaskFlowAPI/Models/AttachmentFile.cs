using TaskFlowAPI.Interfaces;

namespace TaskFlowAPI.Models;

public class AttachmentFile : BaseEntity
{
    public int Id { get; set; }

    // File storage paths
    public string OriginalFileName { get; set; } = string.Empty; // e.g. "receipt.pdf"
    public string StorageFileName { get; set; } = string.Empty;  // e.g. "a5b2-3c1e-guid.pdf"
    public string FilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeInBytes { get; set; }

    public int TaskId { get; set; }
    public ProjectTask? Task { get; set; }
}
