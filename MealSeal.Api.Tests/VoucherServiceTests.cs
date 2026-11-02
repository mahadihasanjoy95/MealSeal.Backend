using System.Security.Cryptography;
using MealSeal.Api;
using MealSeal.Api.Contracts;
using MealSeal.Api.Data;
using MealSeal.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Xunit;

namespace MealSeal.Api.Tests;

public sealed class VoucherServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private MealSealDbContext _db = null!;
    private VoucherCryptoService _crypto = null!;
    private VoucherService _service = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<MealSealDbContext>().UseSqlite(_connection).Options;
        _db = new MealSealDbContext(dbOptions);
        await _db.Database.EnsureCreatedAsync();

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = Options.Create(new MealSealOptions
        {
            PrivateKeyPem = key.ExportPkcs8PrivateKeyPem(),
            PublicKeyPem = key.ExportSubjectPublicKeyInfoPem()
        });
        _crypto = new VoucherCryptoService(options, new TestEnvironment());
        _service = new VoucherService(_db, _crypto);
    }

    public async Task DisposeAsync()
    {
        _crypto.Dispose();
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Voucher_IsSignedVerifiedRedeemedOnce_AndTamperingFails()
    {
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd");
        var signed = await _service.CreateAsync(
            new CreateVoucherRequest("recipient-42", "restaurant-1", "Lunch", 2, expiry), "admin-uid", default);

        var verified = await _service.VerifyAsync(signed, "restaurant-1", default);
        Assert.Equal("ACTIVE", verified.Status);

        var redeemed = await _service.RedeemAsync(signed, "staff-uid", "restaurant-1", default);
        Assert.Equal("REDEEMED", redeemed.Status);

        var replay = await Assert.ThrowsAsync<ApiException>(() =>
            _service.RedeemAsync(signed, "staff-uid", "restaurant-1", default));
        Assert.Equal("ALREADY_REDEEMED", replay.Code);

        var tampered = signed with { Payload = signed.Payload with { Quantity = 19 } };
        var invalid = await Assert.ThrowsAsync<ApiException>(() =>
            _service.VerifyAsync(tampered, "restaurant-1", default));
        Assert.Equal("INVALID_SIGNATURE", invalid.Code);
    }

    [Fact]
    public async Task Voucher_CannotBeVerifiedByAnotherRestaurant_AndCanBeRevoked()
    {
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd");
        var signed = await _service.CreateAsync(
            new CreateVoucherRequest("recipient-7", "restaurant-1", "Dinner", 1, expiry), "admin-uid", default);

        var wrongRestaurant = await Assert.ThrowsAsync<ApiException>(() =>
            _service.VerifyAsync(signed, "restaurant-2", default));
        Assert.Equal("WRONG_RESTAURANT", wrongRestaurant.Code);

        var revoked = await _service.RevokeAsync(signed.Payload.VoucherId, "admin-uid", default);
        Assert.Equal("REVOKED", revoked.Status);

        var rejected = await Assert.ThrowsAsync<ApiException>(() =>
            _service.VerifyAsync(signed, "restaurant-1", default));
        Assert.Equal("REVOKED", rejected.Code);
    }

    [Fact]
    public async Task Voucher_RejectsEveryChangedSignedField_AndChangedSignature()
    {
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd");
        var signed = await _service.CreateAsync(
            new CreateVoucherRequest("recipient-42", "restaurant-1", "Lunch", 2, expiry), "admin-uid", default);

        var changedSignature = signed.Signature[..^1] + (signed.Signature[^1] == 'A' ? "B" : "A");
        SignedVoucher[] tamperedVouchers =
        [
            signed with { Payload = signed.Payload with { VoucherId = Guid.NewGuid().ToString() } },
            signed with { Payload = signed.Payload with { RecipientRef = "recipient-99" } },
            signed with { Payload = signed.Payload with { RestaurantId = "restaurant-2" } },
            signed with { Payload = signed.Payload with { MealType = "Dinner" } },
            signed with { Payload = signed.Payload with { Quantity = 19 } },
            signed with { Payload = signed.Payload with { IssuedAt = signed.Payload.IssuedAt + 1 } },
            signed with { Payload = signed.Payload with { ExpiresAt = signed.Payload.ExpiresAt + 1 } },
            signed with { Payload = signed.Payload with { IssuerUid = "attacker" } },
            signed with { Signature = changedSignature },
            signed with { Algorithm = "none" },
            signed with { KeyId = "attacker-key" }
        ];

        foreach (var tampered in tamperedVouchers)
        {
            var rejected = await Assert.ThrowsAsync<ApiException>(() =>
                _service.VerifyAsync(tampered, "restaurant-1", default));
            Assert.Equal("INVALID_SIGNATURE", rejected.Code);
        }
    }

    [Fact]
    public async Task Voucher_RejectsAnIssuedRecordWithAnInconsistentStoredHash()
    {
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd");
        var signed = await _service.CreateAsync(
            new CreateVoucherRequest("recipient-42", "restaurant-1", "Lunch", 2, expiry), "admin-uid", default);

        var stored = await _db.Vouchers.SingleAsync(x => x.VoucherId == signed.Payload.VoucherId);
        stored.PayloadSha256 = new string('0', 64);
        await _db.SaveChangesAsync();

        var rejected = await Assert.ThrowsAsync<ApiException>(() =>
            _service.VerifyAsync(signed, "restaurant-1", default));
        Assert.Equal("INVALID_VOUCHER", rejected.Code);
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "MealSeal.Api.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
