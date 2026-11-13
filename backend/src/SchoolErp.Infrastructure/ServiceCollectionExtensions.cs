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
                // Demo/local only: data is lost on restart.
                options.UseInMemoryDatabase("SchoolErpDemo");
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
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<INotificationSender, LogNotificationSender>();
        services.AddScoped<IPaymentGateway, DummyPaymentGateway>();
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
