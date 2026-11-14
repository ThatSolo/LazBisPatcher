using System.Diagnostics;
using System.Security.Cryptography;

namespace LazBisPatcher.Core;

public sealed record PendingUpdate(string Id, string Nonce, string OldHash, Release Release, bool Committed = false);
internal sealed record Health(string Id, string Nonce, string Version);

public sealed class SelfUpdate
{
    public const string AppName = "LazBisPatcher.exe";
    public const string HelperName = "LazBisPatcher.Helper.exe";
    private const string Marker = "LazBisPatcher preview self-update v1";
    private readonly string folder;
    private readonly Func<string, string>? bundledHelper;
    private string Control => Path.Combine(folder, ".lazbis-self-update");
    private string Journal => Path.Combine(Control, "pending.json");
    private string Candidate => Path.Combine(Control, "candidate.exe");
    private string Prior => Path.Combine(Control, "previous.exe");
    private string Ready => Path.Combine(Control, "ready.json");
    private string Target => Path.Combine(folder, AppName);
    public bool HasPending => File.Exists(Journal);

    public SelfUpdate(string folder, Func<string, string>? bundledHelper = null)
    {
        this.bundledHelper = bundledHelper;
        this.folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        Files.Guard(this.folder);
        Files.Guard(Control);
    }

    public FileStream OpenNormalAppLease()
    {
        Initialize();
        using var updateLease = LockUpdater();
        return OpenAppLease();
    }

    public FileStream OpenAppLease()
    {
        Initialize();
        return Files.Lock(Path.Combine(Control, "app.lock"));
    }

