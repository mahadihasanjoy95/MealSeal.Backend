using System.Security.Cryptography;
using MealSeal.Api;
using MealSeal.Api.Data;
using MealSeal.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Xunit;

namespace MealSeal.Api.Tests;

public sealed class AuthServicesTests
{
    [Fact]
    public void PasswordService_VerifiesCorrectPassword_AndRejectsWrongPassword()
    {
        var passwords = new PasswordService();
        var user = new UserProfile
        {
            UserId = "user-1",
            Email = "admin@mealseal.test",
            PasswordHash = string.Empty,
            Role = "CHARITY_ADMIN"
        };

        user.PasswordHash = passwords.Hash(user, "correct horse battery staple");

        Assert.True(passwords.Verify(user, "correct horse battery staple"));
        Assert.False(passwords.Verify(user, "wrong password"));
    }

    [Fact]
    public void AuthTokenService_IssuesTokenThatValidatesBackToTheSameUserId()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = Options.Create(new AuthOptions
        {
            Issuer = "mealseal-api",
            Audience = "mealseal-app",
            AccessTokenLifetimeMinutes = 60,
            PrivateKeyPem = key.ExportPkcs8PrivateKeyPem(),
            PublicKeyPem = key.ExportSubjectPublicKeyInfoPem()
        });

        var tokens = new AuthTokenService(options, new TestEnvironment());
        var user = new UserProfile
        {
            UserId = "user-42",
            Email = "staff@mealseal.test",
            PasswordHash = "irrelevant-for-this-test",
            Role = "RESTAURANT_STAFF"
        };

        var token = tokens.IssueAccessToken(user);
        var validatedUserId = tokens.ValidateAccessToken(token);

        Assert.Equal("user-42", validatedUserId);
    }

    [Fact]
    public void AuthTokenService_RejectsTokenSignedByADifferentKey()
    {
        using var key1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var key2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var issuerOptions = Options.Create(new AuthOptions
        {
            PrivateKeyPem = key1.ExportPkcs8PrivateKeyPem(),
            PublicKeyPem = key1.ExportSubjectPublicKeyInfoPem()
        });
        var verifierOptions = Options.Create(new AuthOptions
        {
            // Same issuer/audience, but a public key that does NOT match the signer — a forged
            // or tampered token should fail verification.
            PrivateKeyPem = key2.ExportPkcs8PrivateKeyPem(),
            PublicKeyPem = key2.ExportSubjectPublicKeyInfoPem()
        });

        var issuer = new AuthTokenService(issuerOptions, new TestEnvironment());
        var verifier = new AuthTokenService(verifierOptions, new TestEnvironment());

        var token = issuer.IssueAccessToken(new UserProfile
        {
            UserId = "user-1",
            Email = "admin@mealseal.test",
            PasswordHash = "irrelevant-for-this-test",
            Role = "CHARITY_ADMIN"
        });

        Assert.Null(verifier.ValidateAccessToken(token));
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
