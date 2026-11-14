using System.Windows;
using LazBisPatcher.Core;

namespace LazBisPatcher.Gui;

public partial class InstallReviewWindow : Window
{
    public InstallReviewWindow(InstallPreview plan, string action)
    {
        InitializeComponent();
        var added = plan.Changes.Count(file => file.Action == "Add");
        var replaced = plan.Changes.Count(file => file.Action == "Replace");
        var conflicts = plan.Changes.Where(file => file.Action == "Conflict").ToArray();
        var canApply = conflicts.Length == 0;
        var unverifiedConflict = !canApply && plan.Installed is null;
        const string stop = "MacroQuest can stay open. Run /lua stop lazbis on every character using it. If this copy runs the background helper, also run /lua stop lazbis/bg on those characters.";

        Title = Heading.Text = canApply ? action : unverifiedConflict ? "Existing LazBis copy found" : "Review your existing LazBis files";
        Folder.Text = plan.Root;
        ConfirmButton.Content = action == "Review existing LazBis" ? "Verify LazBis" : action;

        if (canApply)
        {
            Summary.Text = (added, replaced) switch
            {
                (0, 0) => $"Your files match LazBis {plan.Release.Tag}. Confirm to record this version.",
                (0, _) => $"LazBis {plan.Release.Tag} will replace {FileCount(replaced)}.",
                (_, 0) => $"LazBis {plan.Release.Tag} will add {FileCount(added)}.",
                _ => $"LazBis {plan.Release.Tag} will add {FileCount(added)} and replace {FileCount(replaced)}."
            };
            if (plan.Official10Upgrade) Summary.Text = "Official 1.0 files recognized. " + Summary.Text;
            Guidance.Text = added + replaced == 0 ? "No LazBis files need to change." : stop;
        }
        else
        {
            Summary.Text = conflicts.Length == 1
                ? "One file needs review. Your files are unchanged."
                : $"{conflicts.Length} files need review. Your files are unchanged.";
            Guidance.Text = unverifiedConflict
                ? stop + "\n\nTo switch to ThatSolo's version, back up the entire old script folder and its data, then move lua\\lazbis outside lua. Click Check for Updates and install again.\n\nMoving the script folder does not isolate resources\\lazbis.db or config\\lazbis_lockouts.lua. If compatibility is unknown, use a separate MacroQuest test copy with its own data."
                : stop + "\n\nBack up and review the listed local changes before trying again.";
            ConfirmButton.Visibility = Visibility.Collapsed;
            DismissButton.Content = "Close";
            Details.IsExpanded = true;
        }

        FileList.Text = string.Join("\n", plan.Changes.OrderBy(file => file.Action != "Conflict")
            .Select(file => $"{(file.Action == "Conflict" ? "Review" : file.Action)}  {file.Path}"));
        Loaded += (_, _) => DismissButton.Focus();
    }

    private static string FileCount(int count) => count == 1 ? "1 file" : $"{count} files";

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
