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
public sealed class AuthTokenService : IDisposable
{
    private readonly ECDsa _privateKey = ECDsa.Create();
    private readonly ECDsa _publicKey = ECDsa.Create();
    private readonly AuthOptions _options;

    public AuthTokenService(IOptions<AuthOptions> options, IWebHostEnvironment environment)
    {
        _options = options.Value;
        var privatePem = ReadPem(_options.PrivateKeyPem, _options.PrivateKeyPath, environment.ContentRootPath);
        var publicPem = ReadPem(_options.PublicKeyPem, _options.PublicKeyPath, environment.ContentRootPath);
        _privateKey.ImportFromPem(privatePem);
        _publicKey.ImportFromPem(publicPem);
    }

    public string IssueAccessToken(UserProfile user)
    {
        var now = DateTime.UtcNow;
        var signingKey = new ECDsaSecurityKey(_privateKey) { KeyId = _options.KeyId };
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256);

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
        var verificationKey = new ECDsaSecurityKey(_publicKey) { KeyId = _options.KeyId };

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
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256]
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

    public void Dispose()
    {
        _privateKey.Dispose();
        _publicKey.Dispose();
    }
}
