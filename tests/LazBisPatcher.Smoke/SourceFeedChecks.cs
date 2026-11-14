using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LazBisPatcher.Core;

internal static class SourceFeedChecks
{
    internal static async Task RunAsync(byte[] legacyZip, Dictionary<string, byte[]> original, string fixture)
    {
        using var handler = new SourceHandler(legacyZip);
        using var client = new HttpClient(handler);
        var feed = new GitHubFeed("ThatSolo/LazBisPatcher", client);
        var first = new string('a', 40);
        var second = new string('b', 40);
        var noBump = new string('c', 40);
        var later = new string('d', 40);
        var invalid = new string('e', 40);
        handler.Commits[first] = original;
        var updated = original.ToDictionary();
        updated["init.lua"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original["init.lua"])
            .Replace("version = '1.0'", "version = '1.0.1'", StringComparison.Ordinal));
        updated["bg.lua"] = [.. original["bg.lua"], .. "\n-- version 1.0.1\n"u8];
        handler.Commits[second] = updated;
        handler.Commits[noBump] = updated.ToDictionary();
        handler.Commits[noBump]["bg.lua"] = [.. updated["bg.lua"], .. "\n-- unversioned push\n"u8];
        handler.Commits[later] = updated.ToDictionary();
        handler.Commits[later]["init.lua"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(updated["init.lua"])
            .Replace("version = '1.0.1'", "version = '1.0.2'", StringComparison.Ordinal));
        handler.Commits[invalid] = original.ToDictionary();
        handler.Commits[invalid]["init.lua"] = "-- local meta = {version = '9.9'}\nlocal meta = {version = getVersion()}"u8.ToArray();
        handler.Notes[first] = "1.0 (official)\n- official first notes\n\n3.5.0\n- unrelated upstream notes"u8.ToArray();
        handler.Notes[second] = "1.0.1\n- intended update notes\n\n1.0\n- previous notes"u8.ToArray();
        handler.Notes[later] = "1.0.2\n- later branch notes"u8.ToArray();

        Require(LazBisSourceRules.ReadVersion("--[=[local meta={version='9.9'}]=]\nlocal meta={name='x', version=\"1.0.1\"}"u8.ToArray()) == "1.0.1", "literal parser skips comments");
        await Refuses(() => Task.Run(() => LazBisSourceRules.ReadVersion("local meta={version='1.0' .. '.1'}"u8.ToArray())), "meta.version");
        var root = CreateRoot(fixture, "SourceInstall");
        var sentinels = AddSentinels(root);
        var installer = new LazBisInstaller(root);
        handler.Main = first;
        var v1 = await feed.CheckAsync(Product.LazBis, default);
        Require(v1.Tag == "1.0" && v1.Version == new Version(1, 0, 0) && v1.Source?.Commit == first, "main 1.0 identity");
        Require(v1.Notes.Contains("official first notes", StringComparison.Ordinal) && !v1.Notes.Contains("unrelated", StringComparison.Ordinal), "matching plain changelog section only");
        (await installer.PrepareAsync(feed, v1, default)).Apply();
        Require(installer.Inspect()?.Hashes.Count == 13, "source install ownership");

        handler.Main = second;
        handler.MoveMainOnNotes = later;
        var v2 = await feed.CheckAsync(Product.LazBis, default);
        handler.MoveMainOnNotes = null;
        Require(v2.Tag == "1.0.1" && v2.Version == new Version(1, 0, 1), "main version bump");
        Require(handler.Main == later && v2.Source?.Commit == second && v2.Notes.Contains("intended update notes", StringComparison.Ordinal), "notes stay on selected commit while main moves");
        Require(LazBisInstaller.SelectForInstall(v2, installer.Inspect()) == v2, "version bump offers update");
        handler.Main = later;
        var downloadStart = handler.Requests.Count;
        var plan = await installer.PrepareAsync(feed, v2, default);
        Require(handler.Requests.Skip(downloadStart).Count() == 13 &&
            handler.Requests.Skip(downloadStart).All(p => p.StartsWith($"https://raw.githubusercontent.com/ThatSolo/lazbis/{second}/", StringComparison.Ordinal)),
            "download never re-resolves moving main");
        await Refuses(() => Task.Run(() => plan.ApplyWithProbe(_ => throw new IOException("generated source write failure"))), "Previous files restored");
        Require(installer.Inspect()?.Release.Source?.Commit == first, "rollback keeps old provenance");
        VerifyPayload(root, original);
        plan.Apply();
        VerifyPayload(root, updated);
        VerifySentinels(sentinels);
        Console.WriteLine("PASS main 1.0 -> 1.0.1, branch moves before download, pinned 13-file update, write-failure rollback and player-file preservation");

