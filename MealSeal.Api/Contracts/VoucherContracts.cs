namespace MealSeal.Api.Contracts;

public sealed record VoucherPayload(
    int Version,
    string VoucherId,
    string RecipientRef,
    string RestaurantId,
    string MealType,
    int Quantity,
    long IssuedAt,
    long ExpiresAt,
    string IssuerUid);

public sealed record SignedVoucher(VoucherPayload Payload, string Signature, string Algorithm, string KeyId);

public sealed record CreateVoucherRequest(
    string RecipientRef,
    string RestaurantId,
    string MealType,
    int Quantity,
    string ExpiryDate);

public sealed record SignedVoucherRequest(SignedVoucher SignedVoucher);

public sealed record VoucherSummary(
    string VoucherId,
    string RecipientRef,
    string RestaurantId,
    string MealType,
    int Quantity,
    long IssuedAt,
    long ExpiresAt,
    string Status,
    string Algorithm,
    string KeyId,
    DateTimeOffset? RedeemedAt,
    DateTimeOffset? RevokedAt);
