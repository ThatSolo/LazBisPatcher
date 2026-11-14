using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using LazBisPatcher.Core;

namespace LazBisPatcher.Gui;

public partial class MainWindow : Window
{
    private readonly IReleaseFeed feed;
    private readonly SelfUpdate updater;
    private readonly string rootPreference = Path.Combine(AppContext.BaseDirectory, "macroquest-folder.txt");
    private Release? appRelease;
    private Release? patcherRelease;
    private InstallObservation? observation;
    private bool busy;
    private bool active;
    private bool switching;
    private string applicationNotes = "";
    private string patcherNotes = "";

    public MainWindow(IReleaseFeed feed, SelfUpdate updater)
    {
        this.feed = feed;
        this.updater = updater;
        InitializeComponent();
        PatcherVersionText.Text = $"Patcher v{App.CurrentVersion}";
        if (File.Exists(rootPreference))
        {
            try { MqDirTextBox.Text = System.Text.Encoding.UTF8.GetString(Files.Read(rootPreference, 8192)); }
            catch (Exception e) { Log($"Saved MacroQuest folder could not be loaded: {e.Message}"); }
        }
        RefreshButtons();
    }

    internal async Task ActivateAsync()
    {
        active = true;
        await CheckAsync();
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e) => await CheckAsync();

    private Task CheckAsync() => RunAsync(async () =>
    {
        appRelease = null;
        patcherRelease = null;
        observation = null;
        SelfUpdateBanner.Visibility = Visibility.Collapsed;
        PatcherVersionText.Text = $"Patcher v{App.CurrentVersion}";
        PatchNotesText.Text = "";
        applicationNotes = patcherNotes = "";
        var path = MqDirTextBox.Text.Trim();
        VersionLine.Text = "Installed: unknown  |  Available: checking";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var patcherTask = CheckPatcherAsync(deadline.Token);
        try
        {
            LazBisInstaller? installer = null;
            if (path.Length > 0)
            {
                installer = new LazBisInstaller(path);
                var recovery = await Task.Run(installer.Recover);
                if (recovery != "No update recovery needed.") Log(recovery);
                observation = await Task.Run(installer.Observe);
                Files.Write(rootPreference, System.Text.Encoding.UTF8.GetBytes(installer.Root));
            }
            VersionLine.Text = $"Installed: {InstalledLabel()}  |  Available: unavailable";
            var release = await feed.CheckAsync(Product.LazBis, deadline.Token);
            applicationNotes = $"LazBis {release.Tag}\n{release.Notes}";
            VersionLine.Text = $"Installed: {InstalledLabel()}  |  Available: {release.Tag}";
            var installed = observation?.Recorded;
            if (installer is null)
            {
                StatusLine.Text = "Choose your MacroQuest folder.";
            }
            else if (installed is null)
            {
                var available = new Version(release.Version.Major, release.Version.Minor, release.Version.Build, 0);
                StatusLine.Text = observation switch
                {
                    { HasExistingFiles: false } => "LazBis is ready to install.",
                    { ReportedVersion: { } version } when version < available =>
                        "An older LazBis copy was found. Review its files before updating.",
                    { ReportedVersion: { } version } when version > available =>
                        "This copy reports a newer version. Check it before continuing.",
                    _ => "A LazBis copy was found. Review its files before continuing."
                };
                appRelease = release;
            }
            else if (release.Version > installed.Release.Version || LazBisInstaller.SameAsset(release, installed.Release))
            {
                var newer = release.Version > installed.Release.Version;
                StatusLine.Text = observation switch
                {
                    { HasExistingFiles: false } => "LazBis files are missing. Install the available release.",
                    { MissingManagedFiles: > 0 } => newer
                        ? "Some LazBis files are missing. Review the available update."
                        : "Some LazBis files are missing. Use Verify / repair to restore them.",
                    _ => newer ? "A LazBis update is available."
                        : "LazBis version is current. Use Verify / repair to check its files."
                };
                appRelease = release;
            }
            else StatusLine.Text = "The release is older than the last verified version, or its asset changed. Ask ThatSolo to check the release.";
        }
        catch (Exception e)
        {
            StatusLine.Text = "LazBis check unavailable.";
            if (VersionLine.Text.EndsWith("checking", StringComparison.Ordinal))
                VersionLine.Text = "Installed: unverified  |  Available: unavailable";
            Log(Message(e));
        }
        finally
        {
            await patcherTask;
            PatchNotesText.Text = string.Join("\n\n", new[] { applicationNotes, patcherNotes }.Where(n => n.Length > 0));
        }
    });

