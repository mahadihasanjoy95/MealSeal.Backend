namespace MealSeal.Api;

public sealed class MealSealOptions
{
    public string Algorithm { get; set; } = "ES256";
    public string KeyId { get; set; } = "mealseal-key-1";
    public string PrivateKeyPath { get; set; } = "keys/private-key.pem";
    public string PublicKeyPath { get; set; } = "keys/public-key.pem";
    public string? PrivateKeyPem { get; set; }
    public string? PublicKeyPem { get; set; }
}

/// <summary>
/// Options for MealSeal's own access-token signing key — deliberately separate from
/// <see cref="MealSealOptions"/>'s voucher-signing key (separation of duties: a leaked
/// auth key should not let an attacker forge vouchers, and vice versa).
/// </summary>
public sealed class AuthOptions
{
    public string Algorithm { get; set; } = "ES256";
    public string KeyId { get; set; } = "mealseal-auth-key-1";
    public string Issuer { get; set; } = "mealseal-api";
    public string Audience { get; set; } = "mealseal-app";
    public int AccessTokenLifetimeMinutes { get; set; } = 720; // 12 hours
    public string PrivateKeyPath { get; set; } = "keys/auth-private-key.pem";
    public string PublicKeyPath { get; set; } = "keys/auth-public-key.pem";
    public string? PrivateKeyPem { get; set; }
    public string? PublicKeyPem { get; set; }
}

/// <summary>
/// Bootstrap accounts, hashed into the database on startup. There's no self-registration and no
/// café-management UI yet, so the café-staff accounts are a small static list — one per café the
/// mobile app's create-voucher screen already offers — rather than something configured per
/// account. All of them share <see cref="StaffPassword"/>; change it (and the admin's) via
/// POST /auth/users/{userId}/reset-password once signed in, same as any other account.
/// </summary>
public sealed class SeedOptions
{
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string? StaffPassword { get; set; }
}
