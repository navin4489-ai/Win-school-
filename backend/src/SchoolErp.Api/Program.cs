using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

    // A deny-all CSP protects API answers that a browser could be tricked into
    // rendering. It is decided in OnStarting because only then is the content type
    // known: documents the portal opens directly in a tab (PDF receipts, HTML
    // certificates, images) must not be locked down or they stop rendering.
    context.Response.OnStarting(() =>
    {
        var contentType = context.Response.ContentType ?? string.Empty;
        var rendersInBrowser =
            contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) ||
            contentType.StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase) ||
            contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        if (!rendersInBrowser)
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        return Task.CompletedTask;
    });

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
var uploadsPath = Path.GetFullPath(Path.IsPathRooted(uploadRoot)
    ? uploadRoot
    : Path.Combine(builder.Environment.ContentRootPath, uploadRoot));
Directory.CreateDirectory(uploadsPath);
var fileContentTypes = new FileExtensionContentTypeProvider();

app.MapGet("/files/db/{name}", async (string name, AppDbContext db, HttpContext http,
    IOptions<SchoolErp.Application.Common.FileSigningOptions> signingOptions, CancellationToken ct) =>
{
    if (!SchoolErp.Infrastructure.Services.DatabaseFileStorage.TryParseId(name, out var id)) return Results.NotFound();

    // Access control: a request is allowed when ANY holds —
    //   (a) a valid signature for this exact path (constant-time), or
    //   (b) a valid bearer token (authentication middleware has run globally), or
    //   (c) the grace window Files:SignedFromUtc has not yet elapsed, so links already
    //       cached by browsers/apps before signing was switched on keep working.
    // After the grace instant (or when it is absent), unsigned & unauthenticated -> 403.
    var requestPath = http.Request.Path.Value ?? string.Empty;
    var sig = http.Request.Query["sig"].ToString();
    if (!SchoolErp.Application.Common.FileAccessPolicy.IsAllowed(requestPath, sig,
            http.User?.Identity?.IsAuthenticated == true, signingOptions.Value.SignedFromUtc))
        return Results.Json(
            new { detail = "This file link is missing a valid signature. Reload the page to get a fresh link." },
            statusCode: StatusCodes.Status403Forbidden);

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

// Local-disk uploads. There is deliberately no UseStaticFiles: every /files/* request
// passes the gate above. The literal /files/db/{name} route has higher precedence than
// this catch-all, so DB blobs are still served by the endpoint above.
app.MapGet("/files/{**path}", (string path, HttpContext http,
    IOptions<SchoolErp.Application.Common.FileSigningOptions> signingOptions) =>
{
    var requestPath = http.Request.Path.Value ?? string.Empty;
    var sig = http.Request.Query["sig"].ToString();
    if (!SchoolErp.Application.Common.FileAccessPolicy.IsAllowed(requestPath, sig,
            http.User?.Identity?.IsAuthenticated == true, signingOptions.Value.SignedFromUtc))
        return Results.Json(
            new { detail = "This file link is missing a valid signature. Reload the page to get a fresh link." },
            statusCode: StatusCodes.Status403Forbidden);

    // Canonical full path must stay under the uploads root; no traversal or absolute path
    // ever reaches the disk, even for an authenticated caller or a valid signature.
    if (!SchoolErp.Infrastructure.Services.LocalFileStorage.TryResolveFullPath(uploadsPath, path, out var fullPath) ||
        !File.Exists(fullPath))
        return Results.NotFound();

    http.Response.Headers.CacheControl = "private, max-age=86400";
    if (!fileContentTypes.TryGetContentType(fullPath, out var contentType))
        contentType = "application/octet-stream";
    var inline = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                 contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
    return inline
        ? Results.File(fullPath, contentType, enableRangeProcessing: true)
        : Results.File(fullPath, contentType, Path.GetFileName(fullPath), enableRangeProcessing: true);
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

{
    // Every environment: create the schema if missing and the first super admin from
    // configuration (Bootstrap__*). No sample data is ever created.
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
