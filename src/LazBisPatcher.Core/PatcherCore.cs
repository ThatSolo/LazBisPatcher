using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace LazBisPatcher.Core;

public enum Product { LazBis, Patcher }
public sealed record Release(Product Product, string Tag, Version Version, long ReleaseId, long AssetId,
    string Name, long Bytes, string Sha256, string Notes, LazBisSource? Source = null);
public interface IReleaseFeed
{
    Task<Release> CheckAsync(Product product, CancellationToken token);
    Task DownloadAsync(Release release, Stream destination, CancellationToken token);
}

public static class ReleaseRules
{
    public const string PatcherAsset = "LazBisPatcher.exe";
    public static Version VersionOf(string tag, Product product)
    {
        string value = tag.StartsWith('v') ? tag[1..] : tag;
        var parts = value.Split('.');
        if (parts.Length != 3 && !(product == Product.LazBis && tag == "1.0"))
            throw new InvalidDataException($"Unsupported release tag '{tag}'. Use vMAJOR.MINOR.PATCH.");
        if (parts.Any(p => p.Length == 0 || p.Length > 1 && p[0] == '0' || p.Any(c => c is < '0' or > '9'))
            || !Version.TryParse(value, out var parsed))
            throw new InvalidDataException($"Invalid release version '{tag}'.");
        return new(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
    }
    public static string NameOf(Product product) => product == Product.LazBis ? "lazbis.zip" : PatcherAsset;
    public static long Limit(Product product) => product == Product.LazBis ? 16L << 20 : 160L << 20;

    public static void Validate(Release release)
    {
        if (release.Source is not null) { LazBisSourceRules.Validate(release); return; }
        if (release.Version != VersionOf(release.Tag, release.Product) || release.ReleaseId <= 0 ||
            release.AssetId <= 0 || release.Name != NameOf(release.Product) || release.Bytes <= 0 ||
            release.Bytes > Limit(release.Product) || !Files.IsHash(release.Sha256))
            throw new InvalidDataException("Release metadata is inconsistent. Check the tag, uploaded asset, length and SHA-256.");
    }
}

public sealed class GitHubFeed : IReleaseFeed
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    private readonly string patcherRepository;
    private readonly HttpClient client;
    public GitHubFeed(string patcherRepository) : this(patcherRepository, Client) { }
    internal GitHubFeed(string patcherRepository, HttpClient client)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(patcherRepository, @"\A[A-Za-z0-9_-]{1,39}/[A-Za-z0-9_.-]{1,100}\z") ||
            patcherRepository.Split('/')[1] is "." or "..")
            throw new InvalidDataException("The patcher publisher must be a GitHub owner/repository.");
        this.patcherRepository = patcherRepository;
        this.client = client;
    }
    private string Repository(Product product) => product == Product.LazBis ? "ThatSolo/lazbis" : patcherRepository;

    public async Task<Release> CheckAsync(Product product, CancellationToken token)
    {
        if (product == Product.LazBis) return await new LazBisSourceFeed(this).CheckAsync(token);
        var repository = Repository(product);
        using var response = await SendAsync(new($"https://api.github.com/repos/{repository}/releases/latest"), false, token);
        var json = await ReadAsync(response, 1 << 20, token);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("Publish a stable release before checking for updates.");
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var assets = root.GetProperty("assets").EnumerateArray().Where(a =>
            a.GetProperty("name").GetString() == ReleaseRules.NameOf(product)).ToArray();
        if (assets.Length != 1 || assets[0].GetProperty("state").GetString() != "uploaded")
            throw new InvalidDataException($"Release {tag} needs one fully uploaded {ReleaseRules.NameOf(product)} asset.");
        var asset = assets[0];
        var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
        if (digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal))
            throw new InvalidDataException($"Release {tag} is missing the asset SHA-256. Publish a new release with the complete asset.");
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        var release = new Release(product, tag, ReleaseRules.VersionOf(tag, product), root.GetProperty("id").GetInt64(),
            asset.GetProperty("id").GetInt64(), ReleaseRules.NameOf(product), asset.GetProperty("size").GetInt64(),
            digest[7..].ToLowerInvariant(), notes[..Math.Min(notes.Length, 16000)]);
        ReleaseRules.Validate(release);
        return release;
    }

    public async Task DownloadAsync(Release release, Stream destination, CancellationToken token)
    {
        ReleaseRules.Validate(release);
        if (release.Source is not null)
        {
            await new LazBisSourceFeed(this).DownloadAsync(release, destination, token);
            return;
        }
        var repository = Repository(release.Product);
        var url = new Uri($"https://api.github.com/repos/{repository}/releases/assets/{release.AssetId}");
        for (int redirect = 0; redirect < 3; redirect++)
        {
            using var response = await SendAsync(url, true, token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                url = response.Headers.Location ?? throw new InvalidDataException("GitHub asset redirect is missing.");
                if (!url.IsAbsoluteUri || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 ||
                    url.Host is not ("release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                    throw new InvalidDataException("GitHub asset redirected to an unexpected host.");
                continue;
            }
            await using var body = await response.Content.ReadAsStreamAsync(token);
            await Files.CopyBoundedAsync(body, destination, release.Bytes, token);
            return;
        }
        throw new InvalidDataException("GitHub asset redirected too many times.");
    }

    internal async Task<HttpResponseMessage> SendAsync(Uri url, bool asset, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("LazBisPatcher");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(asset ? "application/octet-stream" : "application/vnd.github+json"));
        if (url.Host == "api.github.com") request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.IsSuccessStatusCode || asset && (int)response.StatusCode is >= 300 and < 400) return response;
        var status = response.StatusCode;
        response.Dispose();
        throw new HttpRequestException(status switch
        {
            HttpStatusCode.NotFound => "The release or asset is unavailable. Check the publisher repository and published release.",
            HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests => "GitHub refused or rate-limited the check. Try again later.",
            _ => $"GitHub returned HTTP {(int)status}. Try checking again."
        });
    }

    internal static async Task<byte[]> ReadAsync(HttpResponseMessage response, long maximum, CancellationToken token)
    {
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        await Files.CopyBoundedAsync(input, output, maximum, token);
        return output.ToArray();
    }
}

