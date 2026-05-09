using AMS.API.Data;
using AMS.API.Models;
using Dapper;

namespace AMS.API.Services;

public interface IAttendanceService
{
    Task<TimeLogResponse> RecordTimeLogAsync(int userId, string? fingerprintTemplate = null, string? mode = null);
    Task<List<AttendanceRecord>> GetTodayLogsAsync();
    Task<List<AttendanceRecord>> GetUserAttendanceAsync(int userId, DateTime? startDate, DateTime? endDate);
    Task<List<AttendanceRecord>> GetAllAttendanceAsync(DateTime? startDate, DateTime? endDate);
    Task<AttendanceRecord?> GetUserTodayAttendanceAsync(int userId);
    Task<DashboardSummary> GetDashboardSummaryAsync();
    Task<ReportSummary> GetReportSummaryAsync(string month, string? position, string? searchName);
    Task<TrendData> GetTrendAsync(string? employee, string? month);
}

public class AttendanceService : IAttendanceService
{
    private readonly DatabaseContext _db;
    private readonly IFingerprintService _fingerprintService;

    public AttendanceService(DatabaseContext db, IFingerprintService fingerprintService)
    {
        _db = db;
        _fingerprintService = fingerprintService;
    }

    /// <summary>
    /// Record time log (Time In/Out) with manual or automatic AM/PM detection
    /// </summary>
    public async Task<TimeLogResponse> RecordTimeLogAsync(int userId, string? fingerprintTemplate = null, string? mode = null)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        // Get user info
        var user = await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM users WHERE id = @Id",
            new { Id = userId }
        );

        if (user == null)
        {
            return new TimeLogResponse { Success = false, Message = "User not found" };
        }

        var now = DateTime.Now;
        var currentTime = now.TimeOfDay;
        var today = now.Date;
        var isAM = now.Hour < 12;

        // Get or create today's attendance record
        var attendance = await conn.QueryFirstOrDefaultAsync<Attendance>(
            "SELECT * FROM attendance WHERE user_id = @UserId AND date = @Date",
            new { UserId = userId, Date = today }
        );

        string attendanceType;
        string remarks = "Ontime";
        
        // Manual mode: User selected IN or OUT
        if (!string.IsNullOrEmpty(mode))
        {
            if (mode == "IN")
            {
                // Time IN - determine AM or PM
                if (isAM)
                {
                    attendanceType = "AM Time In";
                    if (currentTime > new TimeSpan(8, 0, 0)) remarks = "LATE";
                    
                    if (attendance == null)
                    {
                        await conn.ExecuteAsync(@"
                            INSERT INTO attendance (user_id, position, am_time_in, date, remarks)
                            VALUES (@UserId, @Position, @TimeIn, @Date, @Remarks)
                        ", new { UserId = userId, Position = user.Position, TimeIn = currentTime, Date = today, Remarks = remarks });
                    }
                    else
                    {
                        await conn.ExecuteAsync(
                            "UPDATE attendance SET am_time_in = @Time, remarks = @Remarks WHERE id = @Id",
                            new { Time = currentTime, Remarks = remarks, Id = attendance.Id }
                        );
                    }
                }
                else
                {
                    attendanceType = "PM Time In";
                    if (currentTime > new TimeSpan(13, 0, 0)) remarks = "LATE";
                    
                    if (attendance == null)
                    {
                        await conn.ExecuteAsync(@"
                            INSERT INTO attendance (user_id, position, pm_time_in, date, remarks)
                            VALUES (@UserId, @Position, @TimeIn, @Date, @Remarks)
                        ", new { UserId = userId, Position = user.Position, TimeIn = currentTime, Date = today, Remarks = remarks });
                    }
                    else
                    {
                        await conn.ExecuteAsync(
                            "UPDATE attendance SET pm_time_in = @Time, remarks = @Remarks WHERE id = @Id",
                            new { Time = currentTime, Remarks = remarks, Id = attendance.Id }
                        );
                    }
                }
            }
            else // OUT
            {
                // Time OUT - determine AM or PM
                if (isAM)
                {
                    attendanceType = "AM Time Out";
                    if (currentTime < new TimeSpan(12, 0, 0)) remarks = "Undertime";
                    
                    if (attendance == null)
                    {
                        await conn.ExecuteAsync(@"
                            INSERT INTO attendance (user_id, position, am_time_out, date, remarks)
                            VALUES (@UserId, @Position, @TimeOut, @Date, @Remarks)
                        ", new { UserId = userId, Position = user.Position, TimeOut = currentTime, Date = today, Remarks = remarks });
                    }
                    else
                    {
                        await conn.ExecuteAsync(
                            "UPDATE attendance SET am_time_out = @Time WHERE id = @Id",
                            new { Time = currentTime, Id = attendance.Id }
                        );
                    }
                }
                else
                {
                    attendanceType = "PM Time Out";
                    if (currentTime < new TimeSpan(17, 0, 0)) remarks = "Undertime";
                    else if (currentTime > new TimeSpan(18, 0, 0)) remarks = "Overtime";
                    
                    if (attendance == null)
                    {
                        await conn.ExecuteAsync(@"
                            INSERT INTO attendance (user_id, position, pm_time_out, date, remarks)
                            VALUES (@UserId, @Position, @TimeOut, @Date, @Remarks)
                        ", new { UserId = userId, Position = user.Position, TimeOut = currentTime, Date = today, Remarks = remarks });
                    }
                    else
                    {
                        await conn.ExecuteAsync(
                            "UPDATE attendance SET pm_time_out = @Time WHERE id = @Id",
                            new { Time = currentTime, Id = attendance.Id }
                        );
                    }
                }
            }
            
            return new TimeLogResponse
            {
                Success = true,
                Message = $"{attendanceType} recorded successfully",
                AttendanceType = attendanceType,
                Time = now.ToString("hh:mm:ss tt"),
                Remarks = remarks,
                User = new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Position = user.Position,
                    Gender = user.Gender
                }
            };
        }

        // Auto mode (original logic)
        TimeSpan? timeValue = currentTime;

        if (attendance == null)
        {
            // First time log today - create new record
            if (isAM)
            {
                // AM Time In
                attendanceType = "AM Time In";
                
                // Check if late (after 8:00 AM)
                if (currentTime > new TimeSpan(8, 0, 0))
                {
                    remarks = "LATE";
                }

                await conn.ExecuteAsync(@"
                    INSERT INTO attendance (user_id, position, am_time_in, date, remarks)
                    VALUES (@UserId, @Position, @TimeIn, @Date, @Remarks)
                ", new { 
                    UserId = userId, 
                    Position = user.Position, 
                    TimeIn = currentTime, 
                    Date = today,
                    Remarks = remarks
                });
            }
            else
            {
                // PM Time In (first log in afternoon)
                attendanceType = "PM Time In";
                
                // Check if late (after 1:00 PM)
                if (currentTime > new TimeSpan(13, 0, 0))
                {
                    remarks = "LATE";
                }

                await conn.ExecuteAsync(@"
                    INSERT INTO attendance (user_id, position, pm_time_in, date, remarks)
                    VALUES (@UserId, @Position, @TimeIn, @Date, @Remarks)
                ", new { 
                    UserId = userId, 
                    Position = user.Position, 
                    TimeIn = currentTime, 
                    Date = today,
                    Remarks = remarks
                });
            }
        }
        else
        {
            // Existing record - determine which field to update
            if (isAM)
            {
                if (attendance.AmTimeIn == null)
                {
                    attendanceType = "AM Time In";
                    if (currentTime > new TimeSpan(8, 0, 0)) remarks = "LATE";
                    
                    await conn.ExecuteAsync(
                        "UPDATE attendance SET am_time_in = @Time, remarks = @Remarks WHERE id = @Id",
                        new { Time = currentTime, Remarks = remarks, Id = attendance.Id }
                    );
                }
                else if (attendance.AmTimeOut == null)
                {
                    attendanceType = "AM Time Out";
                    if (currentTime < new TimeSpan(12, 0, 0)) remarks = "Undertime";
                    
                    await conn.ExecuteAsync(
                        "UPDATE attendance SET am_time_out = @Time WHERE id = @Id",
                        new { Time = currentTime, Id = attendance.Id }
                    );
                }
                else
                {
                    return new TimeLogResponse 
                    { 
                        Success = false, 
                        Message = "AM attendance already completed" 
                    };
                }
            }
            else
            {
                if (attendance.PmTimeIn == null)
                {
                    attendanceType = "PM Time In";
                    if (currentTime > new TimeSpan(13, 0, 0)) remarks = "LATE";
                    
                    await conn.ExecuteAsync(
                        "UPDATE attendance SET pm_time_in = @Time, remarks = @Remarks WHERE id = @Id",
                        new { Time = currentTime, Remarks = remarks, Id = attendance.Id }
                    );
                }
                else if (attendance.PmTimeOut == null)
                {
                    attendanceType = "PM Time Out";
                    if (currentTime < new TimeSpan(17, 0, 0)) remarks = "Undertime";
                    else if (currentTime > new TimeSpan(18, 0, 0)) remarks = "Overtime";
                    
                    await conn.ExecuteAsync(
                        "UPDATE attendance SET pm_time_out = @Time WHERE id = @Id",
                        new { Time = currentTime, Id = attendance.Id }
                    );
                }
                else
                {
                    return new TimeLogResponse 
                    { 
                        Success = false, 
                        Message = "PM attendance already completed" 
                    };
                }
            }
        }

        return new TimeLogResponse
        {
            Success = true,
            Message = $"{attendanceType} recorded successfully",
            AttendanceType = attendanceType,
            Time = now.ToString("hh:mm:ss tt"),
            Remarks = remarks,
            User = new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Position = user.Position,
                Gender = user.Gender
            }
        };
    }

    /// <summary>
    /// Get today's attendance logs
    /// </summary>
    public async Task<List<AttendanceRecord>> GetTodayLogsAsync()
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var today = DateTime.Today;
        
        var records = await conn.QueryAsync<AttendanceRecord>(@"
            SELECT 
                a.user_id as UserId,
                u.username as Username,
                a.position as Position,
                DATE_FORMAT(a.date, '%Y-%m-%d') as Date,
                TIME_FORMAT(a.am_time_in, '%H:%i:%s') as AmTimeIn,
                TIME_FORMAT(a.am_time_out, '%H:%i:%s') as AmTimeOut,
                TIME_FORMAT(a.pm_time_in, '%H:%i:%s') as PmTimeIn,
                TIME_FORMAT(a.pm_time_out, '%H:%i:%s') as PmTimeOut,
                a.remarks as Remarks
            FROM attendance a
            JOIN users u ON a.user_id = u.id
            WHERE a.date = @Date
            ORDER BY a.created_at DESC
        ", new { Date = today });

        return records.ToList();
    }

    /// <summary>
    /// Get attendance for a specific user
    /// </summary>
    public async Task<List<AttendanceRecord>> GetUserAttendanceAsync(int userId, DateTime? startDate, DateTime? endDate)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var start = startDate ?? DateTime.Today.AddDays(-30);
        var end = endDate ?? DateTime.Today;

        var records = await conn.QueryAsync<AttendanceRecord>(@"
            SELECT 
                a.user_id as UserId,
                u.username as Username,
                a.position as Position,
                DATE_FORMAT(a.date, '%Y-%m-%d') as Date,
                TIME_FORMAT(a.am_time_in, '%H:%i:%s') as AmTimeIn,
                TIME_FORMAT(a.am_time_out, '%H:%i:%s') as AmTimeOut,
                TIME_FORMAT(a.pm_time_in, '%H:%i:%s') as PmTimeIn,
                TIME_FORMAT(a.pm_time_out, '%H:%i:%s') as PmTimeOut,
                a.remarks as Remarks
            FROM attendance a
            JOIN users u ON a.user_id = u.id
            WHERE a.user_id = @UserId AND a.date BETWEEN @Start AND @End
            ORDER BY a.date DESC
        ", new { UserId = userId, Start = start, End = end });

        return records.ToList();
    }

    /// <summary>
    /// Get all attendance records
    /// </summary>
    public async Task<List<AttendanceRecord>> GetAllAttendanceAsync(DateTime? startDate, DateTime? endDate)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var start = startDate ?? DateTime.Today.AddDays(-30);
        var end = endDate ?? DateTime.Today;

        var records = await conn.QueryAsync<AttendanceRecord>(@"
            SELECT 
                a.user_id as UserId,
                u.username as Username,
                a.position as Position,
                DATE_FORMAT(a.date, '%Y-%m-%d') as Date,
                TIME_FORMAT(a.am_time_in, '%H:%i:%s') as AmTimeIn,
                TIME_FORMAT(a.am_time_out, '%H:%i:%s') as AmTimeOut,
                TIME_FORMAT(a.pm_time_in, '%H:%i:%s') as PmTimeIn,
                TIME_FORMAT(a.pm_time_out, '%H:%i:%s') as PmTimeOut,
                a.remarks as Remarks
            FROM attendance a
            JOIN users u ON a.user_id = u.id
            WHERE a.date BETWEEN @Start AND @End
            ORDER BY a.date DESC, u.username ASC
        ", new { Start = start, End = end });

        return records.ToList();
    }

    /// <summary>
    /// Get user's today attendance
    /// </summary>
    public async Task<AttendanceRecord?> GetUserTodayAttendanceAsync(int userId)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        return await conn.QueryFirstOrDefaultAsync<AttendanceRecord>(@"
            SELECT 
                a.user_id as UserId,
                u.username as Username,
                a.position as Position,
                DATE_FORMAT(a.date, '%Y-%m-%d') as Date,
                TIME_FORMAT(a.am_time_in, '%H:%i:%s') as AmTimeIn,
                TIME_FORMAT(a.am_time_out, '%H:%i:%s') as AmTimeOut,
                TIME_FORMAT(a.pm_time_in, '%H:%i:%s') as PmTimeIn,
                TIME_FORMAT(a.pm_time_out, '%H:%i:%s') as PmTimeOut,
                a.remarks as Remarks
            FROM attendance a
            JOIN users u ON a.user_id = u.id
            WHERE a.user_id = @UserId AND a.date = @Date
        ", new { UserId = userId, Date = DateTime.Today });
    }

    /// <summary>
    /// Get dashboard summary for today
    /// </summary>
    public async Task<DashboardSummary> GetDashboardSummaryAsync()
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var today = DateTime.Today;

        // Get total officials for today
        var totalOfficials = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(DISTINCT user_id)
            FROM attendance
            WHERE LOWER(TRIM(position)) IN ('official', 'offical')
            AND date = @Date
        ", new { Date = today });

        // Get total staff for today
        var totalStaff = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(DISTINCT user_id)
            FROM attendance
            WHERE LOWER(TRIM(position)) = 'staff'
            AND date = @Date
        ", new { Date = today });

        // Get remarks summary
        var remarksSummary = await conn.QueryFirstOrDefaultAsync<RemarksSummary>(@"
            SELECT 
                SUM(CASE WHEN LOWER(TRIM(remarks)) IN ('ontime', 'on time') THEN 1 ELSE 0 END) AS Ontime,
                SUM(CASE WHEN LOWER(TRIM(remarks)) = 'late' THEN 1 ELSE 0 END) AS Late,
                SUM(CASE WHEN LOWER(TRIM(remarks)) = 'undertime' THEN 1 ELSE 0 END) AS Undertime,
                SUM(CASE WHEN LOWER(TRIM(remarks)) = 'overtime' THEN 1 ELSE 0 END) AS Overtime
            FROM attendance
            WHERE date = @Date
        ", new { Date = today });

        // Get enrolled users count
        var enrolledUsers = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM users WHERE fingerprint_template IS NOT NULL"
        );

        return new DashboardSummary
        {
            Success = true,
            Date = today.ToString("yyyy-MM-dd"),
            TotalOfficials = totalOfficials,
            TotalStaff = totalStaff,
            Ontime = remarksSummary?.Ontime ?? 0,
            Late = remarksSummary?.Late ?? 0,
            Undertime = remarksSummary?.Undertime ?? 0,
            Overtime = remarksSummary?.Overtime ?? 0,
            EnrolledUsers = enrolledUsers
        };
    }

    /// <summary>
    /// Get monthly report summary with FP-Growth patterns and trend chart data
    /// </summary>
    public async Task<ReportSummary> GetReportSummaryAsync(string month, string? position, string? searchName)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var startDate = $"{month}-01";
        var endDate = $"{month}-31";

        // Build query with filters
        var query = @"
            SELECT 
                u.username AS Employee,
                u.position AS Position,
                DATE(a.date) AS Day,
                CASE 
                    WHEN LOWER(a.remarks) LIKE '%ontime%' THEN 'Ontime'
                    WHEN LOWER(a.remarks) LIKE '%late%' THEN 'Late'
                    WHEN LOWER(a.remarks) LIKE '%undertime%' THEN 'Undertime'
                    WHEN LOWER(a.remarks) LIKE '%overtime%' THEN 'Overtime'
                    ELSE 'Unknown'
                END AS Remarks
            FROM attendance a
            LEFT JOIN users u ON a.user_id = u.id
            WHERE DATE(a.date) BETWEEN @StartDate AND @EndDate";

        var parameters = new DynamicParameters();
        parameters.Add("StartDate", startDate);
        parameters.Add("EndDate", endDate);

        if (!string.IsNullOrEmpty(position))
        {
            query += " AND LOWER(TRIM(u.position)) = LOWER(TRIM(@Position))";
            parameters.Add("Position", position);
        }

        if (!string.IsNullOrEmpty(searchName))
        {
            query += " AND LOWER(u.username) LIKE LOWER(@SearchName)";
            parameters.Add("SearchName", $"%{searchName}%");
        }

        query += " ORDER BY Day ASC, Employee ASC";

        var rows = (await conn.QueryAsync<DailyAttendance>(query, parameters)).ToList();

        // Parse month to get the number of days
        var monthParts = month.Split('-');
        var year = int.Parse(monthParts[0]);
        var monthNum = int.Parse(monthParts[1]);
        var daysInMonth = DateTime.DaysInMonth(year, monthNum);

        // Group by day for chart data
        var grouped = rows
            .GroupBy(r => r.Day.ToString("yyyy-MM-dd"))
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    Ontime = g.Count(r => r.Remarks == "Ontime"),
                    Late = g.Count(r => r.Remarks == "Late"),
                    Undertime = g.Count(r => r.Remarks == "Undertime"),
                    Overtime = g.Count(r => r.Remarks == "Overtime")
                }
            );

        // Create full month range (day 1 to last day)
        var labels = new List<string>();
        var ontimeData = new List<int>();
        var lateData = new List<int>();
        var undertimeData = new List<int>();
        var overtimeData = new List<int>();

        for (int day = 1; day <= daysInMonth; day++)
        {
            labels.Add(day.ToString());
            var dateKey = $"{year:0000}-{monthNum:00}-{day:00}";
            
            if (grouped.TryGetValue(dateKey, out var dayData))
            {
                ontimeData.Add(dayData.Ontime);
                lateData.Add(dayData.Late);
                undertimeData.Add(dayData.Undertime);
                overtimeData.Add(dayData.Overtime);
            }
            else
            {
                // No data for this day, fill with zeros
                ontimeData.Add(0);
                lateData.Add(0);
                undertimeData.Add(0);
                overtimeData.Add(0);
            }
        }

        var chartData = new ChartData
        {
            Labels = labels,
            Ontime = ontimeData,
            Late = lateData,
            Undertime = undertimeData,
            Overtime = overtimeData
        };

        // FP-Growth patterns (simplified version - count most frequent remark patterns)
        var patterns = rows
            .GroupBy(r => r.Remarks)
            .Where(g => g.Key != "Unknown")
            .Select(g => new PatternItem
            {
                Pattern = g.Key,
                Support = g.Count()
            })
            .OrderByDescending(p => p.Support)
            .Take(10)
            .ToList();

        return new ReportSummary
        {
            Patterns = patterns,
            Chart = chartData
        };
    }

    /// <summary>
    /// Get trend data for daily attendance (ontime, late, undertime, overtime) by month
    /// </summary>
    public async Task<TrendData> GetTrendAsync(string? employee, string? month)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        // Default to current month if not provided
        if (string.IsNullOrEmpty(month))
        {
            var now = DateTime.Now;
            month = $"{now.Year}-{now.Month:00}";
        }

        var startDate = $"{month}-01";
        var endDate = $"{month}-31";

        // Build query
        var query = @"
            SELECT 
                DATE_FORMAT(DATE(a.date), '%Y-%m-%d') AS Date,
                SUM(CASE WHEN LOWER(a.remarks) LIKE '%ontime%' THEN 1 ELSE 0 END) AS Ontime,
                SUM(CASE WHEN LOWER(a.remarks) LIKE '%late%' THEN 1 ELSE 0 END) AS Late,
                SUM(CASE WHEN LOWER(a.remarks) LIKE '%undertime%' THEN 1 ELSE 0 END) AS Undertime,
                SUM(CASE WHEN LOWER(a.remarks) LIKE '%overtime%' THEN 1 ELSE 0 END) AS Overtime
            FROM attendance a
            LEFT JOIN users u ON u.id = a.user_id
            WHERE DATE(a.date) BETWEEN @StartDate AND @EndDate";

        var parameters = new DynamicParameters();
        parameters.Add("StartDate", startDate);
        parameters.Add("EndDate", endDate);

        if (!string.IsNullOrEmpty(employee))
        {
            query += " AND LOWER(u.username) = LOWER(@Employee)";
            parameters.Add("Employee", employee);
        }

        query += @" GROUP BY DATE(a.date)
                   ORDER BY DATE(a.date)";

        var rows = (await conn.QueryAsync<TrendDataRow>(query, parameters)).ToList();

        Console.WriteLine($"📊 Trend Query - Month: {month}, Employee: {employee ?? "All"}, Rows: {rows.Count}");
        if (rows.Any())
        {
            Console.WriteLine($"   First row: {rows[0].Date} - Ontime:{rows[0].Ontime}, Late:{rows[0].Late}");
        }

        return new TrendData
        {
            Ok = true,
            Data = rows
        };
    }
}

// Helper class for remarks summary
public class RemarksSummary
{
    public int Ontime { get; set; }
    public int Late { get; set; }
    public int Undertime { get; set; }
    public int Overtime { get; set; }
}

// Helper class for daily attendance query
public class DailyAttendance
{
    public string Employee { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public DateTime Day { get; set; }
    public string Remarks { get; set; } = string.Empty;
}
