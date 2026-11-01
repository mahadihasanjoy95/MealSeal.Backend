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

public sealed class FirebaseOptions
{
    public string ProjectId { get; set; } = "mealseal-c49a9";
    public string? ServiceAccountPath { get; set; }
    public string? ServiceAccountJson { get; set; }
}

public sealed class SeedOptions
{
    public string? AdminUid { get; set; }
    public string? StaffUid { get; set; }
    public string? StaffRestaurantId { get; set; }
}
