using Npgsql;

namespace MealSeal.Api.Data;

public sealed record PostgresConnectionStrings(string Application, string Migration);

public static class PostgresConnectionStringResolver
{
    public static PostgresConnectionStrings Resolve(IConfiguration configuration, string? fallback = null)
    {
        var application = FirstNotEmpty(
            configuration.GetConnectionString("Postgres"),
            configuration["DATABASE_URL"],
            fallback);

        if (application is null)
            throw new InvalidOperationException(
                "Configure ConnectionStrings:Postgres or Neon DATABASE_URL before starting the API.");

        var migration = FirstNotEmpty(
            configuration.GetConnectionString("PostgresMigration"),
            configuration["DATABASE_URL_UNPOOLED"],
            application)!;

        return new PostgresConnectionStrings(Normalize(application), Normalize(migration));
    }

    public static string Normalize(string connectionString)
    {
        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
            return connectionString;

        var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        if (string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("The PostgreSQL URL does not contain a database name.");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = database,
            Username = Uri.UnescapeDataString(uri.UserInfo.Split(':', 2)[0]),
            Password = uri.UserInfo.Contains(':')
                ? Uri.UnescapeDataString(uri.UserInfo.Split(':', 2)[1])
                : string.Empty,
            SslMode = SslMode.Require,
            Pooling = true
        };

        return builder.ConnectionString;
    }

    private static string? FirstNotEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
