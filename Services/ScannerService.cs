using AMS.API.Interop.ZKFinger;

namespace AMS.API.Services;

/// <summary>
/// Scanner detection and device management service
/// </summary>
public interface IScannerService : IDisposable
{
    /// <summary>
    /// Check if the SDK is initialized and ready
    /// </summary>
    bool IsInitialized { get; }
    
    /// <summary>
    /// Check if a device is currently open and connected
    /// </summary>
    bool IsDeviceConnected { get; }
    
    /// <summary>
    /// Get the device information (dimensions, DPI)
    /// </summary>
    ScannerInfo? DeviceInfo { get; }
    
    /// <summary>
    /// Initialize the SDK and detect connected scanners
    /// </summary>
    Task<ScannerStatusResult> InitializeAsync();
    
    /// <summary>
    /// Get current status of connected scanners
    /// </summary>
    Task<ScannerStatusResult> GetStatusAsync();
    
    /// <summary>
    /// Open a scanner device for capture
    /// </summary>
    Task<ScannerStatusResult> OpenDeviceAsync(int deviceIndex = 0);
    
    /// <summary>
    /// Close the currently open device
    /// </summary>
    Task<ScannerStatusResult> CloseDeviceAsync();
    
    /// <summary>
    /// Capture a fingerprint from the open device
    /// </summary>
    Task<FingerprintCaptureResult> CaptureAsync();
    
    /// <summary>
    /// Capture and merge 3 fingerprints into a registration template
    /// </summary>
    Task<FingerprintEnrollmentResult> EnrollAsync(int captureCount = 3);
    
    /// <summary>
    /// Match two templates using SDK algorithm
    /// </summary>
    int MatchTemplates(byte[] template1, byte[] template2);
    
    /// <summary>
    /// Match two Base64 encoded templates
    /// </summary>
    int MatchTemplates(string base64Template1, string base64Template2);
}

public class ScannerService : IScannerService
{
    private readonly ILogger<ScannerService> _logger;
    private readonly ZKFPWrapper _zkfp;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _isDisposed = false;

    public ScannerService(ILogger<ScannerService> logger)
    {
        _logger = logger;
        _zkfp = new ZKFPWrapper();
    }

    public bool IsInitialized => _zkfp.IsInitialized;
    public bool IsDeviceConnected => _zkfp.IsDeviceOpen;
    
    public ScannerInfo? DeviceInfo => _zkfp.IsDeviceOpen 
        ? new ScannerInfo 
        { 
            Width = _zkfp.ImageWidth, 
            Height = _zkfp.ImageHeight, 
            Dpi = _zkfp.ImageDpi,
            Model = "ZK Live20R"
        } 
        : null;

    public async Task<ScannerStatusResult> InitializeAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            _logger.LogInformation("Initializing ZKFinger SDK...");
            