        var installed = installer.Inspect()!;
        var statePath = Path.Combine(root, "lua", ".lazbis-patcher", "installed.json");
        var before = File.ReadAllBytes(statePath);
        handler.Main = noBump;
        var sameVersion = await feed.CheckAsync(Product.LazBis, default);
        var selected = LazBisInstaller.SelectForInstall(sameVersion, installed);
        Require(selected.Source?.Commit == second && LazBisInstaller.SameAsset(selected, installed.Release), "no bump selects recorded repair source");
        Require(File.ReadAllBytes(statePath).SequenceEqual(before), "check leaves state untouched");
        VerifyPayload(root, updated);
        await Refuses(() => installer.PrepareAsync(feed, sameVersion, default), "changes an existing version");
        File.Delete(Path.Combine(root, "lua", "lazbis", "bg.lua"));
        (await installer.PrepareAsync(feed, selected, default)).Apply();
        VerifyPayload(root, updated);
        Require(installer.Inspect()?.Release.Source?.Commit == second, "repair does not follow unversioned push");
        VerifySentinels(sentinels);
        File.AppendAllText(Path.Combine(root, "lua", "lazbis", "init.lua"), "\n-- player edit");
        var conflict = await installer.PrepareAsync(feed, selected, default);
        Require(conflict.Preview.Changes.Any(c => c.Path == "init.lua" && c.Action == "Conflict"), "source local-edit conflict");
        await Refuses(() => Task.Run(conflict.Apply), "Existing files differ");
        Console.WriteLine("PASS push without bump offers no update; repair stays on recorded commit and preserves local-edit conflicts");

        before = File.ReadAllBytes(statePath);
        handler.Main = invalid;
        await Refuses(() => feed.CheckAsync(Product.LazBis, default), "meta.version");
        handler.MainUnavailable = true;
        await Refuses(() => feed.CheckAsync(Product.LazBis, default), "main source is unavailable");
        Require((await installer.PrepareAsync(feed, selected, default)).Preview.Release.Source?.Commit == second,
            "recorded repair does not need main to be available");
        handler.MainUnavailable = false;
        handler.Main = second;
        handler.LinkedFile = true;
        await Refuses(() => feed.CheckAsync(Product.LazBis, default), "regular file");
        handler.LinkedFile = false;
        handler.Unavailable = second;
        await Refuses(() => installer.PrepareAsync(feed, selected, default), "Repair cannot switch");
        handler.Unavailable = null;
        handler.CorruptPath = "bg.lua";
        await Refuses(() => installer.PrepareAsync(feed, selected, default), "verification failed");
        handler.CorruptPath = null;
        Require(File.ReadAllBytes(statePath).SequenceEqual(before), "failed reads preserve state");
        VerifySentinels(sentinels);
        Console.WriteLine("PASS unreadable version, unavailable pinned source and corrupt blob fail clearly before installation");