public sealed record ManagedFile(string Path, long Bytes, string Sha256);

public static class LazBisPackage
{
    private static readonly IReadOnlyDictionary<string, ManagedFile> Baseline = LoadBaseline();
    public static IReadOnlyCollection<string> Paths => Baseline.Keys.ToArray();

    private static IReadOnlyDictionary<string, ManagedFile> LoadBaseline()
    {
        using var resource = typeof(LazBisPackage).Assembly.GetManifestResourceStream("LazBisPatcher.Core.LazBis10.json")!;
        return JsonSerializer.Deserialize<ManagedFile[]>(resource, Files.Json)!.ToDictionary(f => f.Path, StringComparer.Ordinal);
    }

    public static Dictionary<string, byte[]> Read(Stream zip, Release release)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > 32) throw new InvalidDataException("LazBis ZIP has too many entries.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (!paths.Add(name)) throw new InvalidDataException("Duplicate ZIP path.");
            var mode = (entry.ExternalAttributes >> 16) & 0xf000;
            if ((entry.ExternalAttributes & 0x400) != 0 ||
                (name.EndsWith('/') ? mode is not (0 or 0x4000) : mode is not (0 or 0x8000)))
                throw new InvalidDataException("Unsupported link or special file in LazBis ZIP.");
            if (name is "lazbis/" or "lazbis/images/")
            {
                if (entry.Length != 0) throw new InvalidDataException("ZIP directory contains data.");
                continue;
            }
            if (!name.StartsWith("lazbis/", StringComparison.Ordinal) || !Baseline.TryGetValue(name[7..], out var expected))
                throw new InvalidDataException($"Unreviewed file in LazBis ZIP: {name}. Ask the maintainer to review the file list.");
            if (entry.Length is < 1 or > 4_194_304 || (total += entry.Length) > ReleaseRules.Limit(Product.LazBis))
                throw new InvalidDataException("LazBis ZIP exceeds extraction limits.");
            using var input = entry.Open();
            var bytes = new byte[checked((int)entry.Length)];
            input.ReadExactly(bytes);
            if (input.ReadByte() != -1) throw new InvalidDataException("LazBis ZIP entry exceeds its declared size.");
            if (release.Source is { } source)
            {
                var file = source.Files.Single(f => f.Path == expected.Path);
                LazBisSourceRules.VerifyFile(file, bytes);
            }
            else if (release.Tag == "1.0" && (bytes.Length != expected.Bytes || Files.Hash(bytes) != expected.Sha256))
                throw new InvalidDataException("LazBis 1.0 differs from the reviewed release. Publish changes with a new tag.");
            result.Add(expected.Path, bytes);
        }
        if (result.Count != Baseline.Count) throw new InvalidDataException("LazBis ZIP is missing files from the reviewed inventory.");
        if (release.Source is not null && LazBisSourceRules.ReadVersion(result["init.lua"]) != release.Tag)
            throw new InvalidDataException("The selected commit's meta.version differs from the checked version. Check again.");
        return result;
    }
}

