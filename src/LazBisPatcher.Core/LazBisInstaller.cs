using System.Text;
using System.Text.RegularExpressions;

namespace LazBisPatcher.Core;

public sealed record InstallState(string Operation, Release Release, Dictionary<string, string> Hashes);
public sealed record InstallObservation(InstallState? Recorded, bool HasExistingFiles, Version? ReportedVersion,
    int MissingManagedFiles = 0);
public sealed record FileChange(string Path, string? Before, string After, string Action);
public sealed record InstallPreview(string Root, Release Release, InstallState? Installed, FileChange[] Changes);
internal sealed record BackupFile(string Path, byte[]? Before, string After);
internal sealed record Backup(string Operation, string Root, BackupFile[] Files);

public sealed class LazBisInstaller
{
    private const string Marker = "LazBisPatcher preview state v1";
    public string Root { get; }
    private string Target => Path.Combine(Root, "lua", "lazbis");
    private string Control => Path.Combine(Root, "lua", ".lazbis-patcher");
    private string StatePath => Path.Combine(Control, "installed.json");
    private string PendingPath => Path.Combine(Control, "pending.json");

    public LazBisInstaller(string root)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        Files.Guard(Root);
        Files.Guard(Target);
        if (!Directory.Exists(Path.Combine(Root, "lua")) || !Directory.Exists(Path.Combine(Root, "config")))
            throw new InvalidDataException("Choose the MacroQuest folder containing lua and config.");
    }

    public InstallState? Inspect()
    {
        if (!Directory.Exists(Control)) return null;
        ValidateControl();
        using var lease = Files.Lock(Path.Combine(Control, "writer.lock"));
        if (File.Exists(PendingPath)) throw new InvalidDataException("An interrupted LazBis update needs recovery before checking.");
        return ReadState();
    }

    public InstallObservation Observe()
    {
        var recorded = Inspect();
        Files.Guard(Target);
        if (File.Exists(Target)) throw new InvalidDataException("A file occupies the LazBis folder.");
        if (recorded is not null)
        {
            var missing = 0;
            foreach (var name in recorded.Hashes.Keys)
            {
                var path = Path.Combine(Target, name);
                Files.Guard(path);
                if (Directory.Exists(path)) throw new InvalidDataException($"A folder occupies {name}.");
                if (!File.Exists(path)) missing++;
            }
            return new(recorded, missing < recorded.Hashes.Count, null, missing);
        }
        if (!Directory.Exists(Target)) return new(null, false, null);

        var init = Path.Combine(Target, "init.lua");
        Files.Guard(init);
        if (!File.Exists(init)) return new(null, Directory.EnumerateFileSystemEntries(Target).Any(), null);

        // Read the reported version as text. Only the verified state governs file ownership.
        using var script = File.OpenRead(init);
        Span<byte> header = stackalloc byte[8192];
        var count = script.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        var declaration = Regex.Match(Encoding.UTF8.GetString(header[..count]).TrimStart('\uFEFF'),
            """(?m)^[ \t]*local[ \t]+meta[ \t]*=[ \t]*\{[ \t]*version[ \t]*=[ \t]*(['"])([0-9]{1,9}(?:\.[0-9]{1,9}){1,3})\1[ \t]*[,}]""");
        Version? reported = null;
        if (Version.TryParse(declaration.Groups[2].Value, out var version))
            reported = new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        return new(null, true, reported);
    }

    public async Task<PreparedLazBis> PrepareAsync(IReleaseFeed feed, Release release, CancellationToken token)
    {
        if (release.Product != Product.LazBis) throw new InvalidDataException("Expected a LazBis release.");
        var scratch = Path.Combine(Path.GetTempPath(), "LazBisPatcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var zip = Path.Combine(scratch, "payload.zip");
        try
        {
            await Files.StageAsync(feed, release, zip, token);
            using var stream = File.OpenRead(zip);
            var payload = LazBisPackage.Read(stream, release);
            return new(this, Plan(release, payload), payload);
        }
        finally
        {
            if (File.Exists(zip)) File.Delete(zip);
            Directory.Delete(scratch);
        }
    }

    private InstallPreview Plan(Release release, Dictionary<string, byte[]> payload)
    {
        Files.Guard(Control);
        if (Directory.Exists(Control)) ValidateControl();
        if (File.Exists(PendingPath)) throw new InvalidDataException("Recover the interrupted update first.");
        var state = ReadState();
        if (state is not null && (release.Version < state.Release.Version ||
            release.Version == state.Release.Version && !SameAsset(release, state.Release)))
            throw new InvalidDataException("The offered release would downgrade LazBis or changes an existing version. Publish a new version.");
        var changes = payload.Select(entry =>
        {
            var path = Path.Combine(Target, entry.Key);
            Files.Guard(path);
            if (Directory.Exists(path)) throw new InvalidDataException($"A folder occupies {entry.Key}.");
            var before = File.Exists(path) ? Files.HashFile(path, 4194304) : null;
            var after = Files.Hash(entry.Value);
            var action = before == after ? "Keep" : before is null ? "Add" :
                state?.Hashes.GetValueOrDefault(entry.Key) == before ? "Replace" : "Conflict";
            return new FileChange(entry.Key, before, after, action);
        }).ToArray();
        return new(Root, release, state, changes);
    }

    internal void Apply(PreparedLazBis prepared, Action<string>? afterWrite = null)
    {
        InitializeControl();
        using var lease = Files.Lock(Path.Combine(Control, "writer.lock"));
        var plan = Plan(prepared.Preview.Release, prepared.Payload);
        if (!plan.Changes.SequenceEqual(prepared.Preview.Changes) || plan.Installed?.Operation != prepared.Preview.Installed?.Operation)
            throw new InvalidDataException("The installation changed after review. Check again.");
        if (plan.Changes.Any(c => c.Action == "Conflict"))
            throw new InvalidDataException("Existing files differ from the known release. Back up or move the conflicting files and check again.");

        var backupFiles = plan.Changes.Where(c => c.Action != "Keep").Select(change =>
        {
            var path = Path.Combine(Target, change.Path);
            byte[]? before = change.Before is null ? null : Files.Read(path, 4194304);
            if (before is not null && Files.Hash(before) != change.Before)
                throw new InvalidDataException("A managed file changed after review.");
            return new BackupFile(change.Path, before, change.After);
        }).ToArray();
        var backup = new Backup(Guid.NewGuid().ToString("N"), Root, backupFiles);
        var backupBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(backup, Files.Json);
        if (backupBytes.Length > 24 << 20) throw new InvalidDataException("The backup exceeds the preview limit.");
        Files.Write(PendingPath, backupBytes, replace: false);
        try
        {
            foreach (var file in backup.Files)
            {
                var path = Path.Combine(Target, file.Path);
                var before = File.Exists(path) ? Files.HashFile(path, 4194304) : null;
                if (before != (file.Before is null ? null : Files.Hash(file.Before)))
                    throw new InvalidDataException("A managed file changed during the update.");
                Files.Write(path, prepared.Payload[file.Path], replace: file.Before is not null);
                afterWrite?.Invoke(file.Path);
            }
            foreach (var change in plan.Changes)
                if (Files.HashFile(Path.Combine(Target, change.Path), 4194304) != change.After)
                    throw new InvalidDataException($"Verification failed for {change.Path}.");
            Files.WriteJson(StatePath, new InstallState(backup.Operation, plan.Release,
                plan.Changes.ToDictionary(c => c.Path, c => c.After, StringComparer.Ordinal)));
        }
        catch (Exception failure)
        {
            try { RecoverLocked(); }
            catch (Exception rollback)
            {
                throw new IOException($"Update failed: {failure.Message} Recovery needs review: {rollback.Message}", rollback);
            }
            throw new IOException($"Update failed. Previous files restored. {failure.Message}", failure);
        }
        FinishCommitted();
    }

    public string Recover()
    {
        if (!Directory.Exists(Control)) return "No update recovery needed.";
        ValidateControl();
        using var lease = Files.Lock(Path.Combine(Control, "writer.lock"));
        return RecoverLocked();
    }

    private string RecoverLocked()
    {
        if (!File.Exists(PendingPath)) return "No update recovery needed.";
        var backup = Files.ReadJson<Backup>(PendingPath, 24 << 20);
        if (!Guid.TryParseExact(backup.Operation, "N", out _) || !string.Equals(backup.Root, Root, StringComparison.OrdinalIgnoreCase) ||
            backup.Files.Length > LazBisPackage.Paths.Count || backup.Files.Select(f => f.Path).Distinct().Count() != backup.Files.Length ||
            backup.Files.Any(f => !LazBisPackage.Paths.Contains(f.Path) || !Files.IsHash(f.After) || f.Before?.Length > 4194304))
            throw new InvalidDataException("Backup identity is invalid. Keep the recovery files for review.");
        if (ReadState()?.Operation == backup.Operation)
        {
            FinishCommitted();
            return "The update committed. Its backup was retained.";
        }

        foreach (var file in backup.Files.Reverse())
        {
            var path = Path.Combine(Target, file.Path);
            Files.Guard(path);
            if (Directory.Exists(path)) throw new InvalidDataException($"Unexpected directory at {file.Path}.");
            var before = file.Before is null ? null : Files.Hash(file.Before);
            var current = File.Exists(path) ? Files.HashFile(path, 4194304) : null;
            if (current == before) continue;
            if (current != file.After) throw new InvalidDataException($"An unexpected change to {file.Path} was preserved.");
            if (file.Before is null) File.Delete(path);
            else Files.Write(path, file.Before);
        }
        File.Delete(PendingPath);
        return "Interrupted update rolled back.";
    }

    private void FinishCommitted()
    {
        var backup = Path.Combine(Control, "last-backup.json");
        Files.Guard(backup);
        try { File.Move(PendingPath, backup, overwrite: true); }
        catch (IOException e) { throw new IOException("Update committed. Backup finalization needs recovery.", e); }
        catch (UnauthorizedAccessException e) { throw new IOException("Update committed. Backup finalization needs recovery.", e); }
    }

    private InstallState? ReadState()
    {
        if (!File.Exists(StatePath)) return null;
        var state = Files.ReadJson<InstallState>(StatePath);
        ReleaseRules.Validate(state.Release);
        if (state.Release.Product != Product.LazBis || !Guid.TryParseExact(state.Operation, "N", out _) ||
            state.Hashes.Count != LazBisPackage.Paths.Count ||
            state.Hashes.Any(p => !LazBisPackage.Paths.Contains(p.Key) || !Files.IsHash(p.Value)))
            throw new InvalidDataException("Installed version record needs review.");
        return state;
    }

    private void InitializeControl()
    {
        Files.Guard(Control);
        if (!Directory.Exists(Control))
        {
            Directory.CreateDirectory(Control);
            var marker = Path.Combine(Control, "owner");
            try { Files.Write(marker, System.Text.Encoding.UTF8.GetBytes(Marker), replace: false); }
            catch (IOException) when (File.Exists(marker)) { }
        }
        ValidateControl();
    }

    private void ValidateControl()
    {
        Files.Guard(Control);
        var marker = Path.Combine(Control, "owner");
        if (!File.Exists(marker) || System.Text.Encoding.UTF8.GetString(Files.Read(marker, 128)) != Marker)
            throw new InvalidDataException("LazBisPatcher state directory has an unknown owner.");
    }

    // An unchanged version repairs from its recorded source, even if main has moved.
    public static Release SelectForInstall(Release available, InstallState? installed) =>
        installed is null || available.Version > installed.Release.Version ? available : installed.Release;

    public static bool SameAsset(Release a, Release b) => a.Product == b.Product && a.Source?.Commit == b.Source?.Commit && a.ReleaseId == b.ReleaseId &&
        a.AssetId == b.AssetId && a.Tag == b.Tag && a.Version == b.Version && a.Bytes == b.Bytes && a.Sha256 == b.Sha256;
}

public sealed class PreparedLazBis
{
    private readonly LazBisInstaller installer;
    public InstallPreview Preview { get; }
    internal Dictionary<string, byte[]> Payload { get; }
    internal PreparedLazBis(LazBisInstaller installer, InstallPreview preview, Dictionary<string, byte[]> payload)
        => (this.installer, Preview, Payload) = (installer, preview, payload);
    public void Apply() => installer.Apply(this);
    internal void ApplyWithProbe(Action<string> afterWrite) => installer.Apply(this, afterWrite);
}
