using AMS.API.Models;
using AMS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AMS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _attendanceService;
    private readonly IFingerprintService _fingerprintService;

    public AttendanceController(IAttendanceService attendanceService, IFingerprintService fingerprintService)
    {
        _attendanceService = attendanceService;
        _fingerprintService = fingerprintService;
    }

    /// <summary>
    /// Record attendance via fingerprint (ZK Live20R)
    /// Auto-detects AM/PM and Time In/Out
    /// </summary>
    [HttpPost("fingerprint")]
    public async Task<IActionResult> RecordViaFingerprint([FromBody] TimeLogRequest request)
    {
        if (string.IsNullOrEmpty(request.FingerprintTemplate))
        {
            return BadRequest(new { success = false, message = "Fingerprint template is required" });
        }

        // Identify user by fingerprint
        var fpResult = await _fingerprintService.IdentifyUserAsync(request.FingerprintTemplate);

        if (!fpResult.Matched || fpResult.UserId == null)
        {
            return NotFound(new { success = false, message = "Fingerprint not recognized" });
        }

        // Record time log with manual mode if provided
        var mode = request.Mode?.ToUpper(); // "IN" or "OUT"
        var result = await _attendanceService.RecordTimeLogAsync(fpResult.UserId.Value, request.FingerprintTemplate, mode);

        return Ok(new 
        {
            result.Success,
            result.Message,
            result.AttendanceType,
            result.Time,
            result.Remarks,
            user = new 
            {
                id = fpResult.UserId,
                username = fpResult.Username,
                position = fpResult.Position
            }
        });
    }

    /// <summary>
    /// Record attendance by user ID (fallback for when fingerprint isn't available)
    /// </summary>
    [HttpPost("timelog/{userId}")]
    [Authorize]
    public async Task<IActionResult> RecordTimeLog(int userId)
    {
        var result = await _attendanceService.RecordTimeLogAsync(userId);
        return Ok(result);
    }

    /// <summary>
    /// Get today's attendance logs
    /// </summary>
    [HttpGet("today")]
    public async Task<IActionResult> GetTodayLogs()
    {
        var logs = await _attendanceService.GetTodayLogsAsync();
        return Ok(new { success = true, count = logs.Count, logs });
    }

    /// <summary>
    /// Get attendance for a specific user
    /// </summary>
    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserAttendance(
        int userId, 
        [FromQuery] DateTime? startDate, 
        [FromQuery] DateTime? endDate)
    {
        var records = await _attendanceService.GetUserAttendanceAsync(userId, startDate, endDate);
        return Ok(new { success = true, count = records.Count, records });
    }

    /// <summary>
    /// Get user's today attendance status
    /// </summary>
    [HttpGet("user/{userId}/today")]
    public async Task<IActionResult> GetUserTodayAttendance(int userId)
    {
        var record = await _attendanceService.GetUserTodayAttendanceAsync(userId);
        
        if (record == null)
        {
            return Ok(new 
            { 
                success = true, 
                hasRecord = false,
                message = "No attendance record for today"
            });
        }

        return Ok(new 
        { 
            success = true, 
            hasRecord = true,
            record 
        });
    }

    /// <summary>
    /// Get all attendance records (Admin only)
    /// </summary>
    [HttpGet("all")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> GetAllAttendance(
        [FromQuery] DateTime? startDate, 
        [FromQuery] DateTime? endDate)
    {
        var records = await _attendanceService.GetAllAttendanceAsync(startDate, endDate);
        return Ok(new { success = true, count = records.Count, records });
    }

    /// <summary>
    /// Get attendance list (public endpoint for list page)
    /// </summary>
    [HttpGet("list")]
    public async Task<IActionResult> GetAttendanceList(
        [FromQuery] DateTime? startDate, 
        [FromQuery] DateTime? endDate)
    {
        var records = await _attendanceService.GetAllAttendanceAsync(startDate, endDate);
        return Ok(new { success = true, count = records.Count, records });
    }

    /// <summary>
    /// Get attendance records (for records page with filters)
    /// </summary>
    [HttpGet("records")]
    public async Task<IActionResult> GetAttendanceRecords(
        [FromQuery] string? searchName,
        [FromQuery] DateTime? fromDate, 
        [FromQuery] DateTime? toDate)
    {
        var records = await _attendanceService.GetAllAttendanceAsync(fromDate, toDate);
        
        // Filter by name if provided
        if (!string.IsNullOrEmpty(searchName))
        {
            records = records.Where(r => 
                r.Username != null && 
                r.Username.Contains(searchName, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }
        
        // Map to expected format
        var data = records.Select(r => new 
        {
            employee = r.Username,
            position = r.Position,
            date = r.Date,
            am_time_in = r.AmTimeIn,
            am_time_out = r.AmTimeOut,
            pm_time_in = r.PmTimeIn,
            pm_time_out = r.PmTimeOut,
            remarks = r.Remarks
        }).ToList();
        
        return Ok(new { success = true, totalRecords = data.Count, data });
    }

    /// <summary>
    /// Get dashboard summary for today
    /// </summary>
    [HttpGet("home/dashboard-summary")]
    public async Task<IActionResult> GetDashboardSummary()
    {
        var summary = await _attendanceService.GetDashboardSummaryAsync();
        return Ok(summary);
    }

    /// <summary>
    /// Get monthly report with FP-Growth patterns and trend chart data
    /// </summary>
    [HttpGet("report/summary")]
    public async Task<IActionResult> GetReportSummary(
        [FromQuery] string month, 
        [FromQuery] string? position = null, 
        [FromQuery] string? searchName = null)
    {
        if (string.IsNullOrEmpty(month))
        {
            return BadRequest(new { success = false, message = "Month is required" });
        }

        var summary = await _attendanceService.GetReportSummaryAsync(month, position, searchName);
        return Ok(summary);
    }

    /// <summary>
    /// Get daily attendance trend data for charts (Records page)
    /// </summary>
    [HttpGet("trend")]
    public async Task<IActionResult> GetTrend(
        [FromQuery] string? employee = null, 
        [FromQuery] string? month = null)
    {
        var trend = await _attendanceService.GetTrendAsync(employee, month);
        return Ok(trend);
    }
}