using MealSeal.Api.Data;
using Microsoft.AspNetCore.Identity;

namespace MealSeal.Api.Services;

/// <summary>
/// Thin wrapper around ASP.NET Core's <see cref="PasswordHasher{TUser}"/> (PBKDF2-HMAC-SHA256
/// with a random salt per password, in its versioned/self-describing format) so the rest of the
/// app never touches raw password bytes.
/// </summary>
public sealed class PasswordService
{
    private readonly PasswordHasher<UserProfile> _hasher = new();

    public string Hash(UserProfile user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(UserProfile user, string password)
    {
        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