    private async Task CheckPatcherAsync(CancellationToken token)
    {
        try
        {
            var release = await feed.CheckAsync(Product.Patcher, token);
            if (release.Version > App.CurrentVersion)
            {
                patcherRelease = release;
                SelfUpdateText.Text = $"Patcher {release.Tag} is available";
                SelfUpdateBanner.Visibility = Visibility.Visible;
                patcherNotes = $"Patcher {release.Tag}\n{release.Notes}";
            }
        }
        catch (Exception e)
        {
            PatcherVersionText.Text = $"Patcher v{App.CurrentVersion} · Check unavailable";
            Log($"Patcher check: {Message(e)}");
        }
    }

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (appRelease is not { } release) return;
        var root = MqDirTextBox.Text.Trim();
        var completed = false;
        await RunAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var installer = new LazBisInstaller(root);
            var prepared = await Task.Run(() => installer.PrepareAsync(feed, release, deadline.Token));
            var plan = prepared.Preview;
            var action = ApplicationAction();
            if (action == "Install LazBis" && plan.Changes.Any(c => c.Action != "Add")) action = "Review existing LazBis";
            var review = new InstallReviewWindow(plan, action) { Owner = this };
            if (review.ShowDialog() != true) return;
            await Task.Run(prepared.Apply);
            Log($"LazBis {release.Tag} verified and recorded.");
            completed = true;
        });
        if (completed) await CheckAsync();
    }

    private async void SelfUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (patcherRelease is not { } release || busy) return;
        if (MessageBox.Show(this, $"Update patcher {App.CurrentVersion} → {release.Tag}?\n\n{release.Notes[..Math.Min(2000, release.Notes.Length)]}",
            "Update LazBisPatcher", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await RunAsync(async () =>
        {
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, SelfUpdate.HelperName)))
                throw new InvalidDataException("Extract the complete patcher ZIP, including its update helper.");
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await Task.Run(() => updater.PrepareAsync(feed, release, App.CurrentVersion, deadline.Token));
            updater.StartHelper(recover: false);
            switching = true;
            Application.Current.Shutdown();
        });
    }

    private async Task RunAsync(Func<Task> work)
    {
        if (busy || !active) return;
        busy = true;
        RefreshButtons();
        try { await work(); }
        catch (Exception e) { Log(Message(e)); StatusLine.Text = "Review the operation result below."; }
        finally { busy = false; RefreshButtons(); }
    }

    private static string Message(Exception e) => e switch
    {
        OperationCanceledException => "The check or download timed out. Try again.",
        System.Net.Http.HttpRequestException => $"GitHub unavailable: {e.Message}",
        _ => e.Message
    };

    private string InstalledLabel()
    {
        if (observation is null) return "unknown";
        if (!observation.HasExistingFiles) return observation.Recorded is { } previous
            ? $"not installed (last verified: {previous.Release.Tag})" : "not installed";
        if (observation.Recorded is { } recorded) return observation.MissingManagedFiles > 0
            ? $"{recorded.Release.Tag} (incomplete)" : recorded.Release.Tag;
        return observation.ReportedVersion is { } version
            ? $"{version.ToString(version.Revision > 0 ? 4 : 3)} (reported)"
            : "existing copy, version unknown";
    }

    private string ApplicationAction() => observation switch
    {
        { HasExistingFiles: false } => "Install LazBis",
        { Recorded: { } recorded } when appRelease?.Version == recorded.Release.Version => "Verify / repair LazBis",
        { Recorded: not null } => "Update LazBis",
        { HasExistingFiles: true } => "Review existing LazBis",
        _ => "Install or update LazBis"
    };

    private void RefreshButtons()
    {
        if (CheckButton is null) return;
        CheckButton.IsEnabled = active && !busy;
        ActionButton.IsEnabled = active && !busy && appRelease is not null;
        SelfUpdateButton.IsEnabled = active && !busy && patcherRelease is not null;
        BrowseButton.IsEnabled = active && !busy;
        MqDirTextBox.IsEnabled = active && !busy;
        ActionButton.Content = ApplicationAction();
        ActionHint.Text = busy ? "Checking or applying changes…" : appRelease is not null
            ? "You’ll review changes before applying them." : "Choose a MacroQuest folder and check for updates.";
    }
    private void Log(string line)
    {
        var log = LogText.Text + line + "\n";
        LogText.Text = log.Length > 16000 ? log[^16000..] : log;
        LogScroll.ScrollToEnd();
    }
    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Select your MacroQuest folder" };
        if (Directory.Exists(MqDirTextBox.Text)) picker.InitialDirectory = MqDirTextBox.Text;
        if (picker.ShowDialog(this) == true) { MqDirTextBox.Text = picker.FolderName; await CheckAsync(); }
    }
    private void MqDirTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        appRelease = null;
        observation = null;
        if (VersionLine is not null && StatusLine is not null)
        {
            VersionLine.Text = "Installed: unchecked  |  Available: unchecked";
            StatusLine.Text = "Check for updates to review this folder.";
        }
        RefreshButtons();
    }
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    protected override void OnClosing(CancelEventArgs e)
    {
        if (busy && !switching) { e.Cancel = true; ActionHint.Text = "Wait for the current check or update to finish before closing."; }
        base.OnClosing(e);
    }
    private void WindowDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
    private void GitHubLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/ThatSolo/lazbis/releases") { UseShellExecute = true })?.Dispose();
        e.Handled = true;
    }
}
