using MealSeal.Api.Data;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace MealSeal.Api.Tests;

public sealed class PostgresConnectionStringResolverTests
{
    [Fact]
    public void Resolve_UsesPooledUrlForApplication_AndDirectUrlForMigration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://app_user:p%40ss@ep-app-pooler.example.test/neondb?sslmode=require",
            ["DATABASE_URL_UNPOOLED"] = "postgresql://app_user:p%40ss@ep-app.example.test/neondb?sslmode=require"
        }).Build();

        var result = PostgresConnectionStringResolver.Resolve(configuration);
        var application = new NpgsqlConnectionStringBuilder(result.Application);
        var migration = new NpgsqlConnectionStringBuilder(result.Migration);

        Assert.Equal("ep-app-pooler.example.test", application.Host);
        Assert.Equal("ep-app.example.test", migration.Host);
        Assert.Equal("neondb", application.Database);
        Assert.Equal("app_user", application.Username);
        Assert.Equal("p@ss", application.Password);
        Assert.Equal(SslMode.Require, application.SslMode);
    }

    [Fact]
    public void Resolve_FallsBackToApplicationConnectionForLocalDevelopment()
    {
        const string local = "Host=localhost;Database=mealseal;Username=developer";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = local
        }).Build();

        var result = PostgresConnectionStringResolver.Resolve(configuration);

        Assert.Equal(local, result.Application);
        Assert.Equal(local, result.Migration);
    }
}
