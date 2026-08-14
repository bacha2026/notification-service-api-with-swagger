using System.Reflection;
using System.Diagnostics;
using Asp.Versioning;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using NSA.Application.Abstractions;
using NSA.Application.Configuration;
using NSA.Infrastructure.Configuration;
using NSA.Infrastructure.Health;
using NSA.Infrastructure.Hosting;
using NSA.Infrastructure.Messaging;
using NSA.Infrastructure.Observability;
using NSA.Persistence;
using NSA.Persistence.Concrete;
using NSA.Presentation.ExceptionHandling;
using NSA.Presentation.OpenApi;
using NSA.Service;

var builder = WebApplication.CreateBuilder(args);

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
            timeout: TimeSpan.FromSeconds(3))
        .AddCheck<RabbitMqReadinessHealthCheck>(
            "rabbitmq",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["ready"],
            timeout: TimeSpan.FromSeconds(3));
}
builder.Services.AddSwaggerGen(options =>
{
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

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
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
});

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("NotificationDb")));

builder.Services.AddScoped<ICartRepository, CartRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IBulkNotificationJobRepository, BulkNotificationJobRepository>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddOptions<BulkNotificationOptions>()
    .Bind(builder.Configuration.GetSection(BulkNotificationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<NotificationRecipientOptions>()
    .Bind(builder.Configuration.GetSection(NotificationRecipientOptions.SectionName))
    .ValidateDataAnnotations()
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
    return new NotificationRecipientSettings(options.AdminEmail, options.DefaultVisitorEmail);
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IApplicationStopping, HostApplicationStopping>();
builder.Services.AddSingleton<ICorrelationIdProvider, ActivityCorrelationIdProvider>();
RabbitMqConfiguration.Validate(builder.Configuration);
builder.Services.AddSingleton<RabbitMqPublishResiliencePolicyProvider>();
builder.Services.AddSingleton<RabbitMqBulkNotificationPublisher>();
builder.Services.AddSingleton<IBulkNotificationFailureInjector, ConfiguredBulkNotificationFailureInjector>();
builder.Services.AddSingleton<IBulkNotificationCommandPublisher>(serviceProvider =>
    serviceProvider.GetRequiredService<RabbitMqBulkNotificationPublisher>());
builder.Services.AddScoped<IBulkNotificationJobService>(serviceProvider => new BulkNotificationJobService(
    serviceProvider.GetRequiredService<IBulkNotificationJobRepository>(),
    serviceProvider.GetRequiredService<IBulkNotificationCommandPublisher>(),
    serviceProvider.GetRequiredService<BulkNotificationSettings>(),
    serviceProvider.GetRequiredService<TimeProvider>(),
    serviceProvider.GetRequiredService<ILogger<BulkNotificationJobService>>()));

var app = builder.Build();

if (builder.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    await dbContext.Database.MigrateAsync();
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
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        if (context.GetRequestedApiVersion()?.MajorVersion == 1)
        {
            context.Response.Headers.TryAdd("Deprecation", "true");
            context.Response.Headers.TryAdd("Sunset", "Thu, 31 Dec 2026 23:59:59 GMT");
        }

        return Task.CompletedTask;
    });

    await next();
});
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
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => !registration.Tags.Contains("ready")
}).ExcludeFromDescription();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
}).ExcludeFromDescription();
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

/// <summary>Exposes the minimal-host entry point to the integration-test host.</summary>
public partial class Program;
