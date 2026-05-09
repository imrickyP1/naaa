namespace AMS.API.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Position { get; set; } = "staff";
    public string? Gender { get; set; }
    public string? FingerprintTemplate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Position { get; set; } = "staff";
    public string? Gender { get; set; }
    public bool HasFingerprint { get; set; }
}
