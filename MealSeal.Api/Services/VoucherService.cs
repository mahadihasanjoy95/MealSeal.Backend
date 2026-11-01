using System.Globalization;
using System.Text.RegularExpressions;
using MealSeal.Api.Contracts;
using MealSeal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace MealSeal.Api.Services;

public sealed partial class VoucherService(MealSealDbContext db, VoucherCryptoService crypto)
{
    private static readonly HashSet<string> MealTypes = ["Breakfast", "Lunch", "Dinner"];

    public async Task<SignedVoucher> CreateAsync(CreateVoucherRequest request, string actorUid, CancellationToken cancellationToken)
    {
        ValidateText(request.RecipientRef, nameof(request.RecipientRef), 120);
        ValidateText(request.RestaurantId, nameof(request.RestaurantId), 128);
        if (!MealTypes.Contains(request.MealType))
            throw new ApiException(400, "INVALID_MEAL_TYPE", "Meal type must be Breakfast, Lunch, or Dinner.");
        if (request.Quantity is < 1 or > 20)
            throw new ApiException(400, "INVALID_QUANTITY", "Quantity must be between 1 and 20.");
        if (!DateOnly.TryParseExact(request.ExpiryDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiryDate))
            throw new ApiException(400, "INVALID_EXPIRY", "Expiry date must use YYYY-MM-DD.");

        var expiresAt = new DateTimeOffset(expiryDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            .ToUnixTimeMilliseconds();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (expiresAt <= now) throw new ApiException(400, "INVALID_EXPIRY", "Expiry date must be today or later.");

        var payload = new VoucherPayload(1, Guid.NewGuid().ToString(), request.RecipientRef.Trim(),
            request.RestaurantId.Trim(), request.MealType, request.Quantity, now, expiresAt, actorUid);
        var signed = crypto.Sign(payload);
        db.Vouchers.Add(new VoucherRecord
        {
            VoucherId = payload.VoucherId,
            RecipientRef = payload.RecipientRef,
            RestaurantId = payload.RestaurantId,
            MealType = payload.MealType,
            Quantity = payload.Quantity,
            IssuedAt = payload.IssuedAt,
            ExpiresAt = payload.ExpiresAt,
            IssuerUid = actorUid,
            Status = "ACTIVE",
            PayloadJson = crypto.CanonicalJson(payload),
            PayloadSha256 = crypto.Sha256(payload),
            Signature = signed.Signature,
            Algorithm = signed.Algorithm,
            KeyId = signed.KeyId
        });
        db.AuditEvents.Add(new AuditEvent { VoucherId = payload.VoucherId, ActorUid = actorUid, Action = "CREATE", Outcome = "SUCCESS" });
        await db.SaveChangesAsync(cancellationToken);
        return signed;
    }

    public async Task<List<VoucherSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var records = await db.Vouchers.AsNoTracking().OrderByDescending(x => x.IssuedAt).ToListAsync(cancellationToken);
        return records.Select(ToSummary).ToList();
    }

    public async Task<SignedVoucher> GetSignedAsync(string voucherId, CancellationToken cancellationToken)
    {
        var record = await db.Vouchers.AsNoTracking().SingleOrDefaultAsync(x => x.VoucherId == voucherId, cancellationToken)
                     ?? throw new ApiException(404, "NOT_FOUND", "Voucher was not found.");
        return ToSigned(record);
    }

    public async Task<VoucherSummary> GetAsync(string voucherId, CancellationToken cancellationToken)
    {
        var record = await db.Vouchers.AsNoTracking().SingleOrDefaultAsync(x => x.VoucherId == voucherId, cancellationToken)
                     ?? throw new ApiException(404, "NOT_FOUND", "Voucher was not found.");
        return ToSummary(record);
    }

    public async Task<VoucherRecord> VerifyAsync(SignedVoucher signed, string restaurantId, CancellationToken cancellationToken)
    {
        if (signed.Payload is null || signed.Payload.Version != 1 || string.IsNullOrWhiteSpace(signed.Payload.VoucherId))
            throw new ApiException(400, "INVALID_VOUCHER", "The voucher structure is invalid.");
        if (!crypto.Verify(signed))
            throw new ApiException(400, "INVALID_SIGNATURE", "The voucher signature is invalid.");

        var record = await db.Vouchers.AsNoTracking()
                         .SingleOrDefaultAsync(x => x.VoucherId == signed.Payload.VoucherId, cancellationToken)
                     ?? throw new ApiException(404, "NOT_FOUND", "Voucher was not found.");
        if (!CryptographicallyMatches(record, signed))
            throw new ApiException(400, "INVALID_VOUCHER", "The voucher does not match the issued record.");
        if (!string.Equals(record.RestaurantId, restaurantId, StringComparison.Ordinal))
            throw new ApiException(403, "WRONG_RESTAURANT", "This voucher belongs to another restaurant.");
        ThrowForUnavailable(record);
        return record;
    }

