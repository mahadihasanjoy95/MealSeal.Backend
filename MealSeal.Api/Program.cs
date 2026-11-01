using System.Security.Claims;
using System.Threading.RateLimiting;
using MealSeal.Api;
using MealSeal.Api.Data;
using MealSeal.Api.Endpoints;
using MealSeal.Api.Security;
using MealSeal.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

if (args.Contains("--generate-keys", StringComparer.Ordinal))
{
    VoucherCryptoService.GenerateKeys(Path.Combine(builder.Environment.ContentRootPath, "keys"));
    Console.WriteLine("Generated keys/private-key.pem and keys/public-key.pem.");
    return;
}

builder.Services.Configure<MealSealOptions>(builder.Configuration.GetSection("MealSeal"));
builder.Services.Configure<FirebaseOptions>(builder.Configuration.GetSection("Firebase"));
builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection("Seed"));

var connectionString = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(connectionString) && !builder.Environment.IsEnvironment("Testing"))
    throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

builder.Services.AddDbContext<MealSealDbContext>(options =>
    options.UseNpgsql(connectionString ?? "Host=localhost;Database=mealseal_testing"));
builder.Services.AddSingleton<IFirebaseTokenVerifier, FirebaseTokenVerifier>();
builder.Services.AddScoped<VoucherCryptoService>();
builder.Services.AddScoped<VoucherService>();
builder.Services.AddAuthentication("Firebase")
    .AddScheme<AuthenticationSchemeOptions, FirebaseAuthenticationHandler>("Firebase", _ => { });
builder.Services.AddAuthorization();
builder.Services.AddOpenApi();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);

var app = builder.Build();
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapMealSealEndpoints();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MealSealDbContext>();
    await db.Database.MigrateAsync();
    await DatabaseSeeder.SeedAsync(scope.ServiceProvider);
}

await app.RunAsync();

public partial class Program;
