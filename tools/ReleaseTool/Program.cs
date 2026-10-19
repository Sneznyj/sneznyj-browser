using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SneznyjBrowser;

// Инструмент выпуска Снежного браузера (запускает release.ps1):
//   keygen   --key <pem>                                   создать ключ подписи (один раз)
//   pubkey   --key <pem>                                   открытый ключ для Core/Updater.cs
//   pack     --setup <Setup.exe> --app <SneznyjBrowser.exe> --out <установщик>
//   manifest --installer <exe> --version <x.y.z> --url <адрес> --key <pem> --out <update.json> [--notes <текст>] [--notes-file <файл>]
//   verify   --installer <exe> [--manifest <update.json> --pubkey <base64>]
Console.OutputEncoding = Encoding.UTF8;
try
{
    return args.FirstOrDefault() switch
    {
        "keygen" => Keygen(),
        "pubkey" => PubKey(),
        "pack" => Pack(),
        "manifest" => Manifest(),
        "verify" => Verify(),
        _ => Usage(),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine("Ошибка: " + ex.Message);
    return 1;
}

string Opt(string name, bool required = true)
{
    int i = Array.IndexOf(args, name);
    if (i >= 0 && i + 1 < args.Length) return args[i + 1];
    if (required) throw new ArgumentException($"нужен параметр {name}");
    return "";
}

int Usage()
{
    Console.WriteLine("Команды: keygen, pubkey, pack, manifest, verify (подробности в начале Program.cs)");
    return 2;
}

ECDsa LoadKey()
{
    var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(Opt("--key")));
    return key;
}

int Keygen()
{
    var path = Opt("--key");
    if (File.Exists(path)) throw new IOException($"ключ уже есть: {path}. Новый ключ сломает обновления у всех, кто уже поставил браузер.");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem());
    Console.WriteLine("Ключ сохранён: " + path);
    Console.WriteLine("Открытый ключ (вставить в Core/Updater.cs):");
    Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    return 0;
}

int PubKey()
{
    using var key = LoadKey();
    Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    return 0;
}

// Установщик = Setup.exe + сжатый LZMS SneznyjBrowser.exe + хвост 64 байта:
// "SNZPACK1", смещение, сжатый размер, исходный размер (по 8 байт), SHA-256 исходного файла.
int Pack()
{
    var setup = File.ReadAllBytes(Opt("--setup"));
    var app = File.ReadAllBytes(Opt("--app"));
    var outPath = Opt("--out");
    var hash = SHA256.HashData(app);
    var packed = Lzms.Compress(app);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
    using (var f = File.Create(outPath))
    {
        f.Write(setup);
        f.Write(packed);
        var tail = new byte[64];
        Encoding.ASCII.GetBytes("SNZPACK1").CopyTo(tail, 0);
        BitConverter.GetBytes((long)setup.Length).CopyTo(tail, 8);
        BitConverter.GetBytes((long)packed.Length).CopyTo(tail, 16);
        BitConverter.GetBytes((long)app.Length).CopyTo(tail, 24);
        hash.CopyTo(tail, 32);
        f.Write(tail);
    }
    Console.WriteLine($"Установщик: {outPath} — {new FileInfo(outPath).Length / 1048576.0:0.0} МБ (программа {app.Length / 1048576.0:0.0} МБ)");
    return 0;
}

int Manifest()
{
    var installer = Opt("--installer");
    var notesFile = Opt("--notes-file", false);
    var notes = notesFile.Length > 0 ? File.ReadAllText(notesFile).Trim() : Opt("--notes", false);
    var m = new UpdateManifest
    {
        Version = Opt("--version"),
        Url = Opt("--url"),
        Size = new FileInfo(installer).Length,
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(installer))),
        Notes = notes.Replace("\r\n", "\n"),
        Date = DateTime.Now.ToString("yyyy-MM-dd"),
    };
    if (m.ParsedVersion() == null) throw new ArgumentException("версия должна быть вида 0.2.0");
    using var key = LoadKey();
    m.Sign(key);
    var pub = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    if (!m.Verify(pub)) throw new CryptographicException("подпись не сошлась");
    File.WriteAllText(Opt("--out"), m.ToJson() + "\n", new UTF8Encoding(false));
    Console.WriteLine($"update.json: версия {m.Version}, {m.Size} байт, sha256 {m.Sha256[..16]}…");
    return 0;
}

