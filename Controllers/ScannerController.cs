using AMS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMS.API.Controllers;

/// <summary>
/// Controller for ZK Live20R fingerprint scanner hardware management
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ScannerController : ControllerBase
{
    private readonly IScannerService _scannerService;
    private readonly ILogger<ScannerController> _logger;

    public ScannerController(IScannerService scannerService, ILogger<ScannerController> logger)
    {
        _scannerService = scannerService;
        _logger = logger;
    }

    /// <summary>
    /// Get the current status of the fingerprint scanner
    /// Includes SDK status, device count, and connection state
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var result = await _scannerService.GetStatusAsync();
        return Ok(new
        {
            success = result.Success,
            message = result.Message,
            sdk = new
            {
                initialized = result.SdkInitialized,
                platform = Environment.OSVersion.Platform.ToString(),
                is64Bit = Environment.Is64BitProcess
            },
            devices = new
            {
                count = result.DeviceCount,
                connected = result.DeviceOpen,
                info = result.DeviceInfo != null ? new
                {
                    model = result.DeviceInfo.Model,
                    width = result.DeviceInfo.Width,
                    height = result.DeviceInfo.Height,
                    dpi = result.DeviceInfo.Dpi
                } : null
            }
        });
    }

    /// <summary>
    /// Initialize the ZKFinger SDK
    /// Must be called before any other scanner operations
    /// </summary>
    [HttpPost("initialize")]
    public async Task<IActionResult> Initialize()
    {
        _logger.LogInformation("Scanner initialization requested");
        var result = await _scannerService.InitializeAsync();
        
        return Ok(new
        {
            success = result.Success,
            message = result.Message,
            sdkInitialized = result.SdkInitialized,
            deviceCount = result.DeviceCount
        });
    }

    /// <summary>
    /// Detect connected fingerprint scanners
    /// Returns the count of connected devices
    /// </summary>
    [HttpGet("detect")]
    public async Task<IActionResult> DetectDevices()
    {
        // Auto-initialize if not already done
        if (!_scannerService.IsInitialized)
        {
            var initResult = await _scannerService.InitializeAsync();
            if (!initResult.Success)
            {
                return Ok(new
                {
                    success = false,
                    message = initResult.Message,
                    deviceCount = 0,
                    devices = Array.Empty<object>()
                });
            }
        }

        var status = await _scannerService.GetStatusAsync();
        
        // Build device list
        var devices = new List<object>();
        for (int i = 0; i < status.DeviceCount; i++)
        {
            devices.Add(new
            {
                index = i,
                name = $"ZK Live20R #{i + 1}",
                type = "USB Fingerprint Scanner"
            });
        }

        return Ok(new
        {
            success = true,
            message = status.DeviceCount > 0 
                ? $"Found {status.DeviceCount} fingerprint scanner(s)" 
                : "No fingerprint scanners detected",
            deviceCount = status.DeviceCount,
            devices = devices
        });
    }

    /// <summary>
    /// Open/connect to a fingerprint scanner
    /// </summary>
    /// <param name="deviceIndex">Device index (default: 0)</param>
    [HttpPost("connect")]
    public async Task<IActionResult> Connect([FromQuery] int deviceIndex = 0)
    {
        // Auto-initialize if not already done
        if (!_scannerService.IsInitialized)
        {
            var initResult = await _scannerService.InitializeAsync();
            if (!initResult.Success)
            {
                return Ok(new
                {
                    success = false,
                    message = $"Failed to initialize SDK: {initResult.Message}",
                    connected = false
                });
            }
        }

        _logger.LogInformation("Connecting to scanner at index {Index}", deviceIndex);
        var result = await _scannerService.OpenDeviceAsync(deviceIndex);

        return Ok(new
        {
            success = result.Success,
            message = result.Message,
            connected = result.DeviceOpen,
            device = result.DeviceInfo != null ? new
            {
                model = result.DeviceInfo.Model,
                width = result.DeviceInfo.Width,
                height = result.DeviceInfo.Height,
                dpi = result.DeviceInfo.Dpi
            } : null
        });
    }

    /// <summary>
    /// Disconnect from the current scanner
    /// </summary>
    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect()
    {
        _logger.LogInformation("Disconnecting from scanner");
        var result = await _scannerService.CloseDeviceAsync();

        return Ok(new
        {
            success = result.Success,
            message = result.Message,
            connected = result.DeviceOpen
        });
    }

    /// <summary>
    /// Capture a single fingerprint from the connected scanner
    /// Returns the template as Base64 and optionally the image
    /// </summary>
    [HttpPost("capture")]
    public async Task<IActionResult> Capture()
    {
        if (!_scannerService.IsDeviceConnected)
        {
            // Try to auto-connect
            var connectResult = await _scannerService.OpenDeviceAsync();
            if (!connectResult.Success)
            {
                return Ok(new
                {
                    success = false,
                    message = "No scanner connected. Please connect a scanner first.",
                    template = (string?)null
                });
            }
        }

        _logger.LogInformation("Capturing fingerprint");
        var result = await _scannerService.CaptureAsync();

        return Ok(new
        {
            success = result.Success,
            message = result.Message,
            template = result.Template,
            image = result.Image,
            dimensions = result.Success ? new
            {
                width = result.ImageWidth,
                height = result.ImageHeight
            } : null
        });
    }

    /// <summary>
    /// Enroll a fingerprint (capture 3 times and merge)
    /// Used for user registration
    /// </summary>
    [HttpPost("enroll")]
    public async Task<IActionResult> Enroll([FromQuery] int captures = 3)
    {
        if (!_scannerService.IsDeviceConnected)
        {
            var connectResult = await _scannerService.OpenDeviceAsync();
            if (!connectResult.Success)
            {
                return Ok(new
                {
                    success = false,
                    message = "No scanner connected. Please connect a scanner first.",
                    template = (string?)null
                });
            }
        }

        if (captures < 1 || captures > 5)
        {
            return BadRequest(new
            {
                success = false,
                message = "Capture count must be between 1 and 5"
            });
        }

        _logger.LogInformation("Starting enrollment with {Captures} captures", captures);
        var result = await _scannerService.EnrollAsync(captures);

        return Ok(new
        {
            success = result.Success,
            message = result.Message,
            template = result.Template,
            captureCount = result.CaptureCount
        });
    }

    /// <summary>
    /// Match two fingerprint templates
    /// Returns the match score (0 = no match, higher = better match)
    /// </summary>
    [HttpPost("match")]
    public IActionResult Match([FromBody] MatchRequest request)
    {
        if (string.IsNullOrEmpty(request.Template1) || string.IsNullOrEmpty(request.Template2))
        {
            return BadRequest(new
            {
                success = false,
                message = "Both templates are required"
            });
        }

        try
        {
            var score = _scannerService.MatchTemplates(request.Template1, request.Template2);
            var threshold = int.Parse(Environment.GetEnvironmentVariable("MATCH_THRESHOLD") ?? "50");

            return Ok(new
            {
                success = true,
                matched = score >= threshold,
                score = score,
                threshold = threshold,
                message = score >= threshold ? "Templates match" : "Templates do not match"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error matching templates");
            return Ok(new
            {
                success = false,
                matched = false,
                score = 0,
                message = $"Error matching templates: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Verify fingerprint against a user's stored template
    /// </summary>
    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyRequest request)
    {
        if (request.UserId <= 0)
        {
            return BadRequest(new
            {
                success = false,
                message = "Valid User ID is required"
            });
        }

        try
        {
            if (!_scannerService.IsInitialized)
            {
                return Ok(new
                {
                    success = false,
                    message = "Scanner SDK is not initialized"
                });
            }

            // Capture new fingerprint
            var captureResult = await _scannerService.CaptureAsync();
            if (!captureResult.Success)
            {
                return Ok(new
                {
                    success = false,
                    message = $"Failed to capture fingerprint: {captureResult.Message}"
                });
            }

            // For now, return simulated match
            // In production, this would fetch user template from database and compare
            var matchScore = 75; // Simulated score
            var threshold = int.Parse(Environment.GetEnvironmentVariable("MATCH_THRESHOLD") ?? "50");

            return Ok(new
            {
                success = true,
                matched = matchScore >= threshold,
                matchScore = matchScore,
                threshold = threshold,
                userId = request.UserId,
                message = matchScore >= threshold ? "Fingerprint verified successfully" : "Fingerprint does not match"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying fingerprint");
            return Ok(new
            {
                success = false,
                message = $"Error verifying fingerprint: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Health check endpoint for the scanner subsystem
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "healthy",
            scanner = new
            {
                sdkInitialized = _scannerService.IsInitialized,
                deviceConnected = _scannerService.IsDeviceConnected,
                deviceInfo = _scannerService.DeviceInfo
            },
            platform = new
            {
                os = Environment.OSVersion.ToString(),
                is64Bit = Environment.Is64BitProcess,
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
            }
        });
    }
}

/// <summary>
/// Request model for template matching
/// </summary>
public class MatchRequest
{
    public string Template1 { get; set; } = string.Empty;
    public string Template2 { get; set; } = string.Empty;
}

/// <summary>
/// Request model for fingerprint verification
/// </summary>
public class VerifyRequest
{
    public int UserId { get; set; }
}

