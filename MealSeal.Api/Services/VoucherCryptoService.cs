using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MealSeal.Api.Contracts;
using Microsoft.Extensions.Options;

namespace MealSeal.Api.Services;

/// <summary>
/// SECURITY BOUNDARY: Creates and verifies the digital signature carried inside every MealSeal QR.
/// The QR data is not trusted merely because it can be scanned. ECDSA P-256 signs the exact UTF-8
/// bytes produced by <see cref="SerializePayload"/> using SHA-256. If any signed field is changed
/// after issue (recipient, café, meal, quantity, timestamps or issuer), verification fails because
/// the attacker cannot create a matching signature without the private key.
/// </summary>
public sealed class VoucherCryptoService : IDisposable
{
    private readonly ECDsa _privateKey = ECDsa.Create();
    private readonly ECDsa _publicKey = ECDsa.Create();
    private readonly MealSealOptions _options;

    public VoucherCryptoService(IOptions<MealSealOptions> options, IWebHostEnvironment environment)
    {
        _options = options.Value;
        var privatePem = ReadPem(_options.PrivateKeyPem, _options.PrivateKeyPath, environment.ContentRootPath);
        var publicPem = ReadPem(_options.PublicKeyPem, _options.PublicKeyPath, environment.ContentRootPath);
        _privateKey.ImportFromPem(privatePem);
        _publicKey.ImportFromPem(publicPem);
    }

    public SignedVoucher Sign(VoucherPayload payload)
    {
        // Only the backend receives the private key. The mobile app and the QR code receive the
        // signature, never the key that could create a new valid voucher.
        var bytes = SerializePayload(payload);
        var signature = _privateKey.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return new SignedVoucher(payload, Base64UrlEncode(signature), _options.Algorithm, _options.KeyId);
    }

    /// <summary>
    /// Authenticates a scanned voucher. False means the payload/signature is corrupted, was
    /// modified, uses an unsupported algorithm, or was signed by an unrecognised key.
    /// </summary>
    public bool Verify(SignedVoucher voucher)
    {
        if (voucher.Algorithm != _options.Algorithm || voucher.KeyId != _options.KeyId) return false;
        try
        {
            return _publicKey.VerifyData(SerializePayload(voucher.Payload), Base64UrlDecode(voucher.Signature),
                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public string CanonicalJson(VoucherPayload payload) => Encoding.UTF8.GetString(SerializePayload(payload));
    public string Sha256(VoucherPayload payload) => Convert.ToHexString(SHA256.HashData(SerializePayload(payload))).ToLowerInvariant();
    public string PublicKeyPem => _publicKey.ExportSubjectPublicKeyInfoPem();

    public static byte[] SerializePayload(VoucherPayload payload)
    {
        // Cryptographic signatures operate on bytes, not abstract JSON objects. Writing every
        // field in a fixed order produces one canonical byte representation and prevents property
        // ordering or formatting differences from changing what is signed and verified.
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
               { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", payload.Version);
            writer.WriteString("voucherId", payload.VoucherId);
            writer.WriteString("recipientRef", payload.RecipientRef);
            writer.WriteString("restaurantId", payload.RestaurantId);
            writer.WriteString("mealType", payload.MealType);
            writer.WriteNumber("quantity", payload.Quantity);
            writer.WriteNumber("issuedAt", payload.IssuedAt);
            writer.WriteNumber("expiresAt", payload.ExpiresAt);
            writer.WriteString("issuerUid", payload.IssuerUid);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static void GenerateKeys(string directory)
    {
        Directory.CreateDirectory(directory);
        var privatePath = Path.Combine(directory, "private-key.pem");
        var publicPath = Path.Combine(directory, "public-key.pem");
        if (File.Exists(privatePath) || File.Exists(publicPath))
            throw new InvalidOperationException("Key files already exist; refusing to overwrite them.");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(privatePath, key.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(publicPath, key.ExportSubjectPublicKeyInfoPem());
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(privatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static string ReadPem(string? inlinePem, string configuredPath, string root)
    {
        if (!string.IsNullOrWhiteSpace(inlinePem)) return inlinePem.Replace("\\n", "\n");
        var path = Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(root, configuredPath);
        if (!File.Exists(path)) throw new InvalidOperationException($"Signing key is missing: {path}");
        return File.ReadAllText(path);
    }

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Base64UrlDecode(string value)
    {
        var text = value.Replace('-', '+').Replace('_', '/');
        text += new string('=', (4 - text.Length % 4) % 4);
        return Convert.FromBase64String(text);
    }

    public void Dispose()
    {
        _privateKey.Dispose();
        _publicKey.Dispose();
    }
}
