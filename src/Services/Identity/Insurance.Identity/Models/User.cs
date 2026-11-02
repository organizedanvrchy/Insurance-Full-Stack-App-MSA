using Insurance.BuildingBlocks.Security;


namespace Insurance.Identity.Models;

public class User
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public DateTime? EmailVerifiedAt { get; set; }    // null until the customer clicks the link in their email

    public bool EmailVerified => EmailVerifiedAt is not null;
}
