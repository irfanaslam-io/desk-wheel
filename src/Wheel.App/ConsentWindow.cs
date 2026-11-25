using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Wheel.App;

/// <summary>
/// Asked once: may Desk Wheel send anonymous usage counts? Says plainly what is and isn't sent. Closing the
/// window counts as "no". The answer can be changed any time in Settings › Privacy.
/// </summary>
internal sealed class ConsentWindow : Window
{
    public ConsentWindow(Action<bool> answer)
    {
        Ui.Window(this, "Help improve Desk Wheel");
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var answered = false;
        void Answer(bool yes)
        {
            if (answered) return;
            answered = true;
            answer(yes);
            Close();
        }

        var yes = Ui.Primary("Share anonymous statistics", () => Answer(true));
        yes.IsDefault = true;
        var no = Ui.Secondary("No thanks", () => Answer(false));
        no.IsCancel = true;
        Closed += (_, _) => Answer(false);

        Content = new StackPanel
        {
            Margin = new Thickness(24, 22, 24, 20), Spacing = 12,
            Children =
            {
                Ui.Title("Help improve Desk Wheel?"),
                Ui.Body("Desk Wheel can send anonymous counts of which tools are used, so the most useful ones get better first."),
                Ui.Card(new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        Ui.Label("What's sent"),
                        Ui.Caption("Tool names (like \"PNG\" or \"Compress PDF\"), how many files (roughly), the app version and Mac or Windows."),
                        Ui.Label("Never sent"),
                        Ui.Caption("Your files, file names, folders, screenshots, clipboard or anything you type. Conversions always happen on your computer."),
                    },
                }, new Thickness(14, 12)),
                Ui.Caption("You can change this any time in Settings › Privacy."),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { no, yes } },
            },
        };
    }
}
