namespace AMS.API.Models;

public class Attendance
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Position { get; set; } = "staff";
    public TimeSpan? AmTimeIn { get; set; }
    public TimeSpan? AmTimeOut { get; set; }
    public TimeSpan? PmTimeIn { get; set; }
    public TimeSpan? PmTimeOut { get; set; }
    public DateTime Date { get; set; }
    public string Remarks { get; set; } = "Ontime";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Navigation
    public string? Username { get; set; }
}

public class AttendanceRecord
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string? AmTimeIn { get; set; }
    public string? AmTimeOut { get; set; }
    public string? PmTimeIn { get; set; }
    public string? PmTimeOut { get; set; }
    public string Remarks { get; set; } = "Ontime";
}

public class TimeLogRequest
{
    public string? FingerprintTemplate { get; set; }
    public int? UserId { get; set; }
    public string? Mode { get; set; } // "IN" or "OUT"
}

public class TimeLogResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string AttendanceType { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public UserDto? User { get; set; }
}
public class DashboardSummary
{
    public bool Success { get; set; }
    public string Date { get; set; } = string.Empty;
    public int TotalOfficials { get; set; }
    public int TotalStaff { get; set; }
    public int Ontime { get; set; }
    public int Late { get; set; }
    public int Undertime { get; set; }
    public int Overtime { get; set; }
    public int EnrolledUsers { get; set; }
}

public class ReportSummary
{
    public List<PatternItem> Patterns { get; set; } = new();
    public ChartData Chart { get; set; } = new();
}

public class PatternItem
{
    public string Pattern { get; set; } = string.Empty;
    public int Support { get; set; }
}

public class ChartData
{
    public List<string> Labels { get; set; } = new();
    public List<int> Ontime { get; set; } = new();
    public List<int> Late { get; set; } = new();
    public List<int> Undertime { get; set; } = new();
    public List<int> Overtime { get; set; } = new();
}

public class TrendData
{
    public bool Ok { get; set; }
    public List<TrendDataRow> Data { get; set; } = new();
}

public class TrendDataRow
{
    [System.Text.Json.Serialization.JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("ontime")]
    public int Ontime { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("late")]
    public int Late { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("undertime")]
    public int Undertime { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("overtime")]
    public int Overtime { get; set; }
}