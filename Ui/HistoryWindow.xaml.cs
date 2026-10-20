using System.Windows;
using AlexLV.WindowsUi;

namespace ClippedImageToPath.Ui;

/// <summary>Every release from CHANGELOG.md, newest first.</summary>
public partial class HistoryWindow : ThemedWindow
{
    public HistoryWindow()
    {
        InitializeComponent();
        Title = $"{AppInfo.DisplayName} Change History";
        foreach (var entry in Changelog.Entries)
        {
            Entries.Children.Add(new System.Windows.Controls.TextBlock { Text = $"{entry.Version} · {entry.Date}", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 8) });
            ReleaseNotesView.Fill(Entries, entry);
        }
        Loaded += (_, _) => CloseButton.Focus();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
