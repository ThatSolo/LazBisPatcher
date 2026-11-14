using System.IO.Compression;
using System.Text;
using LazBisPatcher.Core;

internal static class ManualInstallChecks
{
    internal static void WriteUiFixtures(string zipPath, string output)
    {
        if (Directory.Exists(output)) throw new IOException("UI fixture root must be new.");
        var bytes = File.ReadAllBytes(zipPath);
        if (Files.Hash(bytes) != "a975f2a5fcfe91fdb6c7cbe694563954950dcd0b6b7cee5400f2f2ee9d150e44")
            throw new InvalidDataException("Use the reviewed LazBis 1.0 ZIP.");
        var release = new Release(Product.LazBis, "1.0", new(1, 0, 0), 439858043, 729732529, "lazbis.zip", bytes.Length, Files.Hash(bytes), "");
        using var input = new MemoryStream(bytes);
        var baseline = LazBisPackage.Read(input, release);
        var target = Target(baseline);
        foreach (var name in new[] { "official", "fork" })
        {
            var folder = Path.Combine(output, name);
            var root = CreateCopy(Path.Combine(folder, "MacroQuest"), baseline);
            if (name == "fork")
            {
                File.WriteAllText(Path.Combine(root, "lua", "lazbis", "init.lua"), "local meta = {version = '1.0', name = 'lazbis'}\n-- unknown fork\n");
                File.WriteAllText(Path.Combine(root, "lua", "lazbis", "fork-extra.txt"), "Preserve fork extra");
            }
            Files.Write(Path.Combine(folder, "macroquest-folder.txt"), Encoding.UTF8.GetBytes(root));
            Files.WriteJson(Path.Combine(folder, "fixture-feed", "LazBis.json"), target.Release);
            Files.Write(Path.Combine(folder, "fixture-feed", "0-lazbis.zip"), target.Zip);
        }
        Files.WriteJson(Path.Combine(output, "expected-managed.json"), target.Files.ToDictionary(f => f.Key, f => Files.Hash(f.Value)));
        Console.WriteLine("Prepared disposable LazBis UI fixtures at " + output);
    }

