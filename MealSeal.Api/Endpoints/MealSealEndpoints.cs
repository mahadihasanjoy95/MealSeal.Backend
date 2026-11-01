using System.Security.Claims;
using System.Text.Json;
using MealSeal.Api.Contracts;
using MealSeal.Api.Data;
using MealSeal.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace MealSeal.Api.Endpoints;

public static class MealSealEndpoints
{
    public static IEndpointRouteBuilder MapMealSealEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", async (MealSealDbContext db, CancellationToken ct) =>
        {
            var database = await db.Database.CanConnectAsync(ct);
            return Results.Ok(new { status = database ? "ok" : "degraded", database });
        }).AllowAnonymous();

        app.MapGet("/public-key", (VoucherCryptoService crypto) => Results.Ok(new
        {
            algorithm = "ES256",
            publicKey = crypto.PublicKeyPem
        })).AllowAnonymous();

        app.MapGet("/auth/me", (ClaimsPrincipal principal) => Results.Ok(new
        {
            success = true,
            uid = Uid(principal),
            role = principal.FindFirstValue(ClaimTypes.Role),
            restaurantId = principal.FindFirstValue("restaurant_id")
        })).RequireAuthorization().RequireRateLimiting("api");

        var admin = app.MapGroup("/vouchers").RequireAuthorization(policy => policy.RequireRole("CHARITY_ADMIN"))
            .RequireRateLimiting("api");

        admin.MapPost("/create", async (CreateVoucherRequest request, VoucherService service,
            ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var signed = await service.CreateAsync(request, Uid(principal), ct);
            return Results.Ok(new { success = true, signedVoucher = signed });
        });

        admin.MapGet("", async (VoucherService service, CancellationToken ct) =>
            Results.Ok(new { success = true, vouchers = await service.ListAsync(ct) }));

        admin.MapGet("/{voucherId}", async (string voucherId, VoucherService service, CancellationToken ct) =>
            Results.Ok(new { success = true, voucher = await service.GetAsync(voucherId, ct) }));

        admin.MapGet("/{voucherId}/signed", async (string voucherId, VoucherService service, CancellationToken ct) =>
            Results.Ok(new { success = true, signedVoucher = await service.GetSignedAsync(voucherId, ct) }));

        admin.MapGet("/{voucherId}/qr", async (string voucherId, VoucherService service, CancellationToken ct) =>
        {
            var signed = await service.GetSignedAsync(voucherId, ct);
            var json = JsonSerializer.Serialize(signed, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(json, QRCodeGenerator.ECCLevel.Q);
            using var png = new PngByteQRCode(data);
            return Results.File(png.GetGraphic(8), "image/png", $"mealseal-{voucherId}.png");
        });

        admin.MapPost("/{voucherId}/revoke", async (string voucherId, VoucherService service,
            ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var record = await service.RevokeAsync(voucherId, Uid(principal), ct);
            return Results.Ok(new { success = true, status = record.Status, voucherId = record.VoucherId });
        });

        var staff = app.MapGroup("/vouchers").RequireAuthorization(policy => policy.RequireRole("RESTAURANT_STAFF"))
            .RequireRateLimiting("api");

        staff.MapPost("/verify", async (SignedVoucherRequest request, VoucherService service,
            ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var record = await service.VerifyAsync(request.SignedVoucher, RestaurantId(principal), ct);
            return Results.Ok(new
            {
                success = true, valid = true, verified = true, status = record.Status,
                voucher = VoucherService.ToSummary(record)
            });
        });

        staff.MapPost("/redeem", async (SignedVoucherRequest request, VoucherService service,
            ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var record = await service.RedeemAsync(request.SignedVoucher, Uid(principal), RestaurantId(principal), ct);
            return Results.Ok(new
            {
                success = true, valid = true, verified = true, status = record.Status,
                voucher = VoucherService.ToSummary(record)
            });
        });

        return app;
    }

    private static string Uid(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new ApiException(401, "UNAUTHORIZED", "User identity is missing.");

    private static string RestaurantId(ClaimsPrincipal principal) =>
        principal.FindFirstValue("restaurant_id")
        ?? throw new ApiException(403, "FORBIDDEN", "Restaurant staff profile has no restaurant assignment.");
}
