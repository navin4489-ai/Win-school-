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
        options.JsonSerializerOptions.Converters.Add(new SchoolErp.Api.UtcDateTimeConverter());
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
if (allowedOrigins.Length == 0 && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Cors:AllowedOrigins must list the web portal origin(s) outside Development.");
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
    // Sign-in attempts: tight per-IP limit against password guessing.
    options.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:LoginPerMinute", 10),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
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

// Baseline security headers on every API response.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    if (!app.Environment.IsDevelopment())
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    if (context.Request.Path.StartsWithSegments("/api"))
        headers["Cache-Control"] = "no-store";
    await next();
});

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

app.MapGet("/files/db/{name}", async (string name, AppDbContext db, HttpContext http, CancellationToken ct) =>
{
    if (!SchoolErp.Infrastructure.Services.DatabaseFileStorage.TryParseId(name, out var id)) return Results.NotFound();
    var blob = await db.StoredBlobs.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
    if (blob is null) return Results.NotFound();

    // Ids are unguessable; content never changes for a given id, so it can be cached.
    http.Response.Headers.CacheControl = "private, max-age=86400";
    var inline = blob.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                 blob.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
    return inline
        ? Results.File(blob.Content, blob.ContentType)
        : Results.File(blob.Content, blob.ContentType, blob.FileName);
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
            ALTER TABLE "Payment" ADD COLUMN IF NOT EXISTS "PenaltyPortion" numeric(18,2) NOT NULL DEFAULT 0;
            CREATE TABLE IF NOT EXISTS "StoredBlob" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "SchoolId" integer NULL,
                "FileName" varchar(256) NOT NULL,
                "ContentType" varchar(512) NOT NULL,
                "Content" bytea NOT NULL,
                "SizeBytes" bigint NOT NULL,
                "CreatedAtUtc" timestamp without time zone NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Payment_GatewayPaymentId_Unique"
                ON "Payment" ("GatewayPaymentId") WHERE "GatewayPaymentId" IS NOT NULL;
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
