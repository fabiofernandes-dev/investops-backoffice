using System.Threading.RateLimiting;
using InvestOps.Api;
using InvestOps.Application;
using InvestOps.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();

const string DevCorsPolicy = "dev-cors";
builder.Services.AddCors(options => options.AddPolicy(DevCorsPolicy, policy =>
    policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod()));

var rateLimiting = builder.Configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value)
            ? (int)value.TotalSeconds
            : rateLimiting.WindowSeconds;
        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();
        return ValueTask.CompletedTask;
    };

    // Health checks ficam fora do limite: probes de liveness/readiness não podem ser rejeitadas.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.StartsWithSegments("/health")
            ? RateLimitPartition.GetNoLimiter("health")
            : RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimiting.PermitLimit,
                Window = TimeSpan.FromSeconds(rateLimiting.WindowSeconds)
            }));

    // Política mais rígida para endpoints de escrita/efeito colateral (import, reprocess).
    options.AddPolicy(RateLimitingOptions.WritePolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimiting.WritePermitLimit,
            Window = TimeSpan.FromSeconds(rateLimiting.WindowSeconds)
        }));
});

var connectionString = builder.Configuration.GetConnectionString("Postgres")!;
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: new[] { "ready" });

var app = builder.Build();

app.UseSerilogRequestLogging();

app.UseRateLimiter();

if (app.Environment.IsDevelopment())
    app.UseCors(DevCorsPolicy);

if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
}

app.MapControllers();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = h => h.Tags.Contains("ready") });

app.Run();

// Atrás do edge nginx todas as conexões chegam com o IP do proxy — o particionamento por IP
// só diferencia clientes com forwarded headers confiáveis (ver ADR 0006).
static string ClientKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

public partial class Program { }
