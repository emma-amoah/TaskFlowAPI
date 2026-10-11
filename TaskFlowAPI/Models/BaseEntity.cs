using System;
using System.ComponentModel.DataAnnotations;
using TaskFlowAPI.Interfaces;

namespace TaskFlowAPI.Models;

public abstract class BaseEntity : IAuditableEntity, ISoftDelete
{
    // Audit Fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    // Soft Delete Fields
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Concurrency Token Field
    [Timestamp] // Tells EF Core / SQL Server to treat this as a rowversion tracker
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
