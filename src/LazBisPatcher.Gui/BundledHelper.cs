using System.IO;
using System.Reflection;
using LazBisPatcher.Core;

namespace LazBisPatcher.Gui;

internal static class BundledHelper
{
    internal static string Ensure(string control)
    {
        var assembly = typeof(App).Assembly;
        var expected = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "BundledHelperSha256").Value;
        if (!Files.IsHash(expected)) throw new InvalidDataException("Missing bundled helper identity.");
        var path = Path.Combine(control, SelfUpdate.HelperName);
        Files.Guard(path);
        if (File.Exists(path) && Files.HashFile(path) == expected) return path;

        // The caller holds the updater lock. Replace only this owned cache file.
        var staged = Path.Combine(control, "helper-stage.exe");
        Files.Guard(staged);
        try
        {
            using (var input = assembly.GetManifestResourceStream("LazBisPatcher.BundledHelper.exe")
                ?? throw new InvalidDataException("Missing bundled helper."))
            using (var output = new FileStream(staged, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (input.Length is <= 0 or > 160L << 20) throw new InvalidDataException("Invalid bundled helper size.");
                input.CopyTo(output);
                output.Flush(true);
            }
            if (Files.HashFile(staged) != expected) throw new InvalidDataException("Bundled helper verification failed.");
            File.Move(staged, path, overwrite: true);
            if (Files.HashFile(path) != expected) throw new InvalidDataException("Extracted helper verification failed.");
            return path;
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }
}
