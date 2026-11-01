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
using Microsoft.OpenApi;

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

var testingFallback = builder.Environment.IsEnvironment("Testing")
    ? "Host=localhost;Database=mealseal_testing"
    : null;
var databaseConnections = PostgresConnectionStringResolver.Resolve(builder.Configuration, testingFallback);

builder.Services.AddDbContext<MealSealDbContext>(options =>
    options.UseNpgsql(databaseConnections.Application));
builder.Services.AddSingleton<IFirebaseTokenVerifier, FirebaseTokenVerifier>();
builder.Services.AddScoped<VoucherCryptoService>();
builder.Services.AddScoped<VoucherService>();
builder.Services.AddAuthentication("Firebase")
    .AddScheme<AuthenticationSchemeOptions, FirebaseAuthenticationHandler>("Firebase", _ => { });
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MealSeal API",
        Version = "v1",
        Description = "Cryptographically signed, single-use meal vouchers. Protected operations require a Firebase ID token."
    });
    options.AddSecurityDefinition("FirebaseBearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "Firebase ID token",
        Description = "Paste only the Firebase ID token. Swagger adds the 'Bearer' prefix automatically."
    });
    options.OperationFilter<FirebaseSecurityOperationFilter>();
});

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

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "MealSeal API v1");
    options.DocumentTitle = "MealSeal API";
    options.DisplayRequestDuration();
});
app.MapMealSealEndpoints();

if (!app.Environment.IsEnvironment("Testing"))
{
    var migrationOptions = new DbContextOptionsBuilder<MealSealDbContext>()
        .UseNpgsql(databaseConnections.Migration)
        .Options;
    await using (var migrationDb = new MealSealDbContext(migrationOptions))
    {
        await migrationDb.Database.MigrateAsync();
    }

    await using var scope = app.Services.CreateAsyncScope();
    await DatabaseSeeder.SeedAsync(scope.ServiceProvider);
}

await app.RunAsync();

public partial class Program;
