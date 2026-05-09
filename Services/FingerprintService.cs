using AMS.API.Data;
using AMS.API.Models;
using Dapper;

namespace AMS.API.Services;

public interface IFingerprintService
{
    Task<FingerprintVerifyResponse> IdentifyUserAsync(string fingerprintTemplate);
    Task<bool> EnrollFingerprintAsync(FingerprintEnrollRequest request);
    Task<List<Fingerprint>> GetAllTemplatesAsync();
    Task<Fingerprint?> GetUserFingerprintAsync(int userId);
    Task<bool> DeleteFingerprintAsync(int userId);
    double CompareTemplates(string template1, string template2);
}

public class FingerprintService : IFingerprintService
{
    private readonly DatabaseContext _db;
    private readonly IScannerService? _scannerService;
    private readonly ILogger<FingerprintService> _logger;
    private readonly double _matchThreshold;

    public FingerprintService(DatabaseContext db, ILogger<FingerprintService> logger, IScannerService? scannerService = null)
    {
        _db = db;
        _logger = logger;
        _scannerService = scannerService;
        _matchThreshold = double.Parse(Environment.GetEnvironmentVariable("MATCH_THRESHOLD") ?? "50");
    }

    /// <summary>
    /// Identify user by fingerprint (1:N matching)
    /// </summary>
    public async Task<FingerprintVerifyResponse> IdentifyUserAsync(string fingerprintTemplate)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        // Get all fingerprint templates
        var fingerprints = await conn.QueryAsync<Fingerprint>(@"
            SELECT f.id, f.user_id as UserId, f.fingerprint_template as FingerprintTemplate, 
                   f.finger_index as FingerIndex, f.quality, f.capture_count as CaptureCount,
                   f.registered_at as RegisteredAt, u.username as Username 
            FROM fingerprints f 
            JOIN users u ON f.user_id = u.id
        ");

        // Also check users table for fingerprints
        var usersWithFingerprints = await conn.QueryAsync<User>(@"
            SELECT * FROM users WHERE fingerprint_template IS NOT NULL AND fingerprint_template != ''
        ");

        var fingerprintList = fingerprints.ToList();
        var usersList = usersWithFingerprints.ToList();
        
        _logger.LogInformation("🔍 Starting fingerprint identification...");
        _logger.LogInformation("   Found {FingerprintCount} fingerprints in fingerprints table", fingerprintList.Count);
        _logger.LogInformation("   Found {UserCount} users with fingerprints in users table", usersList.Count);
        _logger.LogInformation("   Match threshold: {Threshold}", _matchThreshold);

        double bestScore = 0;
        int? matchedUserId = null;
        string? matchedUsername = null;
        string? matchedPosition = null;

        // Check fingerprints table
        foreach (var fp in fingerprintList)
        {
            var score = CompareTemplates(fingerprintTemplate, fp.FingerprintTemplate);
            _logger.LogInformation("Comparing with User {UserId} ({Username}): Score = {Score}", fp.UserId, fp.Username, score);
            if (score > bestScore)
            {
                bestScore = score;
                matchedUserId = fp.UserId;
                matchedUsername = fp.Username;
            }
        }

        // Check users table
        foreach (var user in usersList)
        {
            if (string.IsNullOrEmpty(user.FingerprintTemplate)) continue;
            
            var score = CompareTemplates(fingerprintTemplate, user.FingerprintTemplate);
            _logger.LogInformation("Comparing with User {UserId} ({Username}) from users table: Score = {Score}", user.Id, user.Username, score);
            if (score > bestScore)
            {
                bestScore = score;
                matchedUserId = user.Id;
                matchedUsername = user.Username;
                matchedPosition = user.Position;
            }
        }

        // Get user position if matched
        if (matchedUserId != null && string.IsNullOrEmpty(matchedPosition))
        {
            var user = await conn.QueryFirstOrDefaultAsync<User>(
                "SELECT position FROM users WHERE id = @Id",
                new { Id = matchedUserId }
            );
            matchedPosition = user?.Position;
        }

        if (bestScore >= _matchThreshold && matchedUserId != null)
        {
            _logger.LogInformation("✅ MATCH FOUND! User {UserId} ({Username}) - Score: {Score} >= Threshold: {Threshold}", matchedUserId, matchedUsername, bestScore, _matchThreshold);
            return new FingerprintVerifyResponse
            {
                Success = true,
                Matched = true,
                UserId = matchedUserId,
                Username = matchedUsername,
                Position = matchedPosition,
                Score = bestScore,
                Message = "Fingerprint matched"
            };
        }

        _logger.LogWarning("❌ NO MATCH! Best score: {BestScore} < Threshold: {Threshold}", bestScore, _matchThreshold);
        return new FingerprintVerifyResponse
        {
            Success = true,
            Matched = false,
            Score = bestScore,
            Message = $"No matching fingerprint found (best score: {bestScore}, threshold: {_matchThreshold})"
        };
    }

    /// <summary>
    /// Enroll fingerprint for a user
    /// </summary>
    public async Task<bool> EnrollFingerprintAsync(FingerprintEnrollRequest request)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        _logger.LogInformation("📝 Enrolling fingerprint for User ID: {UserId}, Finger Index: {FingerIndex}", 
            request.UserId, request.FingerIndex);
        _logger.LogInformation("   Template length: {Length} characters", 
            request.FingerprintTemplate?.Length ?? 0);

        // Update users table
        await conn.ExecuteAsync(@"
            UPDATE users SET fingerprint_template = @Template WHERE id = @UserId
        ", new { Template = request.FingerprintTemplate, request.UserId });

        _logger.LogInformation("   ✓ Updated users table");

        // Check if fingerprint exists in fingerprints table
        var existing = await conn.QueryFirstOrDefaultAsync<Fingerprint>(
            "SELECT id FROM fingerprints WHERE user_id = @UserId AND finger_index = @FingerIndex",
            new { request.UserId, request.FingerIndex }
        );

        if (existing != null)
        {
            _logger.LogInformation("   Updating existing fingerprint record ID: {Id}", existing.Id);
            // Update existing
            await conn.ExecuteAsync(@"
                UPDATE fingerprints 
                SET fingerprint_template = @Template, quality = @Quality, capture_count = @CaptureCount 
                WHERE id = @Id
            ", new { Template = request.FingerprintTemplate, request.Quality, request.CaptureCount, existing.Id });
        }
        else
        {
            _logger.LogInformation("   Creating new fingerprint record");
            // Insert new
            await conn.ExecuteAsync(@"
                INSERT INTO fingerprints (user_id, fingerprint_template, finger_index, quality, capture_count)
                VALUES (@UserId, @Template, @FingerIndex, @Quality, @CaptureCount)
            ", new { request.UserId, Template = request.FingerprintTemplate, request.FingerIndex, request.Quality, request.CaptureCount });
        }

        _logger.LogInformation("   ✅ Fingerprint enrollment complete");
        return true;
    }

    /// <summary>
    /// Get all fingerprint templates (for 1:N matching)
    /// </summary>
    public async Task<List<Fingerprint>> GetAllTemplatesAsync()
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var fingerprints = new List<Fingerprint>();

        // Get from fingerprints table
        var fpTable = await conn.QueryAsync<Fingerprint>(@"
            SELECT f.*, u.username as Username 
            FROM fingerprints f 
            JOIN users u ON f.user_id = u.id
        ");
        fingerprints.AddRange(fpTable);
        _logger.LogInformation($"Found {fpTable.Count()} fingerprints in fingerprints table");

        // Also get from users table fingerprint_template column
        var usersWithFp = await conn.QueryAsync<(int id, string username, string template)>(@"
            SELECT id, username, fingerprint_template as template
            FROM users 
            WHERE fingerprint_template IS NOT NULL AND fingerprint_template != ''
        ");
        _logger.LogInformation($"Found {usersWithFp.Count()} users with fingerprint templates");

        foreach (var user in usersWithFp)
        {
            // Avoid duplicates
            if (!fingerprints.Any(f => f.UserId == user.id))
            {
                fingerprints.Add(new Fingerprint
                {
                    UserId = user.id,
                    Username = user.username,
                    FingerprintTemplate = user.template,
                    FingerIndex = 0,
                    RegisteredAt = DateTime.Now
                });
            }
        }

        _logger.LogInformation($"Total enrolled users count: {fingerprints.Count}");
        return fingerprints;
    }

    /// <summary>
    /// Get fingerprint for a specific user
    /// </summary>
    public async Task<Fingerprint?> GetUserFingerprintAsync(int userId)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        return await conn.QueryFirstOrDefaultAsync<Fingerprint>(
            "SELECT * FROM fingerprints WHERE user_id = @UserId",
            new { UserId = userId }
        );
    }

    /// <summary>
    /// Delete fingerprint for a user
    /// </summary>
    public async Task<bool> DeleteFingerprintAsync(int userId)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            "DELETE FROM fingerprints WHERE user_id = @UserId",
            new { UserId = userId }
        );

        await conn.ExecuteAsync(
            "UPDATE users SET fingerprint_template = NULL WHERE id = @UserId",
            new { UserId = userId }
        );

        return true;
    }

    /// <summary>
    /// Compare two fingerprint templates
    /// Uses ZKTeco SDK for matching when available, falls back to byte comparison
    /// </summary>
    public double CompareTemplates(string template1, string template2)
    {
        if (string.IsNullOrEmpty(template1) || string.IsNullOrEmpty(template2))
            return 0;

        // For ZK Live20R, templates are typically Base64-encoded
        // If templates are exactly the same
        if (template1 == template2)
        {
            _logger.LogDebug("Templates are identical - 100% match");
            return 100;
        }

        try
        {
            // Try to use SDK matching if available
            if (_scannerService != null)
            {
                _logger.LogInformation("   Captured template length: {Len1}, Stored template length: {Len2}", 
                    template1.Length, template2.Length);
                var score = _scannerService.MatchTemplates(template1, template2);
                _logger.LogInformation("🔍 SDK match score: {Score} (threshold: {Threshold})", score, _matchThreshold);
                return score;
            }
            else
            {
                _logger.LogWarning("   Scanner service is NULL - cannot use SDK matching");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SDK matching failed, falling back to byte comparison");
        }

        // Fallback: Simple byte comparison (for demo/non-Windows environments)
        try
        {
            // Decode Base64 templates
            var bytes1 = Convert.FromBase64String(template1);
            var bytes2 = Convert.FromBase64String(template2);

            // Simple byte comparison (for demo purposes)
            if (bytes1.Length != bytes2.Length)
            {
                _logger.LogDebug("Template lengths don't match: {Len1} vs {Len2}", bytes1.Length, bytes2.Length);
                return 0;
            }

            int matchingBytes = 0;
            for (int i = 0; i < bytes1.Length; i++)
            {
                if (bytes1[i] == bytes2[i])
                    matchingBytes++;
            }

            var similarity = (matchingBytes / (double)bytes1.Length) * 100;
            _logger.LogDebug("Byte comparison similarity: {Similarity}%", similarity);
            return similarity;
        }
        catch
        {
            // If Base64 decoding fails, use string similarity
            var similarity = CalculateStringSimilarity(template1, template2) * 100;
            _logger.LogDebug("String similarity: {Similarity}%", similarity);
            return similarity;
        }
    }

    private double CalculateStringSimilarity(string s1, string s2)
    {
        if (s1 == s2) return 1.0;
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;

        int maxLen = Math.Max(s1.Length, s2.Length);
        int minLen = Math.Min(s1.Length, s2.Length);
        int matches = 0;

        for (int i = 0; i < minLen; i++)
        {
            if (s1[i] == s2[i]) matches++;
        }

        return (double)matches / maxLen;
    }
}
