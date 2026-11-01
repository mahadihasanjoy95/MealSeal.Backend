using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MealSeal.Api.Contracts;
using Microsoft.Extensions.Options;

namespace MealSeal.Api.Services;

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
        var bytes = SerializePayload(payload);
        var signature = _privateKey.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return new SignedVoucher(payload, Base64UrlEncode(signature), _options.Algorithm, _options.KeyId);
    }

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
