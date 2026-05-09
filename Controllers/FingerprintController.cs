using AMS.API.Models;
using AMS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using MySqlConnector;

namespace AMS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FingerprintController : ControllerBase
{
    private readonly IFingerprintService _fingerprintService;

    public FingerprintController(IFingerprintService fingerprintService)
    {
        _fingerprintService = fingerprintService;
    }

    /// <summary>
    /// Verify/Identify fingerprint (1:N matching)
    /// Returns matched user if found
    /// </summary>
    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] FingerprintVerifyRequest request)
    {
        if (string.IsNullOrEmpty(request.FingerprintTemplate))
        {
            return BadRequest(new { success = false, message = "Fingerprint template is required" });
        }

        var result = await _fingerprintService.IdentifyUserAsync(request.FingerprintTemplate);
        return Ok(result);
    }

    /// <summary>
    /// Enroll fingerprint for a user
    /// </summary>
    [HttpPost("enroll")]
    public async Task<IActionResult> Enroll([FromBody] FingerprintEnrollRequest request)
    {
        if (request.UserId <= 0 || string.IsNullOrEmpty(request.FingerprintTemplate))
        {
            return BadRequest(new { success = false, message = "User ID and fingerprint template are required" });
        }

        var success = await _fingerprintService.EnrollFingerprintAsync(request);

        return Ok(new { success, message = success ? "Fingerprint enrolled successfully" : "Failed to enroll fingerprint" });
    }

    /// <summary>
    /// Get all fingerprint templates (for client-side matching)
    /// </summary>
    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates()
    {
        var templates = await _fingerprintService.GetAllTemplatesAsync();

        return Ok(new 
        { 
            success = true, 
            count = templates.Count,
            templates = templates.Select(t => new 
            {
                userId = t.UserId,
                username = t.Username,
                template = t.FingerprintTemplate,
                fingerIndex = t.FingerIndex
            })
        });
    }

    /// <summary>
    /// Debug: Get raw database info
    /// </summary>
    [HttpGet("debug/count")]
    public async Task<IActionResult> GetDebugCount()
    {
        using var conn = new MySqlConnector.MySqlConnection("Server=localhost;User=root2;Password=blaise;Database=ams_db;");
        await conn.OpenAsync();

        var fingerprintCount = (long)await conn.ExecuteScalarAsync("SELECT COUNT(*) FROM fingerprints");
        var usersWithFp = (long)await conn.ExecuteScalarAsync("SELECT COUNT(*) FROM users WHERE fingerprint_template IS NOT NULL AND fingerprint_template != ''");

        var fingerprints = await conn.QueryAsync<dynamic>(
            "SELECT id, user_id, finger_index, LENGTH(fingerprint_template) as template_length FROM fingerprints");
        
        var users = await conn.QueryAsync<dynamic>(
            "SELECT id, username, role, LENGTH(fingerprint_template) as template_length FROM users WHERE fingerprint_template IS NOT NULL AND fingerprint_template != ''");

        return Ok(new
        {
            fingerprints_table_count = fingerprintCount,
            users_with_fingerprint_count = usersWithFp,
            total = fingerprintCount + usersWithFp,
            fingerprints_details = fingerprints,
            users_details = users
        });
    }

    /// <summary>
    /// Get fingerprint for a specific user
    /// </summary>
    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserFingerprint(int userId)
    {
        var fingerprint = await _fingerprintService.GetUserFingerprintAsync(userId);

        if (fingerprint == null)
        {
            return NotFound(new { success = false, message = "Fingerprint not found" });
        }

        return Ok(new 
        { 
            success = true, 
            fingerprint = new 
            {
                userId = fingerprint.UserId,
                template = fingerprint.FingerprintTemplate,
                fingerIndex = fingerprint.FingerIndex,
                registeredAt = fingerprint.RegisteredAt
            }
        });
    }

    /// <summary>
    /// Delete fingerprint for a user
    /// </summary>
    [HttpDelete("user/{userId}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteFingerprint(int userId)
    {
        var success = await _fingerprintService.DeleteFingerprintAsync(userId);
        return Ok(new { success, message = "Fingerprint deleted" });
    }

    /// <summary>
    /// ZK Live20R device status check
    /// This endpoint is for the frontend to check if the API is ready
    /// </summary>
    [HttpGet("status")]
    public IActionResult Status()
    {
        return Ok(new 
        { 
            success = true, 
            message = "Fingerprint API ready",
            device = "ZK Live20R",
            matchThreshold = Environment.GetEnvironmentVariable("MATCH_THRESHOLD") ?? "50"
        });
    }
}