            try
            {
                var result = _zkfp.Initialize();
                if (!result.Success)
                {
                    _logger.LogError("Failed to initialize SDK: {Message}", result.Message);
                    return new ScannerStatusResult
                    {
                        Success = false,
                        Message = result.Message,
                        SdkInitialized = false,
                        DeviceCount = 0
                    };
                }

                // Initialize DB for matching
                var dbResult = _zkfp.InitializeDB();
                if (!dbResult.Success)
                {
                    _logger.LogWarning("Failed to initialize matching database: {Message}", dbResult.Message);
                }

                var deviceCount = _zkfp.GetDeviceCount();
                _logger.LogInformation("SDK initialized. Found {Count} device(s)", deviceCount);

                return new ScannerStatusResult
                {
                    Success = true,
                    Message = $"SDK initialized, {deviceCount} device(s) found",
                    SdkInitialized = true,
                    DeviceCount = deviceCount
                };
            }
            catch (DllNotFoundException dllEx)
            {
                _logger.LogError(dllEx, "ZKFinger SDK library not found. This is expected if the hardware SDK is not installed.");
                return new ScannerStatusResult
                {
                    Success = false,
                    Message = "ZKFinger SDK not available - hardware scanner unavailable. Please install the SDK drivers.",
                    SdkInitialized = false,
                    DeviceCount = 0
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during SDK initialization");
            return new ScannerStatusResult
            {
                Success = false,
                Message = $"SDK initialization failed: {ex.Message}",
                SdkInitialized = false,
                DeviceCount = 0
            };
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<ScannerStatusResult> GetStatusAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!_zkfp.IsInitialized)
            {
                return new ScannerStatusResult
                {
                    Success = true,
                    Message = "SDK not initialized",
                    SdkInitialized = false,
                    DeviceCount = 0
                };
            }

            var deviceCount = _zkfp.GetDeviceCount();
            
            return new ScannerStatusResult
            {
                Success = true,
                Message = deviceCount > 0 ? $"{deviceCount} device(s) connected" : "No devices connected",
                SdkInitialized = true,
                DeviceCount = deviceCount,
                DeviceOpen = _zkfp.IsDeviceOpen,
                DeviceInfo = DeviceInfo
            };
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<ScannerStatusResult> OpenDeviceAsync(int deviceIndex = 0)
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!_zkfp.IsInitialized)
            {
                _logger.LogWarning("Attempting to open device but SDK not initialized");
                return new ScannerStatusResult
                {
                    Success = false,
                    Message = "SDK not initialized. Call Initialize first.",
                    SdkInitialized = false
                };
            }

            if (_zkfp.IsDeviceOpen)
            {
                _logger.LogInformation("Device already open");
                return new ScannerStatusResult
                {
                    Success = true,
                    Message = "Device already open",
                    SdkInitialized = true,
                    DeviceCount = _zkfp.GetDeviceCount(),
                    DeviceOpen = true,
                    DeviceInfo = DeviceInfo
                };
            }

            var deviceCount = _zkfp.GetDeviceCount();
            if (deviceCount == 0)
            {
                _logger.LogWarning("No fingerprint devices connected");
                return new ScannerStatusResult
                {
                    Success = false,
                    Message = "No fingerprint devices connected",
                    SdkInitialized = true,
                    DeviceCount = 0
                };
            }

            if (deviceIndex >= deviceCount)
            {
                return new ScannerStatusResult
                {
                    Success = false,
                    Message = $"Device index {deviceIndex} out of range. Only {deviceCount} device(s) available.",
                    SdkInitialized = true,
                    DeviceCount = deviceCount
                };
            }

            var result = _zkfp.OpenDevice(deviceIndex);
            if (!result.Success)
            {
                _logger.LogError("Failed to open device: {Message}", result.Message);
                return new ScannerStatusResult
                {
                    Success = false,
                    Message = result.Message,
                    SdkInitialized = true,
                    DeviceCount = deviceCount
                };
            }

            _logger.LogInformation("Device opened: {Width}x{Height} @ {DPI}dpi", 
                _zkfp.ImageWidth, _zkfp.ImageHeight, _zkfp.ImageDpi);

            return new ScannerStatusResult
            {
                Success = true,
                Message = "Device opened successfully",
                SdkInitialized = true,
                DeviceCount = deviceCount,
                DeviceOpen = true,
                DeviceInfo = DeviceInfo
            };
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<ScannerStatusResult> CloseDeviceAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            var result = _zkfp.CloseDevice();
            
