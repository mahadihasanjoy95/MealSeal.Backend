using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MealSeal.Api.Data;

public sealed class MealSealDbContextFactory : IDesignTimeDbContextFactory<MealSealDbContext>
{
    public MealSealDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PostgresMigration")
                               ?? Environment.GetEnvironmentVariable("DATABASE_URL_UNPOOLED")
                               ?? Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                               ?? Environment.GetEnvironmentVariable("DATABASE_URL")
                               ?? "Host=localhost;Database=mealseal";
        var options = new DbContextOptionsBuilder<MealSealDbContext>()
            .UseNpgsql(PostgresConnectionStringResolver.Normalize(connectionString))
            .Options;
        return new MealSealDbContext(options);
    }
}