    internal static (Release Release, byte[] Zip, Dictionary<string, byte[]> Files) Target(Dictionary<string, byte[]> baseline)
    {
        var files = baseline.ToDictionary();
        files["init.lua"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(files["init.lua"])
            .Replace("version = '1.0'", "version = '1.0.1'", StringComparison.Ordinal));
        files["bg.lua"] = [.. files["bg.lua"], .. "\n-- intended official 1.0.1\n"u8];
        var source = new LazBisSource(new string('f', 40), files.Select(f =>
            new SourceFile(f.Key, f.Value.Length, LazBisSourceRules.GitBlobHash(f.Value))).ToArray());
        var release = new Release(Product.LazBis, "1.0.1", new(1, 0, 1), 0, 0, "lazbis.zip",
            files.Sum(f => (long)f.Value.Length), LazBisSourceRules.ManifestHash(source), "Generated official update", source);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var (path, bytes) in files) { using var entry = zip.CreateEntry("lazbis/" + path).Open(); entry.Write(bytes); }
        return (release, output.ToArray(), files);
    }

    internal static async Task RunAsync(Dictionary<string, byte[]> baseline, string fixture)
    {
        var target = Target(baseline);
        foreach (var missing in new[] { false, true })
        {
            var root = CreateCopy(Path.Combine(fixture, missing ? "ManualMissing" : "ManualOfficial"), baseline);
            var player = PlayerHashes(root);
            if (missing) File.Delete(Path.Combine(root, "lua", "lazbis", "bg.lua"));
            var installer = new LazBisInstaller(root);
            var before = Snapshot(root);
            var plan = await installer.PrepareAsync(new MemoryFeed(target.Zip), target.Release, default);
            Require(plan.Preview.Official10Upgrade && plan.Preview.Changes.All(c => c.Action != "Conflict"), "official upgrade preview");
            Require(plan.Preview.Changes.Single(c => c.Path == "bg.lua").Action == (missing ? "Add" : "Replace"), "missing or owned baseline file");
            Require(installer.Inspect() is null && Same(before, Snapshot(root)), "dismissed preview makes no record or file changes");
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            try { await installer.PrepareAsync(new MemoryFeed(target.Zip), target.Release, cancel.Token); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { }
            Require(installer.Inspect() is null && Same(before, Snapshot(root)), "cancellation preserves manual copy");
            plan.Apply();
            Require(installer.Inspect()?.Release.Source?.Commit == target.Release.Source!.Commit, "record only after apply");
            CheckManaged(root, target.Files);
            CheckPlayer(player);
        }
        Console.WriteLine("PASS unrecorded official 1.0 -> 1.0.1, missing-file add, cancelled preview/download, verified record only after apply, player files preserved");

        var matching = CreateCopy(Path.Combine(fixture, "ManualTargetMatch"), target.Files);
        var matchingInstaller = new LazBisInstaller(matching);
        var matchingPlan = await matchingInstaller.PrepareAsync(new MemoryFeed(target.Zip), target.Release, default);
        Require(matchingPlan.Preview.Changes.All(c => c.Action == "Keep") && !matchingPlan.Preview.Official10Upgrade && matchingInstaller.Inspect() is null,
            "already matching target kept, not labelled an upgrade or recorded before confirmation");

        foreach (var fork in new[] { false, true })
        {
            var root = CreateCopy(Path.Combine(fixture, fork ? "UnknownFork" : "ManualLocalEdit"), baseline);
            var script = Path.Combine(root, "lua", "lazbis");
            if (fork)
                File.WriteAllText(Path.Combine(script, "init.lua"), "local meta = {version = '1.0', name = 'lazbis'}\n-- unknown fork; never execute\n");
            else File.AppendAllText(Path.Combine(script, "spells.lua"), "\n-- local edit\n");
            File.WriteAllText(Path.Combine(script, "fork-extra.txt"), "Preserve this untracked script file");
            var before = Snapshot(script);
            var player = PlayerHashes(root);
            var installer = new LazBisInstaller(root);
            if (fork) Require(installer.Observe().ReportedVersion == new Version(1, 0, 0, 0), "fork reports 1.0 only as information");
            var plan = await installer.PrepareAsync(new MemoryFeed(target.Zip), target.Release, default);
            Require(plan.Preview.Changes.Any(c => c.Action == "Conflict") && !plan.Preview.Official10Upgrade, "unknown managed bytes conflict");
            try { plan.Apply(); throw new Exception("Expected conflict refusal"); }
            catch (InvalidDataException) { }
            Require(installer.Inspect() is null && Same(before, Snapshot(script)), "blocked copy and extras untouched");
            CheckPlayer(player);
            if (fork)
            {
                var moved = Path.Combine(fixture, "MovedForkOutsideLua");
                Directory.Move(script, moved);
                var fresh = await installer.PrepareAsync(new MemoryFeed(target.Zip), target.Release, default);
                Require(fresh.Preview.Changes.All(c => c.Action == "Add"), "manual move enables clean install");
                fresh.Apply();
                CheckManaged(root, target.Files);
                Require(Same(before, Snapshot(moved)) && !File.Exists(Path.Combine(script, "fork-extra.txt")), "old fork is preserved outside lua, never merged");
                CheckPlayer(player.ToDictionary(p => p.Key.StartsWith(script + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    ? Path.Combine(moved, Path.GetRelativePath(script, p.Key)) : p.Key, p => p.Value));
            }
        }
        Console.WriteLine("PASS local edit and unknown fork reporting 1.0 blocked unchanged; manual move outside lua then clean install; shared data and unmanaged files preserved");
    }

    internal static string CreateCopy(string root, Dictionary<string, byte[]> baseline)
    {
        if (Directory.Exists(root)) throw new IOException("Disposable fixture must be new.");
        foreach (var (path, bytes) in baseline)
        {
            var file = Path.Combine(root, "lua", "lazbis", path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, bytes);
        }
        foreach (var path in new[] { "config/lazbis_lockouts.lua", "resources/lazbis.db", "resources/lazbis.db-wal", "resources/lazbis.db-shm", "lua/other-tool.lua" })
        {
            var file = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "Preserve player data " + path);
        }
        File.WriteAllText(Path.Combine(root, "lua", "lazbis", "local-notes.txt"), "Preserve untracked notes");
        return root;
    }

    private static Dictionary<string, string> PlayerHashes(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(p => !LazBisPackage.Paths.Contains(Path.GetRelativePath(Path.Combine(root, "lua", "lazbis"), p).Replace('\\', '/')))
        .ToDictionary(p => p, p => Files.HashFile(p));
    private static void CheckPlayer(Dictionary<string, string> files)
    {
        foreach (var (file, hash) in files) Require(File.Exists(file) && Files.HashFile(file) == hash, "player file " + file);
    }
    private static Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(root, p), p => Files.HashFile(p));
    private static bool Same(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(p => b.GetValueOrDefault(p.Key) == p.Value);
    private static void CheckManaged(string root, Dictionary<string, byte[]> files)
    {
        foreach (var (path, bytes) in files) Require(Files.HashFile(Path.Combine(root, "lua", "lazbis", path)) == Files.Hash(bytes), path);
    }
    private static void Require(bool condition, string reason) { if (!condition) throw new Exception("FAIL " + reason); }
}
