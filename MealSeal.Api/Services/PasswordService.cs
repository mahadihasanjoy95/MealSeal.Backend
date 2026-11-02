using MealSeal.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace MealSeal.Api.Services;

/// <summary>
/// SECURITY: Passwords are never stored directly. New password records use ASP.NET Core Identity
/// V3's versioned format: PBKDF2-HMAC-SHA512, a cryptographically random 128-bit salt, a 256-bit
/// derived subkey, and 100,000 iterations. The salt and work-factor metadata are stored inside the
/// encoded hash so verification does not require a separate salt column.
/// </summary>
public sealed class PasswordService
{
    private readonly PasswordHasher<UserProfile> _hasher = new(Options.Create(new PasswordHasherOptions
    {
        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
        IterationCount = 100_000
    }));

    public string Hash(UserProfile user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(UserProfile user, string password)
    {
        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