        handler.Main = second;
        foreach (var kind in new[] { "missing-file", "missing-entry", "bad-blob", "unavailable", "oversize" })
        {
            var notes = handler.Notes[second];
            if (kind == "missing-file") handler.Notes.Remove(second);
            if (kind == "missing-entry") handler.Notes[second] = "9.9.9\n- no matching version"u8.ToArray();
            handler.CorruptPath = kind == "bad-blob" ? "CHANGELOG" : null;
            handler.NotesUnavailable = kind == "unavailable";
            handler.OversizeNotes = kind == "oversize";
            var available = await feed.CheckAsync(Product.LazBis, default);
            Require(available.Version == new Version(1, 0, 1) && available.Source?.Commit == second &&
                available.Notes == LazBisSourceFeed.GenericNotes && available.Source.Files.Length == 13, "optional notes fallback " + kind);
            handler.Notes[second] = notes;
            handler.CorruptPath = null;
            handler.NotesUnavailable = handler.OversizeNotes = false;
        }
        Require(!LazBisPackage.Paths.Contains("CHANGELOG"), "notes never become installed ownership");
        using (var cancelledNotes = new CancellationTokenSource())
        {
            handler.CancelOnNotes = cancelledNotes;
            var available = await feed.CheckAsync(Product.LazBis, cancelledNotes.Token);
            Require(cancelledNotes.IsCancellationRequested && available.Notes == LazBisSourceFeed.GenericNotes && available.Version == new Version(1, 0, 1),
                "cancellation during optional notes keeps verified update available");
            handler.CancelOnNotes = null;
        }
        Console.WriteLine("PASS verified matching CHANGELOG entry, moved-main note pin, missing file/entry, bad blob, unavailable or oversized notes fall back without blocking valid update");

