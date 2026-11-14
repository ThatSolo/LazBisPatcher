using System.IO;
using System.Reflection;
using System.Windows;
using LazBisPatcher.Core;

namespace LazBisPatcher.Gui;

public partial class App : Application
{
    private FileStream? lease;
    internal static Version CurrentVersion { get; } = typeof(App).Assembly.GetName().Version is { } v
        ? new(v.Major, v.Minor, v.Build) : throw new InvalidOperationException("Missing patcher build version.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var folder = AppContext.BaseDirectory;
            var updater = new SelfUpdate(folder, BundledHelper.Ensure);
            var trial = e.Args is ["--trial", _, _];
            if (!string.Equals(Path.GetFileName(Environment.ProcessPath), SelfUpdate.AppName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Keep the extracted executable named {SelfUpdate.AppName}.");
            lease = trial ? updater.OpenAppLease() : updater.OpenNormalAppLease();
            if (updater.HasPending && !trial)
            {
                updater.StartHelper(recover: true);
                Shutdown();
                return;
            }
#if FIXTURE_FEED
            bool runFixtureUpdate = e.Args is ["--fixture-update"];
#else
            const bool runFixtureUpdate = false;
#endif
            if (!trial && !runFixtureUpdate && e.Args.Length != 0) throw new InvalidDataException("Unrecognized startup arguments.");
            var repository = typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "PatcherRepository").Value!;
            IReleaseFeed feed = new GitHubFeed(repository);
#if FIXTURE_FEED
            var fixture = Path.Combine(folder, "fixture-feed");
            feed = new FixtureFeed(fixture);
            if (trial && File.Exists(Path.Combine(fixture, "fail-startup"))) { Shutdown(1); return; }
#endif
            var window = new MainWindow(feed, updater);
            MainWindow = window;
            window.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (trial)
            {
                updater.Acknowledge(e.Args[1], e.Args[2], CurrentVersion);
                await updater.AwaitCommitAsync();
            }
#if FIXTURE_FEED
            if (runFixtureUpdate)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var offer = await feed.CheckAsync(Product.Patcher, deadline.Token);
                await updater.PrepareAsync(feed, offer, CurrentVersion, deadline.Token);
                updater.StartHelper(recover: false);
                Shutdown();
                return;
            }
#endif
            if (window.IsVisible) await window.ActivateAsync();
        }
        catch (Exception error)
        {
            if (e.Args is not ["--trial", _, _]) MessageBox.Show(error.Message, "LazBisPatcher", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        lease?.Dispose();
        base.OnExit(e);
    }
}
