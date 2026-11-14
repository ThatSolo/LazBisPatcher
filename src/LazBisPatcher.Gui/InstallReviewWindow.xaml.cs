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

        Title = Heading.Text = canApply ? action : "Review your existing LazBis files";
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
            Guidance.Text = added + replaced == 0 ? "No LazBis files need to change."
                : action == "Install LazBis" ? "If MacroQuest is open, close it before installing."
                : "Close MacroQuest, or stop LazBis and its background helper on every character before continuing.";
        }
        else
        {
            Summary.Text = conflicts.Length == 1
                ? "One file needs review. Your files are unchanged."
                : $"{conflicts.Length} files need review. Your files are unchanged.";
            Guidance.Text = "Keep a backup and review the listed files before trying again.";
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
