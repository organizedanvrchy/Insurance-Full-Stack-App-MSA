using Insurance.BuildingBlocks.Security;


namespace Insurance.Identity.DTOs;

public class UserDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public DateTime CreatedAt { get; set; }

    public bool EmailVerified { get; set; }
    public bool MustChangePassword { get; set; }   // true after an admin reset, until a new password is chosen
}