            return new ScannerStatusResult
            {
                Success = result.Success,
                Message = result.Message,
                SdkInitialized = _zkfp.IsInitialized,
                DeviceCount = _zkfp.IsInitialized ? _zkfp.GetDeviceCount() : 0,
                DeviceOpen = false
            };
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<FingerprintCaptureResult> CaptureAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!_zkfp.IsDeviceOpen)
            {
                return new FingerprintCaptureResult
                {
                    Success = false,
                    Message = "Device not open. Call OpenDevice first."
                };
            }

            var result = _zkfp.CaptureFingerprint();
            
            if (!result.Success)
            {
                return new FingerprintCaptureResult
                {
                    Success = false,
                    Message = result.Message
                };
            }

            return new FingerprintCaptureResult
            {
                Success = true,
                Template = result.Template != null ? Convert.ToBase64String(result.Template) : null,
                Image = result.Image != null ? Convert.ToBase64String(result.Image) : null,
                ImageWidth = result.ImageWidth,
                ImageHeight = result.ImageHeight,
                Message = "Fingerprint captured successfully"
            };
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<FingerprintEnrollmentResult> EnrollAsync(int captureCount = 3)
    {
        var captures = new List<byte[]>();
        
        for (int i = 0; i < captureCount; i++)
        {
            var captureResult = await CaptureAsync();
            
            if (!captureResult.Success || string.IsNullOrEmpty(captureResult.Template))
            {
                return new FingerprintEnrollmentResult
                {
                    Success = false,
                    Message = $"Capture {i + 1} failed: {captureResult.Message}",
                    CaptureCount = i
                };
            }

            captures.Add(Convert.FromBase64String(captureResult.Template));
        }

        // Merge the captures
        await _semaphore.WaitAsync();
        try
        {
            if (captures.Count == 3)
            {
                var mergeResult = _zkfp.MergeTemplates(captures[0], captures[1], captures[2]);
                
                if (!mergeResult.Success || mergeResult.RegisteredTemplate == null)
                {
                    return new FingerprintEnrollmentResult
                    {
                        Success = false,
                        Message = $"Failed to merge templates: {mergeResult.Message}",
                        CaptureCount = captureCount
                    };
                }

                return new FingerprintEnrollmentResult
                {
                    Success = true,
                    Template = Convert.ToBase64String(mergeResult.RegisteredTemplate),
                    CaptureCount = captureCount,
                    Message = "Enrollment successful"
                };
            }
            else
            {
                // Just use the first capture if not 3
                return new FingerprintEnrollmentResult
                {
                    Success = true,
                    Template = Convert.ToBase64String(captures[0]),
                    CaptureCount = captures.Count,
                    Message = "Enrollment successful (single capture)"
                };
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public int MatchTemplates(byte[] template1, byte[] template2)
    {
        _logger.LogInformation("📊 MatchTemplates called - Template1: {Len1} bytes, Template2: {Len2} bytes", template1.Length, template2.Length);
        
        if (!_zkfp.IsDBInitialized)
        {
            _logger.LogInformation("   DB not initialized, initializing now...");
            var dbResult = _zkfp.InitializeDB();
            _logger.LogInformation("   DB initialization result: Success={Success}", dbResult.Success);
        }

        var result = _zkfp.Match(template1, template2);
        _logger.LogInformation("   SDK Match result: Success={Success}, Score={Score}, Matched={Matched}, Message={Message}",
            result.Success, result.Score, result.Matched, result.Message);
        return result.Score;
    }

    public int MatchTemplates(string base64Template1, string base64Template2)
    {
        try
        {
            _logger.LogDebug("   Converting Base64 strings to byte arrays...");
            var template1 = Convert.FromBase64String(base64Template1);
            var template2 = Convert.FromBase64String(base64Template2);
            return MatchTemplates(template1, template2);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "   ❌ Failed to decode Base64 templates");
            return 0;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                _zkfp.Dispose();
                _semaphore.Dispose();
            }
            _isDisposed = true;
        }
    }
}

// ============================================
// Result Models
// ============================================

public class ScannerInfo
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int Dpi { get; set; }
    public string Model { get; set; } = "ZK Live20R";
}

public class ScannerStatusResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool SdkInitialized { get; set; }
    public int DeviceCount { get; set; }
    public bool DeviceOpen { get; set; }
    public ScannerInfo? DeviceInfo { get; set; }
}

public class FingerprintCaptureResult
{
    public bool Success { get; set; }
    public string? Template { get; set; }
    public string? Image { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class FingerprintEnrollmentResult
{
    public bool Success { get; set; }
    public string? Template { get; set; }
    public int CaptureCount { get; set; }
    public string Message { get; set; } = string.Empty;
}
