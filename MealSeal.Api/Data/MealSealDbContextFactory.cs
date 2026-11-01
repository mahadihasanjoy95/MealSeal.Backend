using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MealSeal.Api.Data;

public sealed class MealSealDbContextFactory : IDesignTimeDbContextFactory<MealSealDbContext>
{
    public MealSealDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                               ?? "Host=localhost;Database=mealseal";
        var options = new DbContextOptionsBuilder<MealSealDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new MealSealDbContext(options);
    }
}
