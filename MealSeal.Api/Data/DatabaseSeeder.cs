using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MealSeal.Api.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<MealSealDbContext>();
        var seed = services.GetRequiredService<IOptions<SeedOptions>>().Value;

        if (!string.IsNullOrWhiteSpace(seed.AdminUid))
            await UpsertAsync(db, seed.AdminUid, "CHARITY_ADMIN", null);
        if (!string.IsNullOrWhiteSpace(seed.StaffUid))
            await UpsertAsync(db, seed.StaffUid, "RESTAURANT_STAFF", seed.StaffRestaurantId);

        await db.SaveChangesAsync();
    }

    private static async Task UpsertAsync(MealSealDbContext db, string uid, string role, string? restaurantId)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.FirebaseUid == uid);
        if (user is null)
            db.Users.Add(new UserProfile { FirebaseUid = uid, Role = role, RestaurantId = restaurantId });
        else
        {
            user.Role = role;
            user.RestaurantId = restaurantId;
            user.Active = true;
        }
    }
}
