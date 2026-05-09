using AMS.API.Data;
using AMS.API.Models;
using Dapper;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AMS.API.Services;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginWithFingerprintAsync(string fingerprintTemplate);
    string GenerateJwtToken(User user);
}

public class AuthService : IAuthService
{
    private readonly DatabaseContext _db;
    private readonly IFingerprintService _fingerprintService;
    private readonly string _jwtKey;

    public AuthService(DatabaseContext db, IFingerprintService fingerprintService)
    {
        _db = db;
        _fingerprintService = fingerprintService;
        _jwtKey = Environment.GetEnvironmentVariable("JWT_SECRET") ?? "AMS_SuperSecretKey_2024_Live20R_Fingerprint";
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var user = await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM users WHERE username = @Username",
            new { request.Username }
        );

        if (user == null)
        {
            return new AuthResponse { Success = false, Message = "User not found" };
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.Password))
        {
            return new AuthResponse { Success = false, Message = "Invalid password" };
        }

        var token = GenerateJwtToken(user);

        return new AuthResponse
        {
            Success = true,
            Message = "Login successful",
            Token = token,
            User = new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Position = user.Position,
                Gender = user.Gender,
                HasFingerprint = !string.IsNullOrEmpty(user.FingerprintTemplate)
            }
        };
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        // Check if username exists
        var existing = await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT id FROM users WHERE username = @Username",
            new { request.Username }
        );

        if (existing != null)
        {
            return new AuthResponse { Success = false, Message = "Username already exists" };
        }

        // Hash password
        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);

        // Insert user
        var userId = await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO users (username, password, position, gender, fingerprint_template)
            VALUES (@Username, @Password, @Position, @Gender, @FingerprintTemplate);
            SELECT LAST_INSERT_ID();
        ", new
        {
            request.Username,
            Password = hashedPassword,
            request.Position,
            request.Gender,
            request.FingerprintTemplate
        });

        // If fingerprint provided, also add to fingerprints table
        if (!string.IsNullOrEmpty(request.FingerprintTemplate))
        {
            await conn.ExecuteAsync(@"
                INSERT INTO fingerprints (user_id, fingerprint_template, finger_index)
                VALUES (@UserId, @Template, 0)
            ", new { UserId = userId, Template = request.FingerprintTemplate });
        }

        var user = new User
        {
            Id = userId,
            Username = request.Username,
            Position = request.Position,
            Gender = request.Gender
        };

        var token = GenerateJwtToken(user);

        return new AuthResponse
        {
            Success = true,
            Message = "Registration successful",
            Token = token,
            User = new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Position = user.Position,
                Gender = user.Gender,
                HasFingerprint = !string.IsNullOrEmpty(request.FingerprintTemplate)
            }
        };
    }

    public async Task<AuthResponse> LoginWithFingerprintAsync(string fingerprintTemplate)
    {
        // Verify fingerprint and get user
        var result = await _fingerprintService.IdentifyUserAsync(fingerprintTemplate);

        if (!result.Matched || result.UserId == null)
        {
            return new AuthResponse { Success = false, Message = "Fingerprint not recognized" };
        }

        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var user = await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM users WHERE id = @Id",
            new { Id = result.UserId }
        );

        if (user == null)
        {
            return new AuthResponse { Success = false, Message = "User not found" };
        }

        var token = GenerateJwtToken(user);

        return new AuthResponse
        {
            Success = true,
            Message = "Fingerprint login successful",
            Token = token,
            User = new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Position = user.Position,
                Gender = user.Gender,
                HasFingerprint = true
            }
        };
    }

    public string GenerateJwtToken(User user)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Position),
            new Claim("position", user.Position)
        };

        var token = new JwtSecurityToken(
            issuer: "AMS.API",
            audience: "AMS.Client",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
