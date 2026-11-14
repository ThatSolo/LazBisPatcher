using LazBisPatcher.Core;
using System.Runtime.InteropServices;

try
{
    if (args.Length is not (3 or 4) || args[0] is not ("apply" or "recover") ||
        !int.TryParse(args[1], out var pid) || !long.TryParse(args[2], out var ticks))
        throw new InvalidDataException("Start patcher updates from LazBisPatcher.");
    // Three arguments preserve recovery from the older two-file package.
    var updater = new SelfUpdate(args.Length == 4 ? args[3] : AppContext.BaseDirectory);
    await updater.RunHelperAsync(args[0] == "recover", pid, ticks);
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
#if !FIXTURE_FEED
    if (OperatingSystem.IsWindows())
        NativeMessage.Show(IntPtr.Zero, error.Message, "LazBisPatcher update", 0x10);
#endif
    return 1;
}

internal static class NativeMessage
{
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    internal static extern int Show(IntPtr owner, string text, string title, uint flags);
}
