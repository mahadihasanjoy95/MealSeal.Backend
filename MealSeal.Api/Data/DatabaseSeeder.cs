using MealSeal.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MealSeal.Api.Data;

public static class DatabaseSeeder
{
    /// <summary>
    /// The cafés café staff can sign in for — kept in lockstep with the mobile app's
    /// create-voucher café picker (CAFE_OPTIONS in create-voucher.tsx) so every café an admin can
    /// assign a voucher to also has a staff account able to redeem it.
    /// </summary>
    private static readonly (string Email, string RestaurantId)[] StaffAccounts =
    [
        ("hillside@mealseal.local", "CAFE-01"),
        ("riverside@mealseal.local", "CAFE-02"),
        ("corner@mealseal.local", "CAFE-03")
    ];

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<MealSealDbContext>();
        var passwords = services.GetRequiredService<PasswordService>();
        var seed = services.GetRequiredService<IOptions<SeedOptions>>().Value;

        if (!string.IsNullOrWhiteSpace(seed.AdminEmail) && !string.IsNullOrWhiteSpace(seed.AdminPassword))
            await UpsertAsync(db, passwords, seed.AdminEmail, seed.AdminPassword, "CHARITY_ADMIN", null);

        if (!string.IsNullOrWhiteSpace(seed.StaffPassword))
        {
            foreach (var (email, restaurantId) in StaffAccounts)
                await UpsertAsync(db, passwords, email, seed.StaffPassword, "RESTAURANT_STAFF", restaurantId);
        }

        await db.SaveChangesAsync();
    }

    private static async Task UpsertAsync(
        MealSealDbContext db, PasswordService passwords, string email, string password, string role, string? restaurantId)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == normalizedEmail);

        if (user is null)
        {
            user = new UserProfile
            {
                UserId = Guid.NewGuid().ToString(),
                Email = normalizedEmail,
                PasswordHash = string.Empty,
                Role = role,
                RestaurantId = restaurantId
            };
            user.PasswordHash = passwords.Hash(user, password);
            db.Users.Add(user);
        }
        else
        {
            user.Role = role;
            user.RestaurantId = restaurantId;
            user.Active = true;
            // Only (re)seed the password if it isn't already set to something else in a prior
            // run — avoids clobbering a password the admin has since changed via the reset endpoint
            // on every restart. Comparing the seed value's hash would require a second hash call;
            // simplest safe behaviour is to only set it the first time the row is created above.
        }
    }
}
