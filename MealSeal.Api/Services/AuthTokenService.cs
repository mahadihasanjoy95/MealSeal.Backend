using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using MealSeal.Api.Data;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MealSeal.Api.Services;

/// <summary>
/// Issues and verifies MealSeal's own access tokens (ES256-signed JWTs), replacing Firebase ID
/// tokens as the identity evidence presented to the API. Tokens only carry the user's ID —
/// role and restaurant assignment are re-read from the database on every request (see
/// <c>AccessTokenAuthenticationHandler</c>), so deactivating a user takes effect immediately
/// without needing a token-revocation list.
/// </summary>
public sealed class AuthTokenService
{
    private readonly string _privatePem;
    private readonly string _publicPem;
    private readonly AuthOptions _options;

    public AuthTokenService(IOptions<AuthOptions> options, IWebHostEnvironment environment)
    {
        _options = options.Value;
        _privatePem = ReadPem(_options.PrivateKeyPem, _options.PrivateKeyPath, environment.ContentRootPath);
        _publicPem = ReadPem(_options.PublicKeyPem, _options.PublicKeyPath, environment.ContentRootPath);

        // Fail fast at startup rather than on the first sign-in if the configured key is malformed.
        using var check = ECDsa.Create();
        check.ImportFromPem(_privatePem);
    }

    public string IssueAccessToken(UserProfile user)
    {
        var now = DateTime.UtcNow;

        // A fresh ECDsa per call, used and disposed entirely within this method — this service is
        // captured as a constructor dependency by AccessTokenAuthenticationHandler, and handler
        // instances can outlive a single logical request in ways a plain Scoped service doesn't;
        // caching a disposable ECDsa on an instance field intermittently threw
        // ObjectDisposedException under real traffic. PEM parsing is cheap enough to redo per call.
        using var privateKey = ECDsa.Create();
        privateKey.ImportFromPem(_privatePem);

        var signingKey = new ECDsaSecurityKey(privateKey) { KeyId = _options.KeyId };
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256)
        {
            // Microsoft.IdentityModel.Tokens caches signature providers process-wide, keyed by
            // KeyId + algorithm — NOT by object identity. Every call here uses the same constant
            // KeyId but a freshly created (and about-to-be-disposed) ECDsa instance, so the cache
            // was handing back a provider wrapping a PREVIOUS call's already-disposed key on
            // roughly every other request: exactly the "every other sign-in fails" symptom. A
            // factory with caching off forces a fresh provider — tied to this call's live key —
            // every time.
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserId),
            new Claim(
                JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(now).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(_options.AccessTokenLifetimeMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Returns the token's <c>sub</c> (user ID) if the signature, issuer, audience and
    /// expiry all check out; otherwise null.</summary>
    public string? ValidateAccessToken(string token)
    {
        using var publicKey = ECDsa.Create();
        publicKey.ImportFromPem(_publicPem);

        var verificationKey = new ECDsaSecurityKey(publicKey) { KeyId = _options.KeyId };

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = verificationKey,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            // Same reasoning as IssueAccessToken's CryptoProviderFactory above — without this,
            // validation can be handed a cached provider wrapping a disposed key from an earlier call.
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };

        try
        {
            // MapInboundClaims defaults to true, which silently rewrites the short "sub" claim
            // type to the long legacy ClaimTypes.NameIdentifier URI — without this, FindFirstValue
            // below would always return null.
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, parameters, out _);
            return principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static void GenerateKeys(string directory)
    {
        Directory.CreateDirectory(directory);
        var privatePath = Path.Combine(directory, "auth-private-key.pem");
        var publicPath = Path.Combine(directory, "auth-public-key.pem");
        if (File.Exists(privatePath) || File.Exists(publicPath))
            throw new InvalidOperationException("Auth key files already exist; refusing to overwrite them.");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(privatePath, key.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(publicPath, key.ExportSubjectPublicKeyInfoPem());
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(privatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static string ReadPem(string? inlinePem, string configuredPath, string root)
    {
        if (!string.IsNullOrWhiteSpace(inlinePem)) return inlinePem.Replace("\\n", "\n");
        var path = Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(root, configuredPath);
        if (!File.Exists(path)) throw new InvalidOperationException($"Auth signing key is missing: {path}");
        return File.ReadAllText(path);
    }
}
