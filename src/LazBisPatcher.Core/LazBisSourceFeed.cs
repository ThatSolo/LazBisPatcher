using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LazBisPatcher.Core;

public sealed record SourceFile(string Path, long Bytes, string GitBlob);
public sealed record LazBisSource(string Commit, SourceFile[] Files);

public static class LazBisSourceRules
{
    public static bool IsGitSha(string? value) => value is { Length: 40 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static Version VersionOf(string value)
    {
        if (!Regex.IsMatch(value, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(\.(0|[1-9][0-9]*))?\z") ||
            !Version.TryParse(value, out var version))
            throw new InvalidDataException("Cannot read meta.version: use a literal version such as '1.0' or '1.0.1' in init.lua.");
        return new(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    public static string ReadVersion(byte[] init)
    {
        string text;
        try { text = new UTF8Encoding(false, true).GetString(init); }
        catch (DecoderFallbackException e) { throw new InvalidDataException("Cannot read meta.version: init.lua is not valid UTF-8 text.", e); }
        var tokens = Tokens(text).GetEnumerator();
        using (tokens)
        {
            var prefix = new Queue<string>();
            while (tokens.MoveNext())
            {
                prefix.Enqueue(tokens.Current);
                if (prefix.Count > 4) prefix.Dequeue();
                if (!prefix.SequenceEqual(new[] { "local", "meta", "=", "{" })) continue;
                string? version = null;
                var depth = 1;
                while (tokens.MoveNext())
                {
                    var token = tokens.Current;
                    if (token == "{") depth++;
                    if (token == "}") depth--;
                    if (depth == 0)
                    {
                        if (version is null) break;
                        VersionOf(version);
                        return version;
                    }
                    if (depth != 1 || token != "version") continue;
                    if (version is not null || !tokens.MoveNext() || tokens.Current != "=" || !tokens.MoveNext()) break;
                    var literal = tokens.Current;
                    if (literal.Length < 3 || literal[0] is not ('\'' or '"') || literal[^1] != literal[0]) break;
                    version = literal[1..^1];
                    if (!tokens.MoveNext() || tokens.Current is not ("," or ";" or "}")) break;
                    if (tokens.Current == "}") { VersionOf(version); return version; }
                }
                break;
            }
        }
        throw new InvalidDataException("Cannot read meta.version from init.lua. Keep one literal version in the local meta table; no files were installed.");
    }

    private static IEnumerable<string> Tokens(string text)
    {
        for (var i = 0; i < text.Length;)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c) || c == '\uFEFF') { i++; continue; }
            var comment = c == '-' && i + 1 < text.Length && text[i + 1] == '-';
            if (comment) i += 2;
            if (i < text.Length && text[i] == '[')
            {
                var end = i + 1;
                while (end < text.Length && text[end] == '=') end++;
                if (end < text.Length && text[end] == '[')
                {
                    var close = "]" + text[(i + 1)..end] + "]";
                    var at = text.IndexOf(close, end + 1, StringComparison.Ordinal);
                    if (at < 0) throw new InvalidDataException("Unterminated Lua comment or string in init.lua.");
                    i = at + close.Length;
                    if (!comment) yield return "<long string>";
                    continue;
                }
            }
            if (comment)
            {
                while (i < text.Length && text[i] is not ('\r' or '\n')) i++;
                continue;
            }
            var start = i++;
            if (c is '\'' or '"')
            {
                var closed = false;
                while (i < text.Length)
                {
                    var next = text[i++];
                    if (next == '\\') { if (i < text.Length) i++; }
                    else if (next == c) { closed = true; break; }
                }
                if (!closed) throw new InvalidDataException("Unterminated Lua string in init.lua.");
            }
            else if (char.IsAsciiLetter(c) || c == '_')
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_')) i++;
            yield return text[start..i];
        }
    }

    public static string ManifestHash(LazBisSource source) => Files.Hash(Encoding.UTF8.GetBytes(
        source.Commit + "\n" + string.Join("\n", source.Files.OrderBy(f => f.Path, StringComparer.Ordinal)
            .Select(f => $"{f.Path}:{f.Bytes.ToString(CultureInfo.InvariantCulture)}:{f.GitBlob}"))));

    public static void Validate(Release release)
    {
        var source = release.Source!;
        if (release.Product != Product.LazBis || release.Version != VersionOf(release.Tag) ||
            release.Name != "lazbis.zip" || release.ReleaseId != 0 || release.AssetId != 0 || !IsGitSha(source.Commit) ||
            source.Files is null || source.Files.Length != LazBisPackage.Paths.Count ||
            source.Files.Any(f => f is null || !LazBisPackage.Paths.Contains(f.Path) || f.Bytes is < 1 or > 4_194_304 || !IsGitSha(f.GitBlob)) ||
            source.Files.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() != LazBisPackage.Paths.Count ||
            release.Bytes != source.Files.Sum(f => f.Bytes) || release.Bytes > ReleaseRules.Limit(Product.LazBis) ||
            release.Sha256 != ManifestHash(source))
            throw new InvalidDataException("LazBis commit or managed-file record is invalid. Keep the installation record for review.");
    }

    internal static string GitBlobHash(byte[] bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes($"blob {bytes.Length.ToString(CultureInfo.InvariantCulture)}\0"));
        hash.AppendData(bytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static void VerifyFile(SourceFile file, byte[] bytes)
    {
        if (bytes.LongLength != file.Bytes || GitBlobHash(bytes) != file.GitBlob)
            throw new InvalidDataException($"LazBis file verification failed for {file.Path} at the selected commit. No update was applied.");
    }
}

internal sealed class LazBisSourceFeed(GitHubFeed github)
{
    private const string Repository = "https://api.github.com/repos/ThatSolo/lazbis";
    internal const string GenericNotes = "MacroQuest can stay open. Before applying changes, run /lua stop lazbis on every character using it. If this copy runs the background helper, also run /lua stop lazbis/bg on those characters.";

    public async Task<Release> CheckAsync(CancellationToken token)
    {
        try { return await CheckCommitAsync(token); }
        catch (HttpRequestException e)
        {
            throw new HttpRequestException($"LazBis main source is unavailable. Your recorded installation was not changed. {e.Message}", e);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("Cannot read the selected LazBis commit's version or supported file list. No update was applied.", e);
        }
    }

    private async Task<Release> CheckCommitAsync(CancellationToken token)
    {
        using var response = await github.SendAsync(new(Repository + "/commits/main"), false, token);
        using var head = JsonDocument.Parse(await GitHubFeed.ReadAsync(response, 1 << 20, token));
        var commit = head.RootElement.GetProperty("sha").GetString();
        var tree = head.RootElement.GetProperty("commit").GetProperty("tree").GetProperty("sha").GetString();
        if (!LazBisSourceRules.IsGitSha(commit) || !LazBisSourceRules.IsGitSha(tree))
            throw new InvalidDataException("GitHub did not provide a valid LazBis main commit.");
        using var treeResponse = await github.SendAsync(new(Repository + $"/git/trees/{tree}?recursive=1"), false, token);
        using var listing = JsonDocument.Parse(await GitHubFeed.ReadAsync(treeResponse, 2 << 20, token));
        var root = listing.RootElement;
        if (root.GetProperty("sha").GetString() != tree || root.GetProperty("truncated").GetBoolean())
            throw new InvalidDataException("The selected LazBis commit's file list is incomplete. Ask the maintainer to review it.");
        var entries = root.GetProperty("tree").EnumerateArray().ToArray();
        var files = LazBisPackage.Paths.Select(path =>
        {
            var matches = entries.Where(e => e.GetProperty("path").GetString() == path).ToArray();
            if (matches.Length != 1 || matches[0].GetProperty("type").GetString() != "blob" ||
                matches[0].GetProperty("mode").GetString() is not ("100644" or "100755"))
                throw new InvalidDataException($"The selected LazBis commit is missing a supported regular file: {path}. Ask the maintainer to review the managed file list.");
            return new SourceFile(path, matches[0].GetProperty("size").GetInt64(), matches[0].GetProperty("sha").GetString() ?? "");
        }).ToArray();
        var source = new LazBisSource(commit!, files);
        // Validate sizes and inventory before any file download.
        var selected = new Release(Product.LazBis, "1.0", new(1, 0, 0), 0, 0, "lazbis.zip",
            files.Sum(f => f.Bytes), LazBisSourceRules.ManifestHash(source), "", source);
        ReleaseRules.Validate(selected);
        var init = await ReadFileAsync(source.Commit, files.Single(f => f.Path == "init.lua"), token);
        var version = LazBisSourceRules.ReadVersion(init);
        selected = selected with { Tag = version, Version = LazBisSourceRules.VersionOf(version),
            Notes = await ReadNotesAsync(source.Commit, entries, version, token) };
        return selected;
    }

    private async Task<string> ReadNotesAsync(string commit, JsonElement[] entries, string version, CancellationToken token)
    {
        var matches = entries.Where(e => e.GetProperty("path").GetString() == "CHANGELOG").ToArray();
        if (matches.Length != 1) return GenericNotes;
        try
        {
            var entry = matches[0];
            if (entry.GetProperty("type").GetString() != "blob" || entry.GetProperty("mode").GetString() is not ("100644" or "100755"))
                return GenericNotes;
            var file = new SourceFile("CHANGELOG", entry.GetProperty("size").GetInt64(), entry.GetProperty("sha").GetString() ?? "");
            if (file.Bytes is < 1 or > 65536 || !LazBisSourceRules.IsGitSha(file.GitBlob)) return GenericNotes;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(TimeSpan.FromSeconds(5));
            var bytes = await ReadFileAsync(commit, file, budget.Token);
            return LazBisChangelog.Entry(bytes, version) ?? GenericNotes;
        }
        // The version and managed files are already selected; optional notes cannot hide them.
        catch (OperationCanceledException) { return GenericNotes; }
        catch (Exception e) when (e is IOException or InvalidDataException or HttpRequestException or
            JsonException or KeyNotFoundException or InvalidOperationException or FormatException or DecoderFallbackException)
        {
            return GenericNotes;
        }
    }

    public async Task DownloadAsync(Release release, Stream destination, CancellationToken token)
    {
        var source = release.Source!;
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var file in source.Files)
        {
            var bytes = await ReadFileAsync(source.Commit, file, token);
            using var output = archive.CreateEntry("lazbis/" + file.Path).Open();
            await output.WriteAsync(bytes, token);
        }
    }

    private async Task<byte[]> ReadFileAsync(string commit, SourceFile file, CancellationToken token)
    {
        var path = string.Join('/', file.Path.Split('/').Select(Uri.EscapeDataString));
        try
        {
            using var response = await github.SendAsync(new($"https://raw.githubusercontent.com/ThatSolo/lazbis/{commit}/{path}"), false, token);
            var bytes = await GitHubFeed.ReadAsync(response, file.Bytes, token);
            LazBisSourceRules.VerifyFile(file, bytes);
            return bytes;
        }
        catch (HttpRequestException e)
        {
            throw new HttpRequestException($"LazBis source {file.Path} at commit {commit} is unavailable. Repair cannot switch to another commit. {e.Message}", e);
        }
    }
}

internal static class LazBisChangelog
{
    internal static string? Entry(byte[] bytes, string version)
    {
        var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        var headers = Regex.Matches(text,
            @"(?m)^[ \t]*(?:\#{1,6}[ \t]+)?\[?v?(?<version>[0-9]+\.[0-9]+(?:\.[0-9]+)?)\]?(?:[ \t]+[^\r\n]*)?[ \t]*\r?$");
        var matches = headers.Select((header, index) => (header, index))
            .Where(pair => pair.header.Groups["version"].Value == version).ToArray();
        if (matches.Length != 1) return null;
        var (match, position) = matches[0];
        var end = position + 1 < headers.Count ? headers[position + 1].Index : text.Length;
        var body = text[(match.Index + match.Length)..end].Trim();
        if (body.Length == 0) return null;
        var note = text[match.Index..end].Trim();
        return note[..Math.Min(note.Length, 16000)];
    }
}
