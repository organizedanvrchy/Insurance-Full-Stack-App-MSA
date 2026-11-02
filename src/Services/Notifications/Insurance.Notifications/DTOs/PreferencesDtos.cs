using System.ComponentModel.DataAnnotations;
using Insurance.Notifications.Models;

namespace Insurance.Notifications.DTOs;

public class PreferencesDto
{
    public bool PaymentReminders { get; set; }
}

public class UpdatePreferencesRequest
{
    [Required]      // nullable so a missing field is a 400
    public bool? PaymentReminders { get; set; }
}

public class SentEmailDto
{
    public long EmailId { get; set; }
    public NotificationKind Kind { get; set; }
    public string? Subject { get; set; }
    public string? ToEmail { get; set; }
    public DateTime? SentAt { get; set; }
}
