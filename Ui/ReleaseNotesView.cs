using System.Windows;
using System.Windows.Controls;

namespace ClippedImageToPath.Ui;

/// <summary>Draws a changelog entry as section headings and bullets (About tab).</summary>
internal static class ReleaseNotesView
{
    internal static void Fill(Panel panel, ChangelogEntry? entry)
    {
        if (entry is null)
        {
            panel.Children.Add(Text("No release notes available for this version.", "MutedText"));
            return;
        }

        foreach (var section in entry.Sections)
        {
            if (section.Heading is not null)
            {
                var heading = Text(section.Heading, "StrongText");
                heading.FontSize = 13;
                heading.Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 8, 0, 4);
                panel.Children.Add(heading);
            }

            foreach (var bullet in section.Bullets)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var dot = new TextBlock { Text = "•", Margin = new Thickness(2, 0, 8, 0), FontSize = 13 };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                row.Children.Add(new TextBlock { Text = bullet, TextWrapping = TextWrapping.Wrap, FontSize = 13 });
                panel.Children.Add(row);
            }
        }
    }

    private static TextBlock Text(string text, string style)
    {
        var block = new TextBlock { Text = text };
        block.SetResourceReference(FrameworkElement.StyleProperty, style);
        return block;
    }
}
