using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using SchoolErp.Application.Common;
using SchoolErp.Infrastructure.Identity;
using SchoolErp.Infrastructure.Persistence;
using SchoolErp.Infrastructure.Services;

namespace SchoolErp.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:Default is not configured. Set it in appsettings or an environment variable.");

        services.AddHttpContextAccessor();

        var provider = configuration["Database:Provider"] ?? "SqlServer";
        var useInMemory = string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase);
        var usePostgres = string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase);

        // DateTimes are stored as UTC wall-clock values, as on SQL Server, rather than
        // forcing every DateTime in the code base to carry DateTimeKind.Utc.
        if (usePostgres) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        services.AddDbContext<AppDbContext>(options =>
        {
            if (useInMemory)
            {
                // Local testing only: data is lost on restart.
                options.UseInMemoryDatabase("SchoolErpLocal");
                return;
            }

            if (usePostgres)
            {
                options.UseNpgsql(ToNpgsqlConnectionString(connectionString), pg =>
                {
                    pg.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
                    pg.CommandTimeout(60);
                });
                return;
            }

            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
                sql.CommandTimeout(60);
            });
        });

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ICurrentUser, CurrentUserService>();
        services.AddSingleton<IDateTimeProvider, SystemClock>();
        services.AddSingleton<IPasswordHasher, PasswordHasherService>();
        services.AddScoped<ITokenService, TokenService>();
        // Uploads: database by default (survives redeploys); "Local" keeps them on disk.
        if (string.Equals(configuration["Storage:Provider"], "Local", StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IFileStorage, LocalFileStorage>();
        else
            services.AddScoped<IFileStorage, DatabaseFileStorage>();
        services.AddScoped<INotificationSender, LogNotificationSender>();

        // Online payments: Razorpay when Payments:Provider = "Razorpay", otherwise disabled
        // (pay at the school office). Nothing is marked paid without a verified gateway payment.
        var isProduction = string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Production", StringComparison.OrdinalIgnoreCase);
        services.Configure<RazorpayOptions>(configuration.GetSection(RazorpayOptions.SectionName));
        if (string.Equals(configuration["Payments:Provider"], "Razorpay", StringComparison.OrdinalIgnoreCase))
        {
            var razorpay = configuration.GetSection(RazorpayOptions.SectionName).Get<RazorpayOptions>() ?? new RazorpayOptions();
            if (!razorpay.HasKeys)
                throw new InvalidOperationException(
                    "Payments:Provider is 'Razorpay' but Payments:Razorpay:KeyId and Payments:Razorpay:KeySecret are not both set. " +
                    "Set Payments__Razorpay__KeyId and Payments__Razorpay__KeySecret, or remove Payments__Provider.");
            services.AddHttpClient<RazorpayPaymentGateway>(client =>
            {
                client.BaseAddress = new Uri(RazorpayPaymentGateway.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
            });
            services.AddScoped<IPaymentGateway>(sp => sp.GetRequiredService<RazorpayPaymentGateway>());
        }
        else
        {
            services.AddScoped<IPaymentGateway, DisabledPaymentGateway>();
        }
        services.AddScoped<DbInitializer>();

        // Live notification fan-out is process local; a multi-node deployment must
        // replace this with Redis pub/sub or Azure SignalR Service.
        services.AddSingleton<NotificationHub>();
        services.AddSingleton<INotificationPublisher>(sp => sp.GetRequiredService<NotificationHub>());

        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<FeePenaltyOptions>(configuration.GetSection(FeePenaltyOptions.SectionName));

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "Jwt:Issuer is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Audience is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey) && o.SigningKey.Length >= 32,
                "Jwt:SigningKey must be at least 32 characters.")
            .Validate(o => !isProduction || !(o.SigningKey ?? "").StartsWith("dev-only", StringComparison.OrdinalIgnoreCase)
                                          && !(o.SigningKey ?? "").StartsWith("local-compose", StringComparison.OrdinalIgnoreCase),
                "Jwt:SigningKey is a development key; set a secret key in Production.")
            .ValidateOnStart();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var key = string.IsNullOrWhiteSpace(jwt.SigningKey)
            ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('x', 32)))
            : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds < 0 ? 30 : jwt.ClockSkewSeconds),
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
                // A token issued before a password reset, deactivation or role change is
                // rejected immediately instead of living until it expires.
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var principal = context.Principal;
                        if (!int.TryParse(principal?.FindFirst("sub")?.Value, out var userId) ||
                            !int.TryParse(principal?.FindFirst("token_version")?.Value, out var version))
                        {
                            context.Fail("Invalid token.");
                            return;
                        }

                        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                        var current = await db.UserAccounts.IgnoreQueryFilters()
                            .Where(u => u.Id == userId && !u.IsDeleted && u.IsActive)
                            .Select(u => (int?)u.TokenVersion)
                            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);
                        if (current is null || current.Value != version) context.Fail("Session revoked.");
                    }
                };
            });

        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // Singletons so the scheduler endpoint can trigger the same instances on demand.
        services.AddSingleton<NotificationDispatcher>();
        services.AddSingleton<FeePenaltyService>();
        services.AddHostedService(sp => sp.GetRequiredService<NotificationDispatcher>());
        services.AddHostedService(sp => sp.GetRequiredService<FeePenaltyService>());

        return services;
    }

    /// <summary>
    /// Render and most hosts hand out <c>postgres://user:pass@host:port/db</c> URLs; Npgsql
    /// wants key/value pairs. Key/value strings are passed through unchanged.
    /// </summary>
    internal static string ToNpgsqlConnectionString(string value)
    {
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return value;

        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            // Render's internal network is private; external URLs need TLS.
            SslMode = uri.Host.Contains('.') ? Npgsql.SslMode.Require : Npgsql.SslMode.Prefer
        };
        return builder.ConnectionString;
    }
}
