using System.Reflection;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NSA.Application.Abstractions;
using NSA.Application.Configuration;
using NSA.Infrastructure.Authentication;
using NSA.Infrastructure.Configuration;
using NSA.Infrastructure.Health;
using NSA.Infrastructure.Observability;
using NSA.Persistence;
using NSA.Persistence.Concrete;
using NSA.Presentation.ExceptionHandling;
using NSA.Presentation.OpenApi;
using NSA.Service;

var migrateOnly = args.Any(argument => string.Equals(argument, "--migrate", StringComparison.OrdinalIgnoreCase));
var hostArguments = args.Where(argument => !string.Equals(argument, "--migrate", StringComparison.OrdinalIgnoreCase)).ToArray();
var builder = WebApplication.CreateBuilder(hostArguments);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);

if (migrateOnly)
{
    var connectionString = builder.Configuration.GetConnectionString("NotificationDb");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "ConnectionStrings:NotificationDb is required for one-shot database migration.");
    }

    var migrationOptions = new DbContextOptionsBuilder<NotificationDbContext>()
        .UseSqlServer(connectionString)
        .Options;
    await using var migrationContext = new NotificationDbContext(migrationOptions);
    var pendingMigrations = (await migrationContext.Database.GetPendingMigrationsAsync()).ToArray();
    if (pendingMigrations.Length == 0)
    {
        Console.WriteLine("Database schema is current; no migrations were applied.");
        return;
    }

    Console.WriteLine($"Applying {pendingMigrations.Length} additive migration(s).");
    await migrationContext.Database.MigrateAsync();
    Console.WriteLine("Database migrations completed successfully.");
    return;
}
const string FrontendCorsPolicy = "frontend";
const string AuthenticationRateLimitPolicy = "authentication";
var releaseColor = builder.Configuration["Release:Color"] ?? "local";
var releaseSha = builder.Configuration["Release:Sha"] ?? "development";
var frontendOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins")
    .Get<string[]>()?
    .Where(origin => Uri.TryCreate(origin, UriKind.Absolute, out _))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray() ?? [];
var jwtConfiguration = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>();
if (jwtConfiguration is null
    || string.IsNullOrWhiteSpace(jwtConfiguration.SigningKey)
    || Encoding.UTF8.GetByteCount(jwtConfiguration.SigningKey) < 32)
{
    throw new InvalidOperationException(
        "Authentication:Jwt:SigningKey must be supplied by a secret configuration provider and contain at least 32 UTF-8 bytes.");
}

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions.TryAdd("traceId", Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
    };
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddControllers();
if (frontendOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy => policy
        .WithOrigins(frontendOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));
}
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthenticationRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfiguration.SigningKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtConfiguration.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtConfiguration.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    });
builder.Services.AddAuthorization();
if (builder.Configuration.GetValue("ReverseProxy:TrustForwardedHeaders", false))
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(2, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });
builder.Services.AddEndpointsApiExplorer();
var healthChecks = builder.Services.AddHealthChecks();
if (builder.Configuration.GetValue("HealthChecks:DependencyChecksEnabled", true))
{
    healthChecks
        .AddCheck<SqlServerReadinessHealthCheck>(
            "sql-server",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["ready"],
            timeout: TimeSpan.FromSeconds(3));
}
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT authorization header using the Bearer scheme. Example: `Bearer {token}`",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Notification Service API",
        Version = "v1",
        Description = "Order tracking and notification API for products, carts, orders, and notification CRUD operations."
    });
    options.SwaggerDoc("v2", new OpenApiInfo
    {
        Title = "Notification Service API",
        Version = "v2",
        Description = "Current version of the order tracking and notification API."
    });

    foreach (var xmlFile in new[] { "NSA.xml" })
    {
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
        }
    }
    options.SchemaFilter<ProblemDetailsSchemaFilter>();
    options.DocInclusionPredicate((documentName, apiDescription) =>
    {
        if (!string.Equals(apiDescription.GroupName, documentName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var isLegacyUnversionedRoute = !(apiDescription.RelativePath?.StartsWith("api/v", StringComparison.OrdinalIgnoreCase) ?? false);
        return !isLegacyUnversionedRoute || string.Equals(documentName, "v2", StringComparison.OrdinalIgnoreCase);
    });
    options.OperationFilter<ProblemDetailsOperationFilter>();
    options.OperationFilter<BearerSecurityOperationFilter>();
});

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("NotificationDb")));

