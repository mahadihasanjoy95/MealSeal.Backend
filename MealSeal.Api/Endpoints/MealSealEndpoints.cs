using System.Security.Claims;
using MealSeal.Api.Contracts;
using MealSeal.Api.Data;
using MealSeal.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MealSeal.Api.Endpoints;

public static class MealSealEndpoints
{
    private static readonly HashSet<string> ValidRoles = ["CHARITY_ADMIN", "RESTAURANT_STAFF"];

    public static IEndpointRouteBuilder MapMealSealEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", async (MealSealDbContext db, CancellationToken ct) =>
        {
            var database = await db.Database.CanConnectAsync(ct);
            return Results.Ok(new { status = database ? "ok" : "degraded", database });
        }).AllowAnonymous()
            .WithTags("System")
            .WithName("GetHealth")
            .WithSummary("Check API and PostgreSQL health");

        app.MapGet("/public-key", (VoucherCryptoService crypto) => Results.Ok(new
        {
            algorithm = "ES256",
            publicKey = crypto.PublicKeyPem
        })).AllowAnonymous()
            .WithTags("System")
            .WithName("GetPublicKey")
            .WithSummary("Get the ES256 public verification key");

        app.MapPost("/auth/login", async (LoginRequest request, MealSealDbContext db,
            PasswordService passwords, AuthTokenService tokens, CancellationToken ct) =>
        {
            var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
            if (email.Length == 0 || string.IsNullOrEmpty(request.Password))
                throw new ApiException(400, "INVALID_CREDENTIALS", "Email and password are required.");

            var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email, ct);
            if (user is null || !user.Active || !passwords.Verify(user, request.Password))
                throw new ApiException(401, "INVALID_CREDENTIALS", "That email and password don't match.");

            return Results.Ok(new
            {
                success = true,
                accessToken = tokens.IssueAccessToken(user),
                role = user.Role,
                restaurantId = user.RestaurantId,
                email = user.Email
            });
        }).AllowAnonymous().RequireRateLimiting("login")
            .WithTags("Authentication")
            .WithName("Login")
            .WithSummary("Sign in with email and password, receive an access token");

        app.MapGet("/auth/me", (ClaimsPrincipal principal) => Results.Ok(new
        {
            success = true,
            uid = Uid(principal),
            email = principal.FindFirstValue(ClaimTypes.Email),
            role = principal.FindFirstValue(ClaimTypes.Role),
            restaurantId = principal.FindFirstValue("restaurant_id")
        })).RequireAuthorization().RequireRateLimiting("api")
            .WithTags("Authentication")
            .WithName("GetCurrentUser")
            .WithSummary("Get the current user's MealSeal profile");

        var userAdmin = app.MapGroup("/auth/users").RequireAuthorization(policy => policy.RequireRole("CHARITY_ADMIN"))
            .RequireRateLimiting("api");
        userAdmin.WithTags("Authentication");

        userAdmin.MapPost("", async (CreateUserRequest request, MealSealDbContext db,
            PasswordService passwords, CancellationToken ct) =>
        {
            var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
            var role = (request.Role ?? string.Empty).Trim().ToUpperInvariant();

            if (email.Length == 0 || !email.Contains('@'))
                throw new ApiException(400, "INVALID_EMAIL", "A valid email address is required.");
            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
                throw new ApiException(400, "WEAK_PASSWORD", "Password must be at least 8 characters.");
            if (!ValidRoles.Contains(role))
                throw new ApiException(400, "INVALID_ROLE", "Role must be CHARITY_ADMIN or RESTAURANT_STAFF.");
            if (role == "RESTAURANT_STAFF" && string.IsNullOrWhiteSpace(request.RestaurantId))
                throw new ApiException(400, "MISSING_RESTAURANT", "Restaurant staff accounts require a restaurantId.");
            if (await db.Users.AnyAsync(x => x.Email == email, ct))
                throw new ApiException(409, "EMAIL_IN_USE", "An account with this email already exists.");

            var user = new UserProfile
            {
                UserId = Guid.NewGuid().ToString(),
                Email = email,
                PasswordHash = string.Empty,
                Role = role,
                RestaurantId = role == "RESTAURANT_STAFF" ? request.RestaurantId!.Trim() : null
            };
            user.PasswordHash = passwords.Hash(user, request.Password);

            db.Users.Add(user);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { success = true, userId = user.UserId, email = user.Email, role = user.Role });
        }).WithName("CreateUser")
            .WithSummary("Create a new charity-admin or restaurant-staff account");

        userAdmin.MapPost("/{userId}/reset-password", async (string userId, ResetPasswordRequest request,
            MealSealDbContext db, PasswordService passwords, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
                throw new ApiException(400, "WEAK_PASSWORD", "Password must be at least 8 characters.");

            var user = await db.Users.SingleOrDefaultAsync(x => x.UserId == userId, ct)
                ?? throw new ApiException(404, "NOT_FOUND", "No account with this ID was found.");

            user.PasswordHash = passwords.Hash(user, request.NewPassword);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { success = true, userId = user.UserId });
        }).WithName("ResetUserPassword")
            .WithSummary("Set a new password for an existing account");

        var admin = app.MapGroup("/vouchers").RequireAuthorization(policy => policy.RequireRole("CHARITY_ADMIN"))
            .RequireRateLimiting("api");
        admin.WithTags("Admin vouchers");

        admin.MapPost("/create", async (CreateVoucherRequest request, VoucherService service,
            ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var signed = await service.CreateAsync(request, Uid(principal), ct);
            return Results.Ok(new { success = true, signedVoucher = signed });
        }).WithName("CreateVoucher")
            .WithSummary("Create and cryptographically sign a voucher");

        admin.MapGet("", async (VoucherService service, CancellationToken ct) =>
            Results.Ok(new { success = true, vouchers = await service.ListAsync(ct) }))
            .WithName("ListVouchers")
            .WithSummary("List all vouchers and their current states");

        admin.MapGet("/{voucherId}", async (string voucherId, VoucherService service, CancellationToken ct) =>
            Results.Ok(new { success = true, voucher = await service.GetAsync(voucherId, ct) }))
            .WithName("GetVoucher")
            .WithSummary("Get one voucher by ID");

        admin.MapGet("/{voucherId}/signed", async (string voucherId, VoucherService service, CancellationToken ct) =>
            Results.Ok(new { success = true, signedVoucher = await service.GetSignedAsync(voucherId, ct) }))
            .WithName("GetSignedVoucher")
            .WithSummary("Get the original signed voucher object");

        admin.MapGet("/{voucherId}/qr", async (string voucherId, VoucherService service, CancellationToken ct) =>
        {
            var png = await service.GetQrPngAsync(voucherId, ct);
            return Results.File(png, "image/png", $"mealseal-{voucherId}.png");
        }).WithName("DownloadVoucherQr")
            .WithSummary("Download a voucher's stored QR code as PNG")
            .Produces(StatusCodes.Status200OK, contentType: "image/png");

        admin.MapPost("/{voucherId}/revoke", async (string voucherId, VoucherService service,
            ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var record = await service.RevokeAsync(voucherId, Uid(principal), ct);
            return Results.Ok(new { success = true, status = record.Status, voucherId = record.VoucherId });
        }).WithName("RevokeVoucher")
            .WithSummary("Revoke an active voucher");

        var staff = app.MapGroup("/vouchers").RequireAuthorization(policy => policy.RequireRole("RESTAURANT_STAFF"))
            .RequireRateLimiting("api");
        staff.WithTags("Staff vouchers");

        staff.MapPost("/verify", async (SignedVoucherRequest request, VoucherService service,
            ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var record = await service.VerifyAsync(request.SignedVoucher, RestaurantId(principal), ct);
            return Results.Ok(new
            {
                success = true, valid = true, verified = true, status = record.Status,
                voucher = VoucherService.ToSummary(record)
            });
        }).WithName("VerifyVoucher")
            .WithSummary("Verify a voucher without changing its state");

        staff.MapPost("/redeem", async (SignedVoucherRequest request, VoucherService service,
            ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var record = await service.RedeemAsync(request.SignedVoucher, Uid(principal), RestaurantId(principal), ct);
            return Results.Ok(new
            {
                success = true, valid = true, verified = true, status = record.Status,
                voucher = VoucherService.ToSummary(record)
            });
        }).WithName("RedeemVoucher")
            .WithSummary("Verify and atomically redeem a voucher once");

        return app;
    }

    private static string Uid(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new ApiException(401, "UNAUTHORIZED", "User identity is missing.");

    private static string RestaurantId(ClaimsPrincipal principal) =>
        principal.FindFirstValue("restaurant_id")
        ?? throw new ApiException(403, "FORBIDDEN", "Restaurant staff profile has no restaurant assignment.");
}