    public async Task PrepareAsync(IReleaseFeed feed, Release release, Version current, CancellationToken token)
    {
        if (release.Product != Product.Patcher || release.Version <= current)
            throw new InvalidDataException("Choose a newer patcher release.");
        Initialize();
        using var lease = LockUpdater();
        if (HasPending || File.Exists(Candidate) || File.Exists(Prior) || File.Exists(Ready))
            throw new InvalidDataException("An earlier patcher update needs review or recovery.");
        var old = Files.HashFile(Target);
        var pending = new PendingUpdate(Guid.NewGuid().ToString("N"),
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), old, release);
        try
        {
            await Files.StageAsync(feed, release, Candidate, token);
            VerifyExecutable(Candidate, release.Version);
            Files.WriteJson(Journal, pending);
        }
        catch
        {
            if (!HasPending && File.Exists(Candidate)) File.Delete(Candidate);
            throw;
        }
    }

    public void StartHelper(bool recover)
    {
        var path = Path.Combine(folder, HelperName);
        if (bundledHelper is not null)
        {
            Initialize();
            using (LockUpdater()) path = bundledHelper(Control);
        }
        Files.Guard(path);
        if (!File.Exists(path)) throw new InvalidDataException("Extract the complete patcher ZIP, including its update helper.");
        using var self = Process.GetCurrentProcess();
        var start = Start(path);
        foreach (var argument in new[] { recover ? "recover" : "apply", self.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     self.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            start.ArgumentList.Add(argument);
        if (bundledHelper is not null) start.ArgumentList.Add(folder);
        using var child = Process.Start(start) ?? throw new IOException("Could not launch the update helper.");
    }

    public async Task RunHelperAsync(bool recover, int oldPid, long oldStart)
    {
        Initialize();
        using var updateLease = LockUpdater();
        await WaitForOldAsync(oldPid, oldStart);
        if (recover)
        {
            using (OpenAppLease()) RecoverLocked();
            updateLease.Dispose();
            using var restored = Process.Start(Start(Target)) ?? throw new IOException("Could not reopen the recovered patcher.");
            return;
        }

        PendingUpdate pending;
        using (OpenAppLease())
        {
            pending = ReadPending();
            if (Files.HashFile(Target) != pending.OldHash || Files.HashFile(Candidate) != pending.Release.Sha256 ||
                File.Exists(Prior) || File.Exists(Ready))
                throw new InvalidDataException("Patcher update inputs changed. Keep the recovery files for review.");
            VerifyExecutable(Candidate, pending.Release.Version);
            File.Replace(Candidate, Target, Prior);
        }

        Process? trial = null;
        try
        {
            var start = Start(Target);
            start.ArgumentList.Add("--trial");
            start.ArgumentList.Add(pending.Id);
            start.ArgumentList.Add(pending.Nonce);
            trial = Process.Start(start) ?? throw new IOException("The new patcher did not launch.");
            await AwaitHealthAsync(trial, pending, TimeSpan.FromSeconds(35));
            Files.WriteJson(Journal, pending with { Committed = true });
            Cleanup(pending);
        }
        catch when (HasPending && !ReadPending().Committed)
        {
            if (trial is { HasExited: false })
            {
                trial.Kill();
                await trial.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            using (OpenAppLease()) RecoverLocked();
            updateLease.Dispose();
            using var restored = Process.Start(Start(Target)) ?? throw new IOException("Could not reopen the previous patcher.");
            throw;
        }
        finally { trial?.Dispose(); }
    }

    public void Acknowledge(string id, string nonce, Version running)
    {
        var pending = ReadPending();
        if (pending.Committed || id != pending.Id || nonce != pending.Nonce || running != pending.Release.Version ||
            Files.HashFile(Target) != pending.Release.Sha256)
            throw new InvalidDataException("Patcher startup does not match the selected update.");
        Files.WriteJson(Ready, HealthOf(pending));
    }

    public async Task AwaitCommitAsync()
    {
        for (int i = 0; i < 240; i++)
        {
            // The helper removes the journal only after deciding the update.
            if (!HasPending) return;
            await Task.Delay(250);
        }
        throw new IOException("The update helper did not finish. Reopen the patcher to recover.");
    }

    internal void RecoverForProbe()
    {
        using var lease = LockUpdater();
        RecoverLocked();
    }

    private void RecoverLocked()
    {
        var pending = ReadPending();
        Files.Guard(Target);
        var current = File.Exists(Target) ? Files.HashFile(Target) : null;
        if (pending.Committed)
        {
            if (current != pending.Release.Sha256)
                throw new InvalidDataException("Committed executable or startup receipt changed. Review is required.");
        }
        else if (current is null || current == pending.Release.Sha256)
        {
            if (!File.Exists(Prior) || Files.HashFile(Prior) != pending.OldHash)
                throw new InvalidDataException("The previous patcher could not be verified. Keep its recovery files.");
            var restore = Path.Combine(Control, "restore.exe");
            if (!File.Exists(restore)) Files.Write(restore, Files.Read(Prior, (int)ReleaseRules.Limit(Product.Patcher)), replace: false);
            if (Files.HashFile(restore) != pending.OldHash) throw new InvalidDataException("Unexpected recovery executable.");
            if (current is null) File.Move(restore, Target);
            else File.Replace(restore, Target, null);
            if (Files.HashFile(Target) != pending.OldHash) throw new IOException("Previous executable could not be restored.");
        }
        else if (current != pending.OldHash)
            throw new InvalidDataException("The patcher executable changed unexpectedly. It was preserved.");
        Cleanup(pending);
    }

    private void Cleanup(PendingUpdate pending)
    {
        foreach (var (path, hash) in new[] { (Prior, pending.OldHash), (Candidate, pending.Release.Sha256), (Path.Combine(Control, "restore.exe"), pending.OldHash) })
            if (File.Exists(path))
            {
                if (Files.HashFile(path) != hash) throw new InvalidDataException("An unexpected executable was preserved.");
                File.Delete(path);
            }
        if (File.Exists(Ready))
        {
            if (Files.ReadJson<Health>(Ready) != HealthOf(pending)) throw new InvalidDataException("Unexpected startup receipt.");
            File.Delete(Ready);
        }
        File.Delete(Journal);
    }

    private PendingUpdate ReadPending()
    {
        var pending = Files.ReadJson<PendingUpdate>(Journal);
        ReleaseRules.Validate(pending.Release);
        if (pending.Release.Product != Product.Patcher || !Guid.TryParseExact(pending.Id, "N", out _) ||
            !Files.IsHash(pending.Nonce) || !Files.IsHash(pending.OldHash))
            throw new InvalidDataException("Invalid patcher recovery record.");
        return pending;
    }

    private static Health HealthOf(PendingUpdate p) => new(p.Id, p.Nonce, p.Release.Version.ToString());

    internal async Task AwaitHealthAsync(Process trial, PendingUpdate pending, TimeSpan budget)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < budget)
        {
            if (trial.HasExited) throw new IOException("The new patcher exited before startup confirmation.");
            if (File.Exists(Ready))
            {
                if (Files.ReadJson<Health>(Ready) != HealthOf(pending))
                    throw new InvalidDataException("Startup receipt does not match this update.");
                if (trial.HasExited) throw new IOException("The new patcher exited during startup.");
                return;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("The new patcher did not confirm startup.");
    }

    private static async Task WaitForOldAsync(int pid, long ticks)
    {
        if (pid == 0 && ticks == 0) return;
        Process old;
        try { old = Process.GetProcessById(pid); }
        catch (ArgumentException) { return; }
        using (old)
        {
            if (old.HasExited) return;
            if (old.StartTime.ToUniversalTime().Ticks != ticks)
                throw new InvalidDataException("Original patcher process identity changed.");
            await old.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
        }
    }

    private FileStream LockUpdater() => Files.Lock(Path.Combine(Control, "update.lock"));
    private ProcessStartInfo Start(string path) => new(path)
    {
        WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true
    };
    private void Initialize()
    {
        Files.Guard(Control);
        if (!Directory.Exists(Control))
        {
            Directory.CreateDirectory(Control);
            var owner = Path.Combine(Control, "owner");
            try { Files.Write(owner, System.Text.Encoding.UTF8.GetBytes(Marker), replace: false); }
            catch (IOException) when (File.Exists(owner)) { }
        }
        if (System.Text.Encoding.UTF8.GetString(Files.Read(Path.Combine(Control, "owner"), 128)) != Marker)
            throw new InvalidDataException("Unknown patcher state directory.");
    }
    private static void VerifyExecutable(string path, Version expected)
    {
        var version = ExecutableIdentity.Read(path);
        if (version != new Version(expected.Major, expected.Minor, expected.Build, 0))
            throw new InvalidDataException("The executable version does not match the release tag. Rebuild and publish a new tag.");
    }
}