    public async Task<VoucherRecord> RedeemAsync(SignedVoucher signed, string actorUid, string restaurantId,
        CancellationToken cancellationToken)
    {
        await VerifyAsync(signed, restaurantId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var affected = await db.Vouchers
            .Where(x => x.VoucherId == signed.Payload.VoucherId && x.Status == "ACTIVE" &&
                        x.ExpiresAt > now.ToUnixTimeMilliseconds() && x.Signature == signed.Signature)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "REDEEMED")
                .SetProperty(x => x.RedeemedByUid, actorUid)
                .SetProperty(x => x.RedeemedAt, now), cancellationToken);

        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            var current = await db.Vouchers.AsNoTracking().SingleAsync(x => x.VoucherId == signed.Payload.VoucherId, cancellationToken);
            ThrowForUnavailable(current);
            throw new ApiException(409, "ALREADY_REDEEMED", "Voucher could not be redeemed.");
        }

        db.AuditEvents.Add(new AuditEvent { VoucherId = signed.Payload.VoucherId, ActorUid = actorUid, Action = "REDEEM", Outcome = "SUCCESS" });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await db.Vouchers.AsNoTracking().SingleAsync(x => x.VoucherId == signed.Payload.VoucherId, cancellationToken);
    }

    public async Task<VoucherRecord> RevokeAsync(string voucherId, string actorUid, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var affected = await db.Vouchers.Where(x => x.VoucherId == voucherId && x.Status == "ACTIVE")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "REVOKED")
                .SetProperty(x => x.RevokedByUid, actorUid)
                .SetProperty(x => x.RevokedAt, now), cancellationToken);
        if (affected != 1)
        {
            var existing = await db.Vouchers.AsNoTracking().SingleOrDefaultAsync(x => x.VoucherId == voucherId, cancellationToken)
                           ?? throw new ApiException(404, "NOT_FOUND", "Voucher was not found.");
            await transaction.RollbackAsync(cancellationToken);
            ThrowForUnavailable(existing);
            throw new ApiException(409, "INVALID_STATE", "Only an active voucher can be revoked.");
        }
        db.AuditEvents.Add(new AuditEvent { VoucherId = voucherId, ActorUid = actorUid, Action = "REVOKE", Outcome = "SUCCESS" });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await db.Vouchers.AsNoTracking().SingleAsync(x => x.VoucherId == voucherId, cancellationToken);
    }

    public static VoucherSummary ToSummary(VoucherRecord x) => new(x.VoucherId, x.RecipientRef, x.RestaurantId,
        x.MealType, x.Quantity, x.IssuedAt, x.ExpiresAt, x.Status, x.Algorithm, x.KeyId, x.RedeemedAt, x.RevokedAt);

    private SignedVoucher ToSigned(VoucherRecord x) => new(new VoucherPayload(1, x.VoucherId, x.RecipientRef,
        x.RestaurantId, x.MealType, x.Quantity, x.IssuedAt, x.ExpiresAt, x.IssuerUid), x.Signature, x.Algorithm, x.KeyId);

    private bool CryptographicallyMatches(VoucherRecord record, SignedVoucher signed) =>
        record.PayloadJson == crypto.CanonicalJson(signed.Payload) && record.PayloadSha256 == crypto.Sha256(signed.Payload) &&
        record.Signature == signed.Signature && record.Algorithm == signed.Algorithm && record.KeyId == signed.KeyId;

    private static void ThrowForUnavailable(VoucherRecord record)
    {
        if (record.Status == "REDEEMED") throw new ApiException(409, "ALREADY_REDEEMED", "Voucher has already been redeemed.");
        if (record.Status == "REVOKED") throw new ApiException(409, "REVOKED", "Voucher has been revoked.");
        if (record.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            throw new ApiException(410, "EXPIRED", "Voucher has expired.");
        if (record.Status != "ACTIVE") throw new ApiException(409, "INVALID_STATE", "Voucher is not active.");
    }

    private static void ValidateText(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || !SafeTextRegex().IsMatch(value))
            throw new ApiException(400, "INVALID_INPUT", $"{field} contains unsupported characters or has an invalid length.");
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 ._@/\\-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTextRegex();
}