        // Reproduce an old JSON record with no source field, not a new source install.
        var legacyRoot = CreateRoot(fixture, "LegacyRecord");
        var legacy = new LazBisInstaller(legacyRoot);
        var legacyRelease = LegacyRelease(legacyZip);
        (await legacy.PrepareAsync(new MemoryFeed(legacyZip), legacyRelease, default)).Apply();
        var legacyStatePath = Path.Combine(legacyRoot, "lua", ".lazbis-patcher", "installed.json");
        var json = JsonNode.Parse(File.ReadAllText(legacyStatePath))!;
        json["release"]!.AsObject().Remove("source");
        File.WriteAllText(legacyStatePath, json.ToJsonString());
        await CheckLegacyRepairAsync(feed, handler, legacyRoot, legacyZip, original, fixture);
        var earlierBuildRoot = Environment.GetEnvironmentVariable("LAZBIS_LEGACY_FIXTURE_ROOT");
        if (!string.IsNullOrEmpty(earlierBuildRoot))
        {
            await CheckLegacyRepairAsync(feed, handler, earlierBuildRoot, legacyZip, original, fixture);
            Console.WriteLine("PASS repair on a disposable copy installed by the earlier packaged build's core");
        }
        var patcher = await feed.CheckAsync(Product.Patcher, default);
        using var patcherDownload = new MemoryStream();
        await feed.DownloadAsync(patcher, patcherDownload, default);
        Require(patcher.Source is null && Files.Hash(patcherDownload.ToArray()) == patcher.Sha256,
            "patcher still uses stable GitHub Release asset and digest");
        Require(handler.OpenBodies == 0, "all HTTP response bodies disposed");
        Console.WriteLine("PASS legacy release record and file ownership retained; original asset verifies repair; no source fallback; HTTP bodies closed");
    }

    private static async Task CheckLegacyRepairAsync(GitHubFeed feed, SourceHandler handler, string sourceRoot,
        byte[] zip, Dictionary<string, byte[]> original, string fixture)
    {
        var root = Path.Combine(fixture, "LegacyCopy-" + Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(root, Path.GetRelativePath(sourceRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        var sentinels = AddSentinels(root);
        var installer = new LazBisInstaller(root);
        var state = installer.Inspect()!;
        Require(state.Release.Source is null && state.Release.Tag == "1.0", "old release-based record readable");
        handler.Main = new string('a', 40);
        var available = await feed.CheckAsync(Product.LazBis, default);
        var selected = LazBisInstaller.SelectForInstall(available, state);
        Require(LazBisInstaller.SameAsset(selected, state.Release), "legacy same version keeps asset pin");
        File.Delete(Path.Combine(root, "lua", "lazbis", "bg.lua"));
        (await installer.PrepareAsync(feed, selected, default)).Apply();
        VerifyPayload(root, original);
        var repaired = installer.Inspect()!;
        Require(repaired.Release.Source is null && LazBisInstaller.SameAsset(repaired.Release, state.Release) &&
            repaired.Hashes.OrderBy(p => p.Key).SequenceEqual(state.Hashes.OrderBy(p => p.Key)), "legacy version and ownership preserved");
        VerifySentinels(sentinels);
        handler.LegacyUnavailable = true;
        await Refuses(() => installer.PrepareAsync(feed, selected, default), "unavailable");
        handler.LegacyUnavailable = false;
        handler.Main = new string('b', 40);
        var newer = await feed.CheckAsync(Product.LazBis, default);
        (await installer.PrepareAsync(feed, LazBisInstaller.SelectForInstall(newer, repaired), default)).Apply();
        Require(installer.Inspect()?.Release.Source?.Commit == handler.Main, "legacy upgrades to bumped source version");
        VerifySentinels(sentinels);
    }

    internal static async Task LiveAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "LazBisLiveSource-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var feed = new GitHubFeed("ThatSolo/LazBisPatcher");
            var release = await feed.CheckAsync(Product.LazBis, deadline.Token);
            var mq = CreateRoot(root, "MacroQuest");
            var sentinels = AddSentinels(mq);
            var installer = new LazBisInstaller(mq);
            (await installer.PrepareAsync(feed, release, deadline.Token)).Apply();
            var recorded = installer.Inspect()!;
            Require(recorded.Release.Source?.Commit == release.Source?.Commit && recorded.Hashes.Count == 13, "live commit identity");
            Require(!recorded.Hashes.ContainsKey("CHANGELOG") && !File.Exists(Path.Combine(mq, "lua", "lazbis", "CHANGELOG")), "live optional notes never installed");
            VerifySentinels(sentinels);
            Console.WriteLine($"PASS live ThatSolo/lazbis main {release.Tag}, commit {release.Source!.Commit}, 13 verified files installed; player files preserved; verified version notes available: {release.Notes != LazBisSourceFeed.GenericNotes}");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static Release LegacyRelease(byte[] zip) => new(Product.LazBis, "1.0", new(1, 0, 0),
        439858043, 729732529, "lazbis.zip", zip.Length, Files.Hash(zip), "Initial release");
    private static string CreateRoot(string fixture, string name)
    {
        var root = Path.Combine(fixture, name);
        Directory.CreateDirectory(Path.Combine(root, "lua"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        return root;
    }
    private static Dictionary<string, string> AddSentinels(string root)
    {
        var sentinels = new Dictionary<string, string>();
        foreach (var relative in new[] { "config/lazbis_lockouts.lua", "resources/lazbis.db", "lua/lazbis/local-notes.txt", "lua/other-tool.lua" })
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "Preserve player data " + relative);
            sentinels[path] = Files.HashFile(path);
        }
        return sentinels;
    }
    private static void VerifySentinels(Dictionary<string, string> sentinels)
    {
        foreach (var (path, hash) in sentinels) Require(Files.HashFile(path) == hash, "player file " + path);
    }
    private static void VerifyPayload(string root, Dictionary<string, byte[]> payload)
    {
        foreach (var (path, bytes) in payload) Require(Files.HashFile(Path.Combine(root, "lua", "lazbis", path)) == Files.Hash(bytes), path);
    }
    private static void Require(bool ok, string reason) { if (!ok) throw new Exception("FAIL " + reason); }
    private static async Task Refuses(Func<Task> action, string message)
    {
        try { await action(); }
        catch (Exception e) when ((e is IOException or InvalidDataException or HttpRequestException) && e.Message.Contains(message, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new Exception("FAIL expected refusal: " + message);
    }

    private sealed class SourceHandler(byte[] legacyZip) : HttpMessageHandler
    {
        internal Dictionary<string, Dictionary<string, byte[]>> Commits { get; } = [];
        internal Dictionary<string, byte[]> Notes { get; } = [];
        internal List<string> Requests { get; } = [];
        internal string Main = "";
        internal string? Unavailable;
        internal string? CorruptPath;
        internal bool LegacyUnavailable;
        internal bool MainUnavailable;
        internal bool LinkedFile;
        internal string? MoveMainOnNotes;
        internal bool NotesUnavailable;
        internal bool OversizeNotes;
        internal CancellationTokenSource? CancelOnNotes;
        internal int OpenBodies;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            byte[] bytes;
            var status = HttpStatusCode.OK;
            if (url.EndsWith("/commits/main", StringComparison.Ordinal))
            {
                bytes = JsonSerializer.SerializeToUtf8Bytes(new { sha = Main, commit = new { tree = new { sha = Main } } });
                if (MainUnavailable) status = HttpStatusCode.NotFound;
            }
            else if (request.RequestUri.AbsolutePath.Contains("/git/trees/", StringComparison.Ordinal))
            {
                var commit = request.RequestUri.AbsolutePath.Split('/')[^1];
                var entries = Commits[commit].Select(f => new { path = f.Key, mode = LinkedFile && f.Key == "bg.lua" ? "120000" : "100644", type = "blob", size = f.Value.Length, sha = LazBisSourceRules.GitBlobHash(f.Value) }).ToList();
                if (Notes.TryGetValue(commit, out var note)) entries.Add(new { path = "CHANGELOG", mode = "100644", type = "blob", size = OversizeNotes ? 65537 : note.Length, sha = LazBisSourceRules.GitBlobHash(note) });
                bytes = JsonSerializer.SerializeToUtf8Bytes(new { sha = commit, truncated = false, tree = entries });
            }
            else if (request.RequestUri.Host == "raw.githubusercontent.com")
            {
                var path = request.RequestUri.AbsolutePath.Split('/', 5);
                if (path[3] == Unavailable) { bytes = []; status = HttpStatusCode.NotFound; }
                else
                {
                    bytes = path[4] == "CHANGELOG" ? Notes[path[3]] : Commits[path[3]][path[4]];
                    if (path[4] == "CHANGELOG")
                    {
                        if (CancelOnNotes is { } cancellation) { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
                        if (NotesUnavailable) status = HttpStatusCode.NotFound;
                        if (MoveMainOnNotes is { } next) Main = next;
                    }
                    if (path[4] == CorruptPath) { bytes = bytes.ToArray(); bytes[0] ^= 1; }
                }
            }
            else if (url == "https://api.github.com/repos/ThatSolo/lazbis/releases/assets/729732529")
            {
                bytes = legacyZip;
                if (LegacyUnavailable) status = HttpStatusCode.NotFound;
            }
            else if (url == "https://api.github.com/repos/ThatSolo/LazBisPatcher/releases/latest")
                bytes = JsonSerializer.SerializeToUtf8Bytes(new { draft = false, prerelease = false, tag_name = "v0.1.6", id = 3,
                    assets = new[] { new { name = "LazBisPatcher.exe", state = "uploaded", id = 33, size = 7, digest = "sha256:" + Files.Hash("patcher"u8.ToArray()) } } });
            else if (url == "https://api.github.com/repos/ThatSolo/LazBisPatcher/releases/assets/33") bytes = "patcher"u8.ToArray();
            else throw new Exception("Unexpected request " + url);
            OpenBodies++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new TrackedBody(bytes, () => OpenBodies--) });
        }
        private sealed class TrackedBody(byte[] bytes, Action closed) : ByteArrayContent(bytes)
        {
            protected override void Dispose(bool disposing) { if (disposing) closed(); base.Dispose(disposing); }
        }
    }
}
