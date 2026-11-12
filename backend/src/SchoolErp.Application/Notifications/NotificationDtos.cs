using System.ComponentModel.DataAnnotations;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Notifications;

public sealed class NotificationDto
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public NotificationChannel Channel { get; init; }
    public NotificationStatus Status { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ReadAtUtc { get; init; }
}

public sealed class BroadcastRequest
{
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(2000)] public string Message { get; set; } = string.Empty;
    [MaxLength(30)] public string Audience { get; set; } = "All";
    public NotificationChannel Channel { get; set; } = NotificationChannel.InApp;
}
