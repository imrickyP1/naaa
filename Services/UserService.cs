using AMS.API.Data;
using AMS.API.Models;
using Dapper;

namespace AMS.API.Services;

public interface IUserService
{
    Task<List<UserDto>> GetAllUsersAsync();
    Task<UserDto?> GetUserByIdAsync(int id);
    Task<UserDto?> GetUserByUsernameAsync(string username);
    Task<bool> UpdateUserAsync(int id, RegisterRequest request);
    Task<bool> DeleteUserAsync(int id);
}

public class UserService : IUserService
{
    private readonly DatabaseContext _db;

    public UserService(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<List<UserDto>> GetAllUsersAsync()
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var users = await conn.QueryAsync<User>("SELECT * FROM users ORDER BY username");

        return users.Select(u => new UserDto
        {
            Id = u.Id,
            Username = u.Username,
            Position = u.Position,
            Gender = u.Gender,
            HasFingerprint = !string.IsNullOrEmpty(u.FingerprintTemplate)
        }).ToList();
    }

    public async Task<UserDto?> GetUserByIdAsync(int id)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var user = await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM users WHERE id = @Id",
            new { Id = id }
        );

        if (user == null) return null;

        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Position = user.Position,
            Gender = user.Gender,
            HasFingerprint = !string.IsNullOrEmpty(user.FingerprintTemplate)
        };
    }

    public async Task<UserDto?> GetUserByUsernameAsync(string username)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var user = await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM users WHERE username = @Username",
            new { Username = username }
        );

        if (user == null) return null;

        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Position = user.Position,
            Gender = user.Gender,
            HasFingerprint = !string.IsNullOrEmpty(user.FingerprintTemplate)
        };
    }

    public async Task<bool> UpdateUserAsync(int id, RegisterRequest request)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var sql = "UPDATE users SET position = @Position, gender = @Gender";
        
        if (!string.IsNullOrEmpty(request.Password))
        {
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);
            sql += ", password = @Password";
            await conn.ExecuteAsync(sql + " WHERE id = @Id", new 
            { 
                request.Position, 
                request.Gender, 
                Password = hashedPassword,
                Id = id 
            });
        }
        else
        {
            await conn.ExecuteAsync(sql + " WHERE id = @Id", new 
            { 
                request.Position, 
                request.Gender, 
                Id = id 
            });
        }

        return true;
    }

    public async Task<bool> DeleteUserAsync(int id)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        await conn.ExecuteAsync("DELETE FROM users WHERE id = @Id", new { Id = id });
        return true;
    }
}
