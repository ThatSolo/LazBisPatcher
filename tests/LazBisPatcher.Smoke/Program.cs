using System.IO.Compression;
using LazBisPatcher.Core;

if (args.Length is < 1 or > 3) throw new ArgumentException("Usage: Smoke path-to-real-1.0.zip [compiled-patcher.exe] [--live]");
var input = File.ReadAllBytes(args[0]);
const string digest = "a975f2a5fcfe91fdb6c7cbe694563954950dcd0b6b7cee5400f2f2ee9d150e44";
Require(Files.Hash(input) == digest, "real release pin");
var release1 = new Release(Product.LazBis, "1.0", new(1, 0, 0), 439858043, 729732529,
    "lazbis.zip", input.Length, digest, "Initial release");
using var source = new MemoryStream(input);
var original = LazBisPackage.Read(source, release1);
Require(original.Count == 13, "real reviewed inventory");
var fixture = Path.Combine(Path.GetTempPath(), "LazBisSmoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
try
{
    string root = Path.Combine(fixture, "MacroQuest");
    Directory.CreateDirectory(Path.Combine(root, "lua", "lazbis"));
    Directory.CreateDirectory(Path.Combine(root, "config"));
    Directory.CreateDirectory(Path.Combine(root, "resources"));
    var database = Path.Combine(root, "resources", "lazbis.db");
    var config = Path.Combine(root, "config", "lazbis_lockouts.lua");
    var unknown = Path.Combine(root, "lua", "lazbis", "local-notes.txt");
    var target = Path.Combine(root, "lua", "lazbis", "init.lua");
    foreach (var path in new[] { database, config, unknown }) File.WriteAllText(path, "preserve");
    var installer = new LazBisInstaller(root);
    var freshRoot = Path.Combine(fixture, "FreshMacroQuest");
    Directory.CreateDirectory(Path.Combine(freshRoot, "lua"));
    Directory.CreateDirectory(Path.Combine(freshRoot, "config"));
    var fresh = new LazBisInstaller(freshRoot);
    Require(fresh.Observe() == new InstallObservation(null, false, null), "fresh install detection");
    Directory.CreateDirectory(Path.Combine(freshRoot, "lua", "lazbis"));
    Require(!fresh.Observe().HasExistingFiles, "empty folder is ready for install");
    Require(installer.Observe() == new InstallObservation(null, true, null), "unrecognized existing folder");
    File.WriteAllText(target, "-- Older LazBis\nlocal meta = {version = '0.9', name = 'lazbis'}\nerror('Do not execute')");
    Require(installer.Observe() == new InstallObservation(null, true, new Version(0, 9, 0, 0)), "reported older version");
    var plan = await installer.PrepareAsync(new MemoryFeed(input), release1, default);
    Require(plan.Preview.Changes.Any(c => c.Path == "init.lua" && c.Action == "Conflict"), "unknown differing file is a conflict");
    await Refuses(() => Task.Run(plan.Apply));
    Require(File.ReadAllText(target).Contains("Do not execute", StringComparison.Ordinal), "conflict preserves local file");
    File.WriteAllBytes(target, original["init.lua"]);
    Require(installer.Observe() == new InstallObservation(null, true, new Version(1, 0, 0, 0)), "published 1.0 reports its version");
    plan = await installer.PrepareAsync(new MemoryFeed(input), release1, default);
    Require(plan.Preview.Installed is null && plan.Preview.Changes.Any(c => c.Action == "Keep"), "review before adopting");
    plan.Apply();
    Require(installer.Inspect()?.Release.Tag == "1.0", "recorded v1");
    Require(installer.Observe() is { Recorded.Release.Tag: "1.0", HasExistingFiles: true, ReportedVersion: null, MissingManagedFiles: 0 },
        "record and managed files are present");
    Console.WriteLine("PASS actual 1.0 ZIP, explicit adoption, 13 verified files");

    File.Delete(target);
    Require(installer.Observe() is { Recorded.Release.Tag: "1.0", HasExistingFiles: true, MissingManagedFiles: 1 },
        "one missing file marks a recorded installation incomplete");
    var repair = await installer.PrepareAsync(new MemoryFeed(input), release1, default);
    Require(repair.Preview.Changes.Single(c => c.Action == "Add").Path == "init.lua", "partial repair plans only the missing file");
    repair.Apply();

    var installedFolder = Path.GetDirectoryName(target)!;
    var heldFolder = Path.Combine(fixture, "HeldLazBis");
    Directory.Move(installedFolder, heldFolder);
    var recorded = installer.Inspect();
    Require(installer.Observe() is { Recorded.Release.Tag: "1.0", HasExistingFiles: false, MissingManagedFiles: 13 },
        "deleting the folder leaves a historical record but no installed files");
    Require(installer.Inspect()?.Operation == recorded?.Operation && !Directory.Exists(installedFolder),
        "observation preserves the record and never recreates the folder");
    Directory.CreateDirectory(installedFolder);
    Require(installer.Observe() is { HasExistingFiles: false, MissingManagedFiles: 13 }, "empty recorded folder is also missing");
    var reinstall = await installer.PrepareAsync(new MemoryFeed(input), release1, default);
    Require(reinstall.Preview.Changes.All(c => c.Action == "Add"), "reinstall plans to restore all 13 files");
    reinstall.Apply();
    Require(installer.Observe() is { Recorded.Release.Tag: "1.0", HasExistingFiles: true, MissingManagedFiles: 0 },
        "reinstall restores the managed files");
    File.Move(Path.Combine(heldFolder, "local-notes.txt"), unknown);
    Console.WriteLine("PASS recorded install detects missing files and safely repairs or reinstalls");

    var updated = original.ToDictionary();
    updated["init.lua"] = [.. updated["init.lua"], .. "\n-- generated 1.0.1 fixture\n"u8];
    updated["bg.lua"] = [.. updated["bg.lua"], .. "\n-- generated update\n"u8];
    using var zipped = new MemoryStream();
    using (var archive = new ZipArchive(zipped, ZipArchiveMode.Create, leaveOpen: true))
        foreach (var (name, bytes) in updated)
        {
            using var body = archive.CreateEntry("lazbis/" + name).Open();
            body.Write(bytes);
        }
    var input2 = zipped.ToArray();
    var release2 = release1 with { Tag = "v1.0.1", Version = new(1, 0, 1), ReleaseId = 2,
        AssetId = 22, Bytes = input2.Length, Sha256 = Files.Hash(input2) };
    var bad = input2.ToArray(); bad[0] ^= 1;
    await Refuses(() => installer.PrepareAsync(new MemoryFeed(bad), release2, default));
    Require(Files.HashFile(target) == Files.Hash(original["init.lua"]), "bad download leaves application untouched");
    Console.WriteLine("PASS bad SHA-256 rejected before writing");

    plan = await installer.PrepareAsync(new MemoryFeed(input2), release2, default);
    await Refuses(() => Task.Run(() => plan.ApplyWithProbe(_ => throw new IOException("generated mid-apply failure"))));
    Require(installer.Inspect()?.Release.Tag == "1.0", "rollback keeps version");
    foreach (var (name, bytes) in original)
        Require(Files.HashFile(Path.Combine(root, "lua", "lazbis", name)) == Files.Hash(bytes), "rollback " + name);
    plan.Apply();
    Require(installer.Inspect()?.Release.Tag == "v1.0.1", "recorded v1.0.1");
    Require(Files.HashFile(target) == Files.Hash(updated["init.lua"]), "verified new bytes");
    foreach (var path in new[] { database, config, unknown }) Require(File.ReadAllText(path) == "preserve", "sentinel " + path);
    File.AppendAllText(target, "\n-- player edit");
    var conflict = await installer.PrepareAsync(new MemoryFeed(input2), release2, default);
    Require(conflict.Preview.Changes.Any(c => c.Path == "init.lua" && c.Action == "Conflict"), "known local edit is preserved");
    Console.WriteLine("PASS verified LazBis update, mid-apply rollback, database/config/local/unknown preservation");

    if (args.Length == 3 && args[2] == "--live")
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var feed = new GitHubFeed("ThatSolo/LazBisPatcher");
        var actual = await feed.CheckAsync(Product.LazBis, deadline.Token);
        Require(LazBisInstaller.SameAsset(actual, release1), "current GitHub 1.0 offer identity");
        var liveRoot = Path.Combine(fixture, "LiveReleaseDisposable");
        Directory.CreateDirectory(Path.Combine(liveRoot, "lua"));
        Directory.CreateDirectory(Path.Combine(liveRoot, "config"));
        var live = new LazBisInstaller(liveRoot);
        (await live.PrepareAsync(feed, actual, deadline.Token)).Apply();
        Require(live.Inspect()?.Release.AssetId == release1.AssetId, "real feed asset installed");
        Console.WriteLine("PASS live GitHub stable check, exact asset-ID download and disposable install");
    }

    if (args.Length >= 2)
    {
        var app = File.ReadAllBytes(args[1]);
        var dir = Path.Combine(fixture, "patcher");
        Directory.CreateDirectory(dir);
        var appPath = Path.Combine(dir, SelfUpdate.AppName);
        var old = "old version bytes"u8.ToArray();
        File.WriteAllBytes(appPath, old);
        var updater = new SelfUpdate(dir);
        using (updater.OpenAppLease()) { }
        var version = ExecutableIdentity.Read(args[1]);
        var compiled = new Version(version.Major, version.Minor, version.Build);
        var offer = new Release(Product.Patcher, "v" + compiled, compiled, 3, 33, ReleaseRules.PatcherAsset,
            app.Length, Files.Hash(app), "");
        await updater.PrepareAsync(new MemoryFeed(app), offer, new Version(0, 0, 0), default);
        var control = Path.Combine(dir, ".lazbis-self-update");
        var pending = Files.ReadJson<PendingUpdate>(Path.Combine(control, "pending.json"));
        File.Replace(Path.Combine(control, "candidate.exe"), appPath, Path.Combine(control, "previous.exe"));
        updater.RecoverForProbe();
        Require(Files.HashFile(appPath) == Files.Hash(old) && !updater.HasPending, "recover interrupted executable swap");
        await updater.PrepareAsync(new MemoryFeed(app), offer, new Version(0, 0, 0), default);
        pending = Files.ReadJson<PendingUpdate>(Path.Combine(control, "pending.json"));
        File.Replace(Path.Combine(control, "candidate.exe"), appPath, Path.Combine(control, "previous.exe"));
        updater.Acknowledge(pending.Id, pending.Nonce, offer.Version);
        Files.WriteJson(Path.Combine(control, "pending.json"), pending with { Committed = true });
        updater.RecoverForProbe();
        Require(Files.HashFile(appPath) == offer.Sha256 && !updater.HasPending, "recover committed executable swap");
        Console.WriteLine("PASS PE version verification, executable swap rollback and committed recovery");

        if (OperatingSystem.IsLinux())
        {
            // The Linux host refuses to start this Windows PE. Verify the rollback
            // leaves the old bytes even if the restored process cannot start.
            var previous = "#!/bin/sh\nprintf restored > relaunched.txt\n"u8.ToArray();
            File.WriteAllBytes(appPath, previous);
            File.SetUnixFileMode(appPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await updater.PrepareAsync(new MemoryFeed(app), offer, new Version(0, 0, 0), default);
            try { await updater.RunHelperAsync(false, 0, 0); throw new Exception("Expected OS launch refusal."); }
            catch (System.ComponentModel.Win32Exception) { }
            Require(Files.HashFile(appPath) == Files.Hash(previous) && !updater.HasPending, "failed launch rolls back");
            Console.WriteLine("PASS actual candidate launch failure restores prior executable bytes");
        }
    }
}
finally { Directory.Delete(fixture, recursive: true); }

static void Require(bool ok, string reason)
{
    if (!ok) throw new Exception("FAIL " + reason);
}

static async Task Refuses(Func<Task> work)
{
    try { await work(); }
    catch (Exception error) when (error is IOException or InvalidDataException) { return; }
    throw new Exception("FAIL expected refusal");
}

sealed class MemoryFeed(byte[] bytes) : IReleaseFeed
{
    public Task<Release> CheckAsync(Product product, CancellationToken token) => throw new NotSupportedException();
    public Task DownloadAsync(Release release, Stream destination, CancellationToken token) =>
        destination.WriteAsync(bytes, token).AsTask();
}
