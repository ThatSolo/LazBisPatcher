using LazBisPatcher.Core;

internal static class DownloadRecoveryChecks
{
    internal static async Task RunAsync(string fixture, byte[] app, Release release)
    {
        var number = 0;
        (SelfUpdate Updater, string Root, string Control, PendingUpdate Pending) Create()
        {
            var root = Path.Combine(fixture, "DownloadRecovery" + number++);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, SelfUpdate.AppName), "installed executable bytes");
            var updater = new SelfUpdate(root);
            using (updater.OpenAppLease()) { }
            var pending = new PendingUpdate(Guid.NewGuid().ToString("N"), new string('a', 64), Files.HashFile(Path.Combine(root, SelfUpdate.AppName)), release);
            return (updater, root, Path.Combine(root, ".lazbis-self-update"), pending);
        }
        void Own((SelfUpdate Updater, string Root, string Control, PendingUpdate Pending) item)
        {
            Files.WriteJson(Path.Combine(item.Control, "download.json"), new DownloadingUpdate("LazBisPatcher owned download v1", item.Pending));
            File.WriteAllBytes(Path.Combine(item.Control, "candidate.exe"), app[..65536]);
        }

        var partial = Create();
        Own(partial);
        using (Files.Lock(Path.Combine(partial.Control, "update.lock")))
            await Refuses(() => Task.Run(() => { using var lease = partial.Updater.OpenNormalAppLease(); }));
        Require(File.Exists(Path.Combine(partial.Control, "candidate.exe")), "busy updater lock prevents cleanup");
        using (partial.Updater.OpenNormalAppLease()) { }
        Require(!File.Exists(Path.Combine(partial.Control, "candidate.exe")) && !File.Exists(Path.Combine(partial.Control, "download.json")), "owned interrupted staging cleaned");
        Require(Files.HashFile(Path.Combine(partial.Root, SelfUpdate.AppName)) == partial.Pending.OldHash, "installed EXE unchanged");
        await partial.Updater.PrepareAsync(new MemoryFeed(app), release, new(0, 0, 0), default);
        Require(partial.Updater.HasPending, "retry prepared the selected EXE");

        // Constructed prepared state: ownership left behind after pending.json was published.
        var pending = Files.ReadJson<PendingUpdate>(Path.Combine(partial.Control, "pending.json"));
        Files.WriteJson(Path.Combine(partial.Control, "download.json"), new DownloadingUpdate("LazBisPatcher owned download v1", pending));
        using (partial.Updater.OpenNormalAppLease()) { }
        Require(partial.Updater.HasPending && Files.HashFile(Path.Combine(partial.Control, "candidate.exe")) == release.Sha256 &&
            File.Exists(Path.Combine(partial.Control, "download.json")), "startup preserves prepared candidate and journal");
        partial.Updater.RecoverForProbe();
        Require(!partial.Updater.HasPending && !File.Exists(Path.Combine(partial.Control, "download.json")), "explicit prepared recovery clears matching ownership");

        var beforeCreate = Create();
        Own(beforeCreate);
        File.Delete(Path.Combine(beforeCreate.Control, "candidate.exe"));
        using (beforeCreate.Updater.OpenNormalAppLease()) { }
        Require(!File.Exists(Path.Combine(beforeCreate.Control, "download.json")), "interruption before candidate creation cleans ownership only");

        var foreign = Create();
        Own(foreign);
        Files.WriteJson(Path.Combine(foreign.Control, "download.json"), new DownloadingUpdate("unknown owner", foreign.Pending));
        await Refuses(() => Task.Run(() => { using var lease = foreign.Updater.OpenNormalAppLease(); }));
        Require(new FileInfo(Path.Combine(foreign.Control, "candidate.exe")).Length == 65536, "unknown ownership preserves candidate");

        var mismatch = Create();
        await mismatch.Updater.PrepareAsync(new MemoryFeed(app), release, new(0, 0, 0), default);
        var prepared = Files.ReadJson<PendingUpdate>(Path.Combine(mismatch.Control, "pending.json"));
        Files.WriteJson(Path.Combine(mismatch.Control, "download.json"), new DownloadingUpdate("LazBisPatcher owned download v1", prepared with { Id = Guid.NewGuid().ToString("N") }));
        await Refuses(() => Task.Run(mismatch.Updater.RecoverForProbe));
        Require(mismatch.Updater.HasPending && Files.HashFile(Path.Combine(mismatch.Control, "candidate.exe")) == release.Sha256,
            "mismatched ownership preserves prepared evidence");

        foreach (var evidence in new[] { "previous.exe", "ready.json", "restore.exe" })
        {
            var item = Create();
            Own(item);
            File.WriteAllText(Path.Combine(item.Control, evidence), "Preserve rollback evidence");
            await Refuses(() => Task.Run(() => { using var lease = item.Updater.OpenNormalAppLease(); }));
            Require(File.ReadAllText(Path.Combine(item.Control, evidence)) == "Preserve rollback evidence" &&
                new FileInfo(Path.Combine(item.Control, "candidate.exe")).Length == 65536 && File.Exists(Path.Combine(item.Control, "download.json")), "rollback evidence prevents discard");
        }
        var legacy = Create();
        File.WriteAllBytes(Path.Combine(legacy.Control, "candidate.exe"), app[..32]);
        using (legacy.Updater.OpenNormalAppLease()) { }
        await Refuses(() => legacy.Updater.PrepareAsync(new MemoryFeed(app), release, new(0, 0, 0), default), "move only candidate.exe");
        Require(File.ReadAllBytes(Path.Combine(legacy.Control, "candidate.exe")).SequenceEqual(app[..32]), "unrecorded legacy candidate preserved");

        var changed = Create();
        Own(changed);
        File.AppendAllText(Path.Combine(changed.Root, SelfUpdate.AppName), "changed");
        await Refuses(() => Task.Run(() => { using var lease = changed.Updater.OpenNormalAppLease(); }));
        Require(File.Exists(Path.Combine(changed.Control, "candidate.exe")) && File.Exists(Path.Combine(changed.Control, "download.json")), "changed installed EXE prevents discard");

        var failure = Create();
        var observed = false;
        await Refuses(() => failure.Updater.PrepareAsync(new InterruptedFeed(async output =>
        {
            var record = Files.ReadJson<DownloadingUpdate>(Path.Combine(failure.Control, "download.json"));
            Require(record.Pending.OldHash == failure.Pending.OldHash && record.Pending.Release.Sha256 == release.Sha256 &&
                !File.Exists(Path.Combine(failure.Control, "pending.json")), "ownership exists before first byte");
            observed = true;
            await output.WriteAsync(app.AsMemory(0, 4096));
            throw new IOException("generated download failure");
        }), release, new(0, 0, 0), default));
        Require(observed && !File.Exists(Path.Combine(failure.Control, "download.json")) && !File.Exists(Path.Combine(failure.Control, "candidate.exe")), "normal failed download cleans owned staging");
        Console.WriteLine("PASS constructed download-state recovery under lock, retry, prepared-state preservation, rollback-evidence refusal, legacy quarantine guidance and ownership-before-write");
    }
    private static void Require(bool condition, string reason) { if (!condition) throw new Exception("FAIL " + reason); }
    private static async Task Refuses(Func<Task> action, string? message = null)
    {
        try { await action(); }
        catch (Exception e) when ((e is IOException or InvalidDataException) && (message is null || e.Message.Contains(message, StringComparison.OrdinalIgnoreCase))) { return; }
        throw new Exception("FAIL expected safe refusal");
    }
    private sealed class InterruptedFeed(Func<Stream, Task> write) : IReleaseFeed
    {
        public Task<Release> CheckAsync(Product product, CancellationToken token) => throw new NotSupportedException();
        public Task DownloadAsync(Release release, Stream destination, CancellationToken token) => write(destination);
    }
}
