using AMS.API.Data;
using AMS.API.Services;
using AMS.API.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Runtime.InteropServices;

// Load .env file from parent directory
DotNetEnv.Env.Load(Path.Combine(Directory.GetCurrentDirectory(), "../../.env"));

// ============================================
// Configure DLL Search Path for ZKFinger SDK
// ============================================
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    var dllPath = Path.Combine(AppContext.BaseDirectory, "dlls");
    if (Directory.Exists(dllPath))
    {
        // Add DLL directory to search path
        SetDllDirectory(dllPath);
        Console.WriteLine($"✅ DLL search path set to: {dllPath}");
    }
    else
    {
        Console.WriteLine($"⚠️  DLL directory not found: {dllPath}");
    }
}

[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool SetDllDirectory(string lpPathName);

// ============================================
// Helper function to initialize default user
// ============================================
async Task InitializeDefaultUserAsync(DatabaseContext db)
{
    try
    {
        using var conn = db.CreateConnection();
        await conn.OpenAsync();
        
        // Check if users table has any records
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM users";
        var count = (long?)await countCmd.ExecuteScalarAsync() ?? 0;
        
        if (count == 0)
        {
            // Hash the password
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword("default0");
            
            // Insert default admin user
            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = "INSERT INTO users (username, password, position, gender) VALUES (@username, @password, @position, @gender)";
            insertCmd.Parameters.AddWithValue("@username", "Admin");
            insertCmd.Parameters.AddWithValue("@password", hashedPassword);
            insertCmd.Parameters.AddWithValue("@position", "admin");
            insertCmd.Parameters.AddWithValue("@gender", "Male");
            await insertCmd.ExecuteNonQueryAsync();
            
            Console.WriteLine("✅ Default admin user created:");
            Console.WriteLine("   Username: Admin");
            Console.WriteLine("   Password: default0");
            Console.WriteLine("   Position: admin");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️  Could not initialize default user: {ex.Message}");
    }
}

var builder = WebApplication.CreateBuilder(args);

// ============================================
// Configure Services
// ============================================

// Database
builder.Services.AddSingleton<DatabaseContext>();

// Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();

// Scanner Service - Singleton because it manages hardware connection state
builder.Services.AddSingleton<IScannerService, ScannerService>();

// Fingerprint Service - uses Scanner Service for SDK matching
builder.Services.AddScoped<IFingerprintService, FingerprintService>();
builder.Services.AddScoped<IUserService, UserService>();

// JWT Authentication
var jwtKey = Environment.GetEnvironmentVariable("JWT_SECRET") ?? "AMS_SuperSecretKey_2024_Live20R_Fingerprint";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "AMS.API",
            ValidAudience = "AMS.Client",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

// CORS - Allow frontend with credentials
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.WithOrigins("http://localhost:8000", "http://127.0.0.1:8000", "http://localhost", "http://127.0.0.1")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Controllers
builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "AMS API - ZK Live20R", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ============================================
// Configure Pipeline
// ============================================

// Initialize Database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
    await db.InitializeDatabaseAsync();
    
    // Initialize default admin user if no users exist
    await InitializeDefaultUserAsync(db);
}

// Auto-initialize scanner on startup (non-blocking, after DB init)
_ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(1000); // Give API time to start
        using var scope = app.Services.CreateScope();
        var scannerService = scope.ServiceProvider.GetRequiredService<IScannerService>();
        var initResult = await scannerService.InitializeAsync();
        if (initResult.Success)
        {
            Console.WriteLine($"✅ Scanner initialized: {initResult.Message}");
            
            // Auto-connect to first device if available
            if (initResult.DeviceCount > 0)
            {
                var connectResult = await scannerService.OpenDeviceAsync(0);
                if (connectResult.Success)
                {
                    Console.WriteLine($"✅ Scanner device connected: {connectResult.Message}");
                }
                else
                {
                    Console.WriteLine($"⚠️  Scanner device connection failed: {connectResult.Message}");
                }
            }
        }
        else
        {
            Console.WriteLine($"⚠️  Scanner initialization failed: {initResult.Message}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️  Scanner auto-initialization error: {ex.Message}");
    }
});

// Swagger (Development)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "AMS API v1"));
}

// CORS
app.UseCors("AllowAll");

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// Map Controllers
app.MapControllers();

// Get port from environment
var port = Environment.GetEnvironmentVariable("API_PORT") ?? "5003";
app.Urls.Add($"http://0.0.0.0:{port}");

Console.WriteLine($"🚀 AMS API (.NET Core 9) running on port {port}");
Console.WriteLine($"📡 Swagger UI: http://localhost:{port}/swagger");
Console.WriteLine($"🔐 ZK Live20R Fingerprint Integration Ready");

app.Run();