public static class Files
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string HashFile(string path, long limit = 160L << 20)
    {
        Guard(path);
        using var input = File.OpenRead(path);
        if (input.Length > limit) throw new InvalidDataException("File exceeds the preview limit.");
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }
    public static void Guard(string path)
    {
        for (string? part = Path.GetFullPath(path); part is not null; part = Path.GetDirectoryName(part))
        {
            try
            {
                var attributes = File.GetAttributes(part);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("A linked or redirected path requires manual review.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    public static byte[] Read(string path, int maximum)
    {
        Guard(path);
        using var stream = File.OpenRead(path);
        if (stream.Length > maximum) throw new InvalidDataException("File exceeds the preview limit.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new IOException("File changed during read.");
        return bytes;
    }
    public static T ReadJson<T>(string path, int maximum = 65536) =>
        JsonSerializer.Deserialize<T>(Read(path, maximum), Json) ?? throw new InvalidDataException("Missing state data.");
    public static void WriteJson<T>(string path, T value) => Write(path, JsonSerializer.SerializeToUtf8Bytes(value, Json));
    public static void Write(string path, byte[] data, bool replace = true)
    {
        Guard(path);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, ".lazbis-write-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(data);
                output.Flush(true);
            }
            File.Move(temp, path, replace);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static FileStream Lock(string path)
    {
        Guard(path);
        return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public static async Task CopyBoundedAsync(Stream source, Stream destination, long limit, CancellationToken token)
    {
        var buffer = new byte[65536];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, token)) != 0)
        {
            total += count;
            if (total > limit) throw new InvalidDataException("Download exceeds its declared size.");
            await destination.WriteAsync(buffer.AsMemory(0, count), token);
        }
    }
    public static async Task StageAsync(IReleaseFeed feed, Release release, string path, CancellationToken token)
    {
        ReleaseRules.Validate(release);
        Guard(path);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromMinutes(3));
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await feed.DownloadAsync(release, output, budget.Token);
            if (release.Source is null && output.Length != release.Bytes)
                throw new InvalidDataException("Download length does not match the selected release.");
            if (release.Source is not null && output.Length > ReleaseRules.Limit(Product.LazBis) + (1 << 20))
                throw new InvalidDataException("LazBis source package exceeds the size limit.");
            output.Flush(true);
        }
        if (release.Source is null && HashFile(path) != release.Sha256)
            throw new InvalidDataException("Download SHA-256 does not match the selected release.");
        if (release.Source is not null)
        {
            using var archive = File.OpenRead(path);
            LazBisPackage.Read(archive, release);
        }
    }
}
