using AMS.API.Models;
using AMS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IFingerprintService _fingerprintService;

    public UsersController(IUserService userService, IFingerprintService fingerprintService)
    {
        _userService = userService;
        _fingerprintService = fingerprintService;
    }

    /// <summary>
    /// Get all users
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> GetAll()
    {
        var users = await _userService.GetAllUsersAsync();
        return Ok(new { success = true, count = users.Count, users });
    }

    /// <summary>
    /// Get user by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var user = await _userService.GetUserByIdAsync(id);
        
        if (user == null)
        {
            return NotFound(new { success = false, message = "User not found" });
        }

        return Ok(new { success = true, user });
    }

    /// <summary>
    /// Get user by username
    /// </summary>
    [HttpGet("username/{username}")]
    public async Task<IActionResult> GetByUsername(string username)
    {
        var user = await _userService.GetUserByUsernameAsync(username);
        
        if (user == null)
        {
            return NotFound(new { success = false, message = "User not found" });
        }

        return Ok(new { success = true, user });
    }

    /// <summary>
    /// Update user
    /// </summary>
    [HttpPut("{id}")]
    [Authorize]
    public async Task<IActionResult> Update(int id, [FromBody] RegisterRequest request)
    {
        var success = await _userService.UpdateUserAsync(id, request);
        return Ok(new { success, message = success ? "User updated" : "Update failed" });
    }

    /// <summary>
    /// Delete user (Admin only)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var success = await _userService.DeleteUserAsync(id);
        return Ok(new { success, message = "User deleted" });
    }

    /// <summary>
    /// Enroll fingerprint for user
    /// </summary>
    [HttpPost("{id}/fingerprint")]
    public async Task<IActionResult> EnrollFingerprint(int id, [FromBody] FingerprintEnrollRequest request)
    {
        request.UserId = id;
        var success = await _fingerprintService.EnrollFingerprintAsync(request);
        return Ok(new { success, message = success ? "Fingerprint enrolled" : "Enrollment failed" });
    }

    /// <summary>
    /// Get user's fingerprint status
    /// </summary>
    [HttpGet("{id}/fingerprint")]
    public async Task<IActionResult> GetFingerprintStatus(int id)
    {
        var fingerprint = await _fingerprintService.GetUserFingerprintAsync(id);
        
        return Ok(new 
        { 
            success = true, 
            hasFingerprint = fingerprint != null,
            registeredAt = fingerprint?.RegisteredAt
        });
    }
}
