#if FIXTURE_FEED
namespace LazBisPatcher.Core;

// Compiled out of distribution builds.
public sealed class FixtureFeed(string folder) : IReleaseFeed
{
    public Task<Release> CheckAsync(Product product, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var release = Files.ReadJson<Release>(Path.Combine(folder, product + ".json"));
        ReleaseRules.Validate(release);
        if (release.Product != product) throw new InvalidDataException("Wrong fixture product.");
        return Task.FromResult(release);
    }

    public async Task DownloadAsync(Release release, Stream destination, CancellationToken token)
    {
        var source = Path.Combine(folder, release.AssetId + "-" + ReleaseRules.NameOf(release.Product));
        Files.Guard(source);
        await using var input = File.OpenRead(source);
        if (release.Product == Product.Patcher && File.Exists(Path.Combine(folder, "pause-download")))
        {
            var prefix = new byte[65536];
            var count = await input.ReadAsync(prefix, token);
            await destination.WriteAsync(prefix.AsMemory(0, count), token);
            await destination.FlushAsync(token);
            Files.Write(Path.Combine(folder, "download-paused"), "partial candidate written"u8.ToArray());
            while (File.Exists(Path.Combine(folder, "pause-download"))) await Task.Delay(50, token);
            await Files.CopyBoundedAsync(input, destination, release.Bytes - count, token);
            return;
        }
        await Files.CopyBoundedAsync(input, destination, release.Bytes, token);
    }
}
#endif
