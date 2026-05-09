namespace AMS.API.Models;

public class Fingerprint
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string FingerprintTemplate { get; set; } = string.Empty;
    public int FingerIndex { get; set; }
    public DateTime RegisteredAt { get; set; }
    
    /// <summary>
    /// Quality score from SDK (0-100)
    /// </summary>
    public int? Quality { get; set; }
    
    /// <summary>
    /// Number of captures used to create this template
    /// </summary>
    public int CaptureCount { get; set; } = 1;
    
    // Navigation
    public string? Username { get; set; }
}

public class FingerprintEnrollRequest
{
    public int UserId { get; set; }
    public string FingerprintTemplate { get; set; } = string.Empty;
    public int FingerIndex { get; set; } = 0;
    public int? Quality { get; set; }
    public int CaptureCount { get; set; } = 1;
}

public class FingerprintVerifyRequest
{
    public string FingerprintTemplate { get; set; } = string.Empty;
}

public class FingerprintVerifyResponse
{
    public bool Success { get; set; }
    public bool Matched { get; set; }
    public int? UserId { get; set; }
    public string? Username { get; set; }
    public string? Position { get; set; }
    public double Score { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class FingerprintStatusResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool ScannerConnected { get; set; }
    public string? ScannerModel { get; set; }
    public int EnrolledCount { get; set; }
}

