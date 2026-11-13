using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi.Models;
using SchoolErp.Api.Middleware;
using SchoolErp.Application;
using SchoolErp.Infrastructure;
using SchoolErp.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Wintech School ERP API",
        Version = "v1",
        Description = "Multi-tenant school management API. Authenticate with a bearer JWT from /api/auth/login."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token only; the Bearer prefix is added automatically."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length == 0) policy.AllowAnyOrigin();
        else policy.WithOrigins(allowedOrigins);

        policy.AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("Content-Disposition");
    });
});

// Requests per minute per client IP. A school office usually sits behind one public IP,
// so this must cover a whole staff room, not a single person.
var permitsPerMinute = builder.Configuration.GetValue("RateLimiting:PermitsPerMinute", 1200);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

// Behind Render's proxy/CDN the socket address is the proxy's. Take the client address
// from X-Forwarded-For so rate limiting and audit logs see real users. Render strips
// client-supplied values at its edge, so the forwarded chain can be trusted there.
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 2
};
forwarded.KnownNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);

app.UseMiddleware<ExceptionHandlingMiddleware>();
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "School ERP API v1");
        options.DocumentTitle = "Wintech School ERP API";
    });
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

var uploadRoot = builder.Configuration["Storage:RootPath"] ?? "App_Data/uploads";
var uploadsPath = Path.IsPathRooted(uploadRoot)
    ? uploadRoot
    : Path.Combine(builder.Environment.ContentRootPath, uploadRoot);
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/files"
});

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "SchoolErp.Api",
    utc = DateTime.UtcNow
}));

app.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable));

app.MapControllers();

if (app.Environment.IsDevelopment() && builder.Configuration.GetValue("Seed:Enabled", true))
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<DbInitializer>().InitializeAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex,
            "Database initialisation failed. Check ConnectionStrings:Default and that the database is reachable.");
        throw;
    }
}
else if (!app.Environment.IsDevelopment())
{
    // Production: create the schema if missing and the first super admin from secrets.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
    {
        // EnsureCreated does not alter an existing schema; add columns introduced later.
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "School" ADD COLUMN IF NOT EXISTS "PlatformFeeType" varchar(256) NULL;
            ALTER TABLE "School" ADD COLUMN IF NOT EXISTS "PlatformFeeAmount" numeric(18,2) NOT NULL DEFAULT 0;
            ALTER TABLE "School" ADD COLUMN IF NOT EXISTS "PlatformFeeNotes" varchar(256) NULL;
            ALTER TABLE "StudentFeeAssignment" ADD COLUMN IF NOT EXISTS "PlatformFeeAmount" numeric(18,2) NOT NULL DEFAULT 0;
            """);
    }
    var cfg = builder.Configuration;
    await scope.ServiceProvider.GetRequiredService<DbInitializer>().BootstrapPlatformAsync(
        cfg["Bootstrap:SchoolCode"] ?? "WINTECH",
        cfg["Bootstrap:SchoolName"] ?? "Wintech Innovations",
        cfg["Bootstrap:AdminEmail"] ?? string.Empty,
        cfg["Bootstrap:AdminPassword"] ?? string.Empty);
}

app.Run();

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program
{
}