builder.Services.AddScoped<ICartRepository, CartRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IBulkNotificationJobRepository, BulkNotificationJobRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddOptions<BulkNotificationOptions>()
    .Bind(builder.Configuration.GetSection(BulkNotificationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<NotificationRecipientOptions>()
    .Bind(builder.Configuration.GetSection(NotificationRecipientOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<AuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(AuthenticationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<DurabilityOptions>()
    .Bind(builder.Configuration.GetSection(DurabilityOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
        "Authentication:Jwt:SigningKey must contain at least 32 UTF-8 bytes.")
    .ValidateOnStart();
builder.Services.AddSingleton(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<BulkNotificationOptions>>().Value;
    return new BulkNotificationSettings(
        options.MaxTrackedJobs,
        options.MaxBatchSize,
        options.CompletedJobRetentionMinutes);
});
builder.Services.AddSingleton(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<NotificationRecipientOptions>>().Value;
    return new NotificationRecipientSettings(options.DefaultVisitorEmail);
});
builder.Services.AddSingleton(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
    return new AuthenticationSettings(options.MaxFailedAccessAttempts, TimeSpan.FromMinutes(options.LockoutMinutes));
});
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<IOptions<DurabilityOptions>>().Value);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
builder.Services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
builder.Services.AddSingleton<ICorrelationIdProvider, ActivityCorrelationIdProvider>();
builder.Services.AddScoped<IIdempotencyLock, SqlServerIdempotencyLock>();
builder.Services.AddScoped<IdempotencyStore>();
builder.Services.AddScoped<IBulkNotificationJobService, BulkNotificationJobService>();
builder.Services.AddScoped<BulkNotificationProcessor>();
builder.Services.AddSingleton<IBulkNotificationFailureInjector, ConfiguredBulkNotificationFailureInjector>();
if (builder.Configuration.GetValue("RabbitMq:Enabled", false))
{
    BulkNotificationQueueWorker.ValidateConfiguration(builder.Configuration);
    builder.Services.AddHostedService<BulkNotificationQueueWorker>();
}

var app = builder.Build();

if (builder.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    await dbContext.Database.MigrateAsync();
}

// Forwarded headers must be applied before HTTPS decisions, rate limiting,
// authentication, and endpoint execution so those components see the original
// scheme and the right-most client address supplied by the trusted proxy hop.
if (builder.Configuration.GetValue("ReverseProxy:TrustForwardedHeaders", false))
{
    app.UseForwardedHeaders();
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Notification Service API v1");
    options.SwaggerEndpoint("/swagger/v2/swagger.json", "Notification Service API v2");
    options.RoutePrefix = "swagger";
});

if (builder.Configuration.GetValue("HttpsRedirection:Enabled", true))
{
    app.UseHttpsRedirection();
}
app.UseExceptionHandler();
if (frontendOrigins.Length > 0)
{
    app.UseCors(FrontendCorsPolicy);
}
app.UseStatusCodePages(async statusCodeContext =>
{
    var httpContext = statusCodeContext.HttpContext;
    var response = httpContext.Response;
    var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
    await problemDetailsService.WriteAsync(new ProblemDetailsContext
    {
        HttpContext = httpContext,
        ProblemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = response.StatusCode,
            Title = "The requested operation could not be completed.",
            Instance = httpContext.Request.Path
        }
    });
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Release-Color"] = releaseColor;
        context.Response.Headers["X-Release-Sha"] = releaseSha;

        if (context.GetRequestedApiVersion()?.MajorVersion == 1)
        {
            context.Response.Headers.TryAdd("Deprecation", "true");
            context.Response.Headers.TryAdd("Sunset", "Thu, 31 Dec 2026 23:59:59 GMT");
        }

        return Task.CompletedTask;
    });

    await next();
});
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => !registration.Tags.Contains("ready")
}).ExcludeFromDescription();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
}).ExcludeFromDescription();
app.MapGet("/health/version", () => Results.Ok(new
{
    service = "notification-api",
    color = releaseColor,
    sha = releaseSha
})).ExcludeFromDescription();
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

/// <summary>Exposes the minimal-host entry point to the integration-test host.</summary>
public partial class Program;
