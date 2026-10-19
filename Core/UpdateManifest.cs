using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SneznyjBrowser;

/// <summary>
/// Файл версии update.json: какая версия последняя, где лежит установщик, его размер и SHA-256.
/// Всё это подписано ключом ECDSA P-256 — закрытый ключ есть только у того, кто выпускает версии,
/// открытый вшит в браузер. Подделать файл версии или подменить установщик нельзя.
/// Этот же файл подключён в tools/ReleaseTool, который подписывает выпуски.
/// </summary>
public sealed class UpdateManifest
{
    public string Product { get; set; } = "SneznyjBrowser";
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Date { get; set; } = "";
    public string Signature { get; set; } = "";

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static UpdateManifest? Parse(string json) => JsonSerializer.Deserialize<UpdateManifest>(json, Options);

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Что именно подписывается: все поля, кроме самой подписи, в строгом порядке.</summary>
    public byte[] SignedBytes() => Encoding.UTF8.GetBytes(string.Join("\n",
        "SneznyjBrowser update v1", Product, Version, Size.ToString(), Sha256.ToLowerInvariant(), Url, Notes, Date));

    public void Sign(ECDsa privateKey) =>
        Signature = Convert.ToBase64String(privateKey.SignData(SignedBytes(), HashAlgorithmName.SHA256));

    /// <summary>publicKey — SubjectPublicKeyInfo в base64.</summary>
    public bool Verify(string publicKey)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return key.VerifyData(SignedBytes(), Convert.FromBase64String(Signature), HashAlgorithmName.SHA256);
        }
        catch
        {
            return false;
        }
    }

    public System.Version? ParsedVersion() => System.Version.TryParse(Version, out var v) ? v : null;
}
