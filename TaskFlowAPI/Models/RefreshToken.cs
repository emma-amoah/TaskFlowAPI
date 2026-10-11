using System;

namespace TaskFlowAPI.Models;

public class RefreshToken
{
    public int Id { get; set; }

    // Store the SHA256 hash of the token instead of plain text
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    // Creation Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedByIp { get; set; } = string.Empty;

    // Revocation Metadata
    public DateTime? RevokedAt { get; set; }
    public string? RevokedByIp { get; set; }

    // Self-referencing relationship
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive => RevokedAt == null && !IsExpired;

    // Foreign Key matching ASP.NET Identity User (string primary key)
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }
}