int Verify()
{
    var installer = Opt("--installer");
    var bytes = File.ReadAllBytes(installer);
    var tail = bytes.AsSpan(bytes.Length - 64);
    if (Encoding.ASCII.GetString(tail[..8]) != "SNZPACK1") throw new InvalidDataException("в установщике нет программы");
    long offset = BitConverter.ToInt64(tail[8..16]), packedLen = BitConverter.ToInt64(tail[16..24]), appLen = BitConverter.ToInt64(tail[24..32]);
    var app = Lzms.Decompress(bytes.AsSpan((int)offset, (int)packedLen).ToArray(), appLen);
    if (!SHA256.HashData(app).AsSpan().SequenceEqual(tail[32..64])) throw new InvalidDataException("программа внутри установщика повреждена");
    Console.WriteLine($"Установщик цел: программа {appLen} байт распакована и сверена");

    var manifestPath = Opt("--manifest", false);
    if (manifestPath.Length == 0) return 0;
    var m = UpdateManifest.Parse(File.ReadAllText(manifestPath)) ?? throw new InvalidDataException("пустой update.json");
    if (!m.Verify(Opt("--pubkey"))) throw new CryptographicException("подпись update.json не подходит к ключу браузера");
    if (m.Size != bytes.Length || m.Sha256 != Convert.ToHexStringLower(SHA256.HashData(bytes)))
        throw new InvalidDataException("update.json описывает другой файл");
    Console.WriteLine($"update.json подписан верно: версия {m.Version}");
    return 0;
}

/// <summary>Сжатие LZMS из Windows (cabinet.dll): почти как 7-Zip, а распаковщик есть в любой Windows 8+.</summary>
static class Lzms
{
    const uint COMPRESS_ALGORITHM_LZMS = 5;

    [DllImport("cabinet.dll", SetLastError = true)] static extern bool CreateCompressor(uint algorithm, IntPtr routines, out IntPtr handle);
    [DllImport("cabinet.dll", SetLastError = true)] static extern bool Compress(IntPtr handle, byte[] src, nuint srcSize, byte[]? dst, nuint dstSize, out nuint size);
    [DllImport("cabinet.dll", SetLastError = true)] static extern bool CloseCompressor(IntPtr handle);
    [DllImport("cabinet.dll", SetLastError = true)] static extern bool CreateDecompressor(uint algorithm, IntPtr routines, out IntPtr handle);
    [DllImport("cabinet.dll", SetLastError = true)] static extern bool Decompress(IntPtr handle, byte[] src, nuint srcSize, byte[] dst, nuint dstSize, out nuint size);
    [DllImport("cabinet.dll", SetLastError = true)] static extern bool CloseDecompressor(IntPtr handle);

    public static byte[] Compress(byte[] data)
    {
        if (!CreateCompressor(COMPRESS_ALGORITHM_LZMS, IntPtr.Zero, out var h)) throw new IOException("LZMS недоступен");
        try
        {
            Compress(h, data, (nuint)data.Length, null, 0, out var need);
            var buffer = new byte[need];
            if (!Compress(h, data, (nuint)data.Length, buffer, need, out var size))
                throw new IOException("сжатие не удалось: " + Marshal.GetLastWin32Error());
            return buffer.AsSpan(0, (int)size).ToArray();
        }
        finally { CloseCompressor(h); }
    }

    public static byte[] Decompress(byte[] packed, long originalSize)
    {
        if (!CreateDecompressor(COMPRESS_ALGORITHM_LZMS, IntPtr.Zero, out var h)) throw new IOException("LZMS недоступен");
        try
        {
            var buffer = new byte[originalSize];
            if (!Decompress(h, packed, (nuint)packed.Length, buffer, (nuint)buffer.Length, out var size) || (long)size != originalSize)
                throw new IOException("распаковка не удалась: " + Marshal.GetLastWin32Error());
            return buffer;
        }
        finally { CloseDecompressor(h); }
    }
}
