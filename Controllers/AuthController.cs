using AMS.API.Models;
using AMS.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace AMS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Login with username and password
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return BadRequest(new { success = false, message = "Username and password are required" });
        }

        var result = await _authService.LoginAsync(request);

        if (!result.Success)
        {
            return Unauthorized(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Register a new user
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return BadRequest(new { success = false, message = "Username and password are required" });
        }

        var result = await _authService.RegisterAsync(request);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Login with fingerprint (ZK Live20R)
    /// </summary>
    [HttpPost("fingerprint-login")]
    public async Task<IActionResult> FingerprintLogin([FromBody] FingerprintLoginRequest request)
    {
        if (string.IsNullOrEmpty(request.FingerprintTemplate))
        {
            return BadRequest(new { success = false, message = "Fingerprint template is required" });
        }

        var result = await _authService.LoginWithFingerprintAsync(request.FingerprintTemplate);

        if (!result.Success)
        {
            return Unauthorized(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Check if API is running
    /// </summary>
    [HttpGet("status")]
    public IActionResult Status()
    {
        return Ok(new 
        { 
            success = true, 
            message = "AMS API is running",
            version = "1.0.0",
            dotnet = "9.0",
            device = "ZK Live20R"
        });
    }
}
