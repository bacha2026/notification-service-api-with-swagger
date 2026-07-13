using System.Reflection;
using System.Diagnostics;
using Asp.Versioning;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using NSA.Application.Abstractions;
using NSA.Infrastructure.BackgroundServices;
using NSA.Infrastructure.Email;
using NSA.Persistence;
using NSA.Persistence.Concrete;
using NSA.Persistence.Interfaces;
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
builder.Services.AddScoped<EmailNotificationLogger>();
builder.Services.AddOptions<PostboundOptions>()
    .Bind(builder.Configuration.GetSection(PostboundOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.ApiKey), "Postbound:ApiKey is required when Postbound is enabled.")
    .Validate(options => !options.Enabled || (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps), "Postbound:BaseUrl must be an absolute HTTPS URL when Postbound is enabled.")
    .ValidateOnStart();
builder.Services.AddSingleton<EmailResiliencePolicyProvider>();
builder.Services.AddHttpClient<PostboundEmailSender>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PostboundOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
    // Each attempt is bounded by the innermost Polly timeout policy. Disabling the
    // HttpClient-wide timeout prevents it from cancelling the complete retry sequence.
    client.Timeout = Timeout.InfiniteTimeSpan;
})
    // The breaker wraps the retry policy, so one exhausted logical send counts as one
    // breaker failure rather than counting each individual retry attempt.
    .AddPolicyHandler((serviceProvider, _) => serviceProvider.GetRequiredService<EmailResiliencePolicyProvider>().CircuitBreaker)
    .AddPolicyHandler((serviceProvider, _) => serviceProvider.GetRequiredService<EmailResiliencePolicyProvider>().Retry)
    .AddPolicyHandler((serviceProvider, _) => serviceProvider.GetRequiredService<EmailResiliencePolicyProvider>().Timeout);
builder.Services.AddScoped<IEmailSender>(serviceProvider => serviceProvider.GetRequiredService<PostboundEmailSender>());
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddOptions<BulkNotificationOptions>()
    .Bind(builder.Configuration.GetSection(BulkNotificationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BulkNotificationJobService>();
builder.Services.AddSingleton<IBulkNotificationJobService>(serviceProvider => serviceProvider.GetRequiredService<BulkNotificationJobService>());
builder.Services.AddHostedService<BulkNotificationWorker>();

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

app.UseHttpsRedirection();
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
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

/// <summary>Exposes the minimal-host entry point to the integration-test host.</summary>
public partial class Program;
