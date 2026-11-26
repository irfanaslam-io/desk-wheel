using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Wheel.App.Platform;
using Wheel.Core;
using Wheel.Core.Capture;
using Wheel.Core.Converters;

namespace Wheel.App;

/// <summary>
/// Settings, laid out like a system preferences window: sections on the left, grouped rows on the right
/// (what it is and what it does on the left of each row, the control on the right). Every change is saved
/// at once and applied through <see cref="Changed"/>; there is no Save button to forget.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IPlatform _platform;
    private readonly ContentControl _page = new();
    private readonly StackPanel _watchList = new();
    private readonly ListBox _nav = new() { Background = Brushes.Transparent, Margin = new Thickness(8, 0) };
    private readonly List<(string Name, IconKind Icon, Func<Control> Build)> _sections = new();

    /// <summary>Raised after any setting changes (already saved).</summary>
    public event Action? Changed;

    private readonly Updater? _updater;

    public SettingsWindow(AppSettings settings, IPlatform platform, Updater? updater = null)
    {
        _settings = settings;
        _platform = platform;
        _updater = updater;
        Ui.Window(this, "Desk Wheel Settings");
        Width = 860;
        Height = 620;
        MinWidth = 700;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _sections.Add(("General", IconKind.Wheel, General));
        _sections.Add(("Screenshots", IconKind.Image, Screenshots));
        _sections.Add(("Conversions", IconKind.Refresh, Conversions));
        _sections.Add(("Clipboard", IconKind.Clipboard, ClipboardSection));
        _sections.Add(("Watched folders", IconKind.Folder, WatchedFolders));
        if (Updater.Available) _sections.Add(("Updates", IconKind.Refresh, UpdatesSection));
        if (BuildConfig.StatsAvailable) _sections.Add(("Privacy", IconKind.Eye, PrivacySection));
        _sections.Add(("Advanced", IconKind.Settings, Advanced));

        _nav.ItemsSource = _sections.Select(s => new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(2, 1),
            Children = { Ui.Icon(s.Icon, 16), new TextBlock { Text = s.Name, VerticalAlignment = VerticalAlignment.Center } },
        }).ToList();
        _nav.SelectionChanged += (_, _) => ShowSection(Math.Max(0, _nav.SelectedIndex));

        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(20, 20, 16, 18),
            Children =
            {
                AppIcons.Picture is { } picture
                    ? new Image { Source = picture, Width = 36, Height = 36, VerticalAlignment = VerticalAlignment.Center }
                    : new Border
                    {
                        Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(5), BorderBrush = Ui.Accent,
                        Child = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Ui.Accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    },
                new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Children = { Ui.Heading(Brand.Name), Ui.Caption(Brand.Tagline, wrap: false) } },
            },
        };
        var version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "";
        var footer = new StackPanel
        {
            Margin = new Thickness(20, 12, 16, 18), Spacing = 2,
            Children =
            {
                Ui.Caption($"Version {version}", wrap: false),
                Ui.Caption("Free for individuals and small businesses", wrap: false),
                new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(0, 8, 0, 6) },
                FooterLink("Powered by " + Brand.PoweredBy, Brand.Website, "Open " + Brand.Website),
                FooterLink("Support: " + Brand.SupportEmail, Links.SupportMailto(version), "Email " + Brand.SupportEmail),
            },
        };

        var sidebar = new DockPanel { Background = Ui.Surface };
        DockPanel.SetDock(brand, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        sidebar.Children.Add(brand);
        sidebar.Children.Add(footer);
        sidebar.Children.Add(_nav);

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("220,1,*") };
        layout.Children.Add(sidebar);
        var line = new Border { Background = Ui.Line };
        Grid.SetColumn(line, 1);
        layout.Children.Add(line);
        var scroll = new ScrollViewer { Content = _page };
        Grid.SetColumn(scroll, 2);
        layout.Children.Add(scroll);
        Content = layout;

        _nav.SelectedIndex = 0;
    }

    /// <summary>A small text link in the sidebar footer: opens the website, or a new email to support.</summary>
    private Control FooterLink(string text, string uri, string tip)
    {
        var label = new TextBlock { Text = text, FontSize = Ui.CaptionSize, Foreground = Ui.Muted, TextTrimming = TextTrimming.CharacterEllipsis };
        var link = new Button
        {
            Content = label, Padding = new Thickness(0, 1), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), HorizontalAlignment = HorizontalAlignment.Left,
        };
        link.Classes.Add("ghost");
        ToolTip.SetTip(link, tip);
        link.PointerEntered += (_, _) => { label.Foreground = Ui.AccentText; label.TextDecorations = TextDecorations.Underline; };
        link.PointerExited += (_, _) => { label.Foreground = Ui.Muted; label.TextDecorations = null; };
        link.Click += (_, _) => Links.Open(uri);
        return link;
    }

    /// <summary>Opens a section (0 = General … 5 = Advanced).</summary>
    public void SelectSection(int index) => _nav.SelectedIndex = Math.Clamp(index, 0, _sections.Count - 1);

    private void ShowSection(int index)
    {
        var (name, _, build) = _sections[index];
        Usage.Screen("settings-" + name);
        // One readable column: as wide as the window allows, up to 720.
        var column = new StackPanel { Spacing = 18, Children = { Ui.Title(name), build() } };
        var grid = new Grid { Margin = new Thickness(32, 26, 32, 32) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star) { MaxWidth = 720 });
        grid.ColumnDefinitions.Add(new ColumnDefinition(0, GridUnitType.Auto));
        grid.Children.Add(column);
        _page.Content = grid;
    }

    // ---------- sections ----------

    private Control General()
    {
        var mac = OperatingSystem.IsMacOS();
        var login = new ToggleSwitch { IsChecked = SafeLoginState(), OnContent = null, OffContent = null };
        login.IsCheckedChanged += (_, _) =>
        {
            try { _platform.StartAtLogin = login.IsChecked == true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Write(ex); }
        };
        var theme = Choice(new[] { "Match system", "Light", "Dark" }, _settings.Theme switch { "light" => 1, "dark" => 2, _ => 0 },
            i => _settings.Theme = i switch { 1 => "light", 2 => "dark", _ => "system" });
        return Stack(
            Group("Appearance",
                Row("Theme", "Match system follows macOS / Windows, and switches with it.", theme)),
            Group(null,
                Row("Desk Wheel is on", "Turn off to pause every shortcut and the floating button.", Toggle(_settings.Enabled, v => _settings.Enabled = v)),
                Row("Floating button", "The orange button at the edge of the screen: drop files on it, click for the wheel, right-click for the clipboard.",
                    Toggle(_settings.ShowButton, v => _settings.ShowButton = v)),
                PackageInfo.IsPackaged
                    ? Row("Start when I sign in", "Switched on or off in Windows Settings › Apps › Startup.",
                        Ui.Secondary("Open", () => Links.Open(PackageInfo.StartupSettings)))
                    : Row(mac ? "Open at login" : "Start when I sign in", "Desk Wheel starts in the background, ready to use.", login)),
            Group("Shortcuts",
                Row("Open Desk Wheel for the selected files", null, Keys(_platform.HotkeyName)),
                Row("Clipboard history", null, Keys(mac ? "⌥⇧V" : "Alt+Shift+V")),
                Row("Screenshot of an area", null, Keys(_platform.CaptureShortcut(CaptureKind.Area))),
                Row("Screenshot of a window", null, Keys(_platform.CaptureShortcut(CaptureKind.Window))),
                Row("Screenshot of the screen", null, Keys(_platform.CaptureShortcut(CaptureKind.FullScreen)))));
    }

    private Control Screenshots()
    {
        var rows = new List<Control>
        {
            FolderRow("Save to", "Screenshots, recordings and QR codes go here.",
                () => _settings.CaptureFolder ?? CaptureNaming.DefaultFolder(), p => _settings.CaptureFolder = p, canReset: true),
            Row("Format", "PNG keeps every detail; JPG files are smaller.",
                Choice(new[] { "PNG", "JPG" }, _settings.CaptureFormat == "jpg" ? 1 : 0, i => _settings.CaptureFormat = i == 1 ? "jpg" : "png")),
            Row("Copy to the clipboard", "Each screenshot is ready to paste right away.", Toggle(_settings.CaptureToClipboard, v => _settings.CaptureToClipboard = v)),
            Row("Mark up area screenshots right away", "After you select an area, draw arrows, boxes, text or blur on it, then press Enter to save and copy.", Toggle(_settings.EditAfterCapture, v => _settings.EditAfterCapture = v)),
            Row("Show the preview card", "Copy, edit, pin or drag the screenshot from a card in the corner.", Toggle(_settings.ShowCapturePreview, v => _settings.ShowCapturePreview = v)),
        };
        if (OperatingSystem.IsMacOS())
            rows.Add(Row("Window shadow", "Keep the soft shadow around window screenshots.", Toggle(_settings.WindowShadow, v => _settings.WindowShadow = v)));
        return Group(null, rows.ToArray());
    }

    private Control Conversions()
    {
        var desktopWheel = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "DeskWheel");
        var where = Choice(new[] { "Beside the original", "In one folder" }, _settings.ConvertFolder is null ? 0 : 1, _ => { });
        var folderRow = FolderRow("Folder", "Used when results go into one folder.",
            () => _settings.ConvertFolder ?? desktopWheel, p => _settings.ConvertFolder = p ?? desktopWheel, canReset: false);
        folderRow.IsEnabled = _settings.ConvertFolder is not null;
        ((ComboBox)where).SelectionChanged += (_, _) =>
        {
            var inFolder = ((ComboBox)where).SelectedIndex == 1;
            _settings.ConvertFolder = inFolder ? _settings.ConvertFolder ?? desktopWheel : null;
            folderRow.IsEnabled = inFolder;
            Save();
        };

        var quality = new TextBlock { Text = _settings.ImageQuality.ToString(), MinWidth = 28, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        var slider = new Slider { Minimum = 50, Maximum = 100, Value = _settings.ImageQuality, TickFrequency = 5, IsSnapToTickEnabled = true, Width = 200 };
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            _settings.ImageQuality = (int)slider.Value;
            quality.Text = _settings.ImageQuality.ToString();
            Save();
        };

        return Stack(
            Group("Results",
                Row("Save results", "Originals are never changed or overwritten.", where),
                folderRow,
                Row("JPG and WebP quality", "Higher is sharper; lower makes smaller files.", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { slider, quality } })),
            Group("Helper programs",
                FfmpegRow(),
                Status("Office documents", "Word, Excel and PowerPoint to PDF uses your Microsoft Office, or LibreOffice (free)", OfficeFound(), "Microsoft Office, or LibreOffice from libreoffice.org"),
                Status("Text recognition", "Text on the Screenshot and Tools wheels", OcrEngine.IsAvailable ? OcrEngine.Description : null, OcrEngine.InstallHint)));
    }

    private Control ClipboardSection() => Stack(
        Group(null,
            Row("Remember what I copy", "Text, links, images and files, so you can paste something from earlier.",
                Toggle(_settings.ClipboardHistory, v => _settings.ClipboardHistory = v)),
            Row("Open the history", "Or right-click the floating button, or choose Clipboard on the Screenshot wheel.",
                Keys(OperatingSystem.IsMacOS() ? "⌥⇧V" : "Alt+Shift+V"))),
        Notice(IconKind.Info, "Private by design",
            "The history stays on this computer. Copies that password managers mark as private (1Password, Bitwarden, KeePass and others) are never recorded. The last 300 items are kept; pinned items stay until you unpin them."));

    private Control WatchedFolders()
    {
        var choice = new ComboBox { ItemsSource = Wheel.Core.Files.WatchChoices.All.Select(c => c.Label).ToArray(), SelectedIndex = 0, MinWidth = 240 };
        var add = Ui.Primary("Add folder…", async () =>
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Folder to watch" });
            if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } folder) return;
            var c = Wheel.Core.Files.WatchChoices.All[Math.Max(0, choice.SelectedIndex)];
            _settings.WatchFolders.RemoveAll(r => r.Folder == folder && r.OptionId == c.OptionId);
            _settings.WatchFolders.Add(new Wheel.Core.Files.WatchRule(folder, c.OptionId));
            Save();
            RefreshWatchList();
        }, IconKind.Plus);
        RefreshWatchList();
        return Stack(
            Ui.Caption("New files that arrive in these folders are converted automatically. Results are saved beside them; Desk Wheel's own results and files still downloading are skipped."),
            Ui.Card(_watchList, new Thickness(16, 4)),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { choice, add } });
    }

    private Control Advanced() => Stack(
        Group("Keyboard gesture",
            Row("Drag a file, then tap a key", OperatingSystem.IsMacOS()
                    ? "Shift, ⌘ or ⌃ while dragging opens the Convert, Tools or Actions wheel. Needs the Input Monitoring permission."
                    : "Shift, Ctrl or Alt while dragging opens the Convert, Tools or Actions wheel.",
                Toggle(_settings.KeyGesture, v => _settings.KeyGesture = v)),
            Row($"Gesture in every app", $"Off: only in {_platform.FileManagerName} and on the desktop, to avoid accidental wheels.",
                Toggle(_settings.WorkInAllApps, v => _settings.WorkInAllApps = v))),
        Group("Support",
            Row("Settings and log folder", "Settings, history and the error log. Nothing in it leaves this computer.",
                Ui.Secondary("Open", () =>
                {
                    Directory.CreateDirectory(_platform.DataFolder);
                    _platform.OpenFolder(_platform.DataFolder);
                }, IconKind.Folder))));

    // ---------- updates ----------

    private Control UpdatesSection()
    {
        var status = Ui.Caption(UpdateStatus());
        var action = Ui.Primary("Check now", () => { }, IconKind.Refresh);
        action.Click += async (_, _) =>
        {
            if (_updater is null) return;
            if (_updater.Latest is { Available: true })
            {
                await _updater.InstallAsync();
                return;
            }
            action.IsEnabled = false;
            status.Text = "Checking…";
            await _updater.CheckAsync();
            status.Text = UpdateStatus();
            action.IsEnabled = true;
            if (_updater.Latest is { Available: true } u) action.Content = $"Install {u.Info!.Version}";
        };
        if (_updater?.Latest is { Available: true } found) action.Content = $"Install {found.Info!.Version}";
        return Stack(
            Group("Desk Wheel " + BuildConfig.Version,
                Row("Updates", null, action),
                new Border { Padding = new Thickness(0, 0, 0, 12), Child = status }),
            Group("Automatic",
                Row("Check for updates automatically", "Twice a day at most. You're always asked before anything is installed.",
                    Toggle(_settings.AutoUpdate, v => _settings.AutoUpdate = v))),
            Ui.Caption("Updates install over the current version: no need to uninstall, and your settings, history and clipboard are kept. Each update is checked against Desk Wheel's signature before it's installed."),
            Group("Optional downloads", MediaPlayerRow()));
    }

    /// <summary>The media player's engine: downloaded the first time a video or song is played, removable here.</summary>
    private Control MediaPlayerRow()
    {
        var pack = MediaEngine.Pack;
        var caption = Ui.Caption("");
        var button = Ui.Secondary("", () => { });
        void Refresh()
        {
            var installed = MediaEngine.Packs.IsInstalled(pack);
            var download = pack.ForThisComputer;
            caption.Text = installed
                ? $"Downloaded · {Wheel.Core.Disk.DiskScanner.FormatSize(MediaEngine.Packs.SizeOnDisk(pack))} · VLC {pack.Version.Split('-')[0]} by VideoLAN (LGPL)"
                : download is null ? "Not available for this computer yet."
                : $"Not downloaded. It downloads ({Wheel.Core.Disk.DiskScanner.FormatSize(download.Size)}) the first time you play a video or song, after asking.";
            button.Content = installed ? "Remove" : "Download";
            button.IsVisible = installed || download is not null;
        }
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                if (MediaEngine.Packs.IsInstalled(pack))
                {
                    try { MediaEngine.Packs.Remove(pack); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Log.Write(ex);
                        caption.Text = "In use: it will be removed the next time Desk Wheel starts.";
                        return;
                    }
                }
                else
                {
                    var progress = new Progress<Wheel.Core.Packs.PackProgress>(p =>
                        caption.Text = p.Unpacking ? "Unpacking…" : $"Downloading… {Wheel.Core.Disk.DiskScanner.FormatSize(p.Done)} of {Wheel.Core.Disk.DiskScanner.FormatSize(p.Total)}");
                    try { await MediaEngine.Packs.InstallAsync(pack, progress); }
                    catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or InvalidOperationException)
                    {
                        Log.Write(ex);
                        caption.Text = ex.Message;
                        return;
                    }
                }
                Refresh();
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        Refresh();
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { Ui.Body("Media player engine", wrap: false), caption } };
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Margin = new Thickness(20, 0, 0, 0);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 12), MinHeight = 32 };
        row.Children.Add(text);
        Grid.SetColumn(button, 1);
        row.Children.Add(button);
        return row;
    }

    private string UpdateStatus()
    {
        var last = _settings.LastUpdateCheck is { } t ? $"Last checked {t.ToLocalTime():d MMM, HH:mm}." : "Not checked yet.";
        return _updater?.Latest switch
        {
            { Available: true } u => $"Version {u.Info!.Version} is available." + (string.IsNullOrWhiteSpace(u.Info.Notes) ? "" : "\n" + u.Info.Notes),
            { Problem: { } problem } => problem,
            not null => $"You're up to date. {last}",
            null => last,
        };
    }

    // ---------- privacy ----------

    private Control PrivacySection() => Stack(
        Group("Usage statistics",
            Row("Share anonymous usage statistics", "Counts of which tools are used, so the most useful ones get better first.",
                Toggle(_settings.ShareUsage == true, v =>
                {
                    _settings.ShareUsage = v;
                    if (!v) Usage.Forget();
                }))),
        Group("What's sent",
            new StackPanel
            {
                Spacing = 6, Margin = new Thickness(0, 12),
                Children =
                {
                    Ui.Body("Tool names (like \"PNG\" or \"Compress PDF\"), roughly how many files, screenshot type, the app version and Mac or Windows. A random id tells visits apart; it isn't linked to you."),
                    Ui.Caption("Never sent: your files, file names, folders, screenshots, clipboard or anything you type. Conversions always happen on your computer."),
                },
            }));

    // ---------- watched folders list ----------

    private void RefreshWatchList()
    {
        _watchList.Children.Clear();
        var rules = _settings.WatchFolders.ToList();
        if (rules.Count == 0)
        {
            _watchList.Children.Add(new StackPanel
            {
                Margin = new Thickness(0, 14), Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center,
                Children = { Ui.Icon(IconKind.Folder, 22, Ui.Faint), Ui.Caption("No watched folders yet", wrap: false) },
            });
            return;
        }
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (i > 0) _watchList.Children.Add(Ui.Divider());
            var remove = Ui.IconButton(IconKind.Trash, "Stop watching this folder", () =>
            {
                _settings.WatchFolders.Remove(rule);
                Save();
                RefreshWatchList();
            });
            var text = new StackPanel
            {
                Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    Ui.Body(Wheel.Core.Files.WatchChoices.LabelFor(rule.OptionId), wrap: false),
                    new TextBlock { Text = rule.Folder, FontSize = Ui.CaptionSize, Foreground = Ui.Muted, TextTrimming = TextTrimming.PrefixCharacterEllipsis },
                },
            };
            var row = new DockPanel { Margin = new Thickness(0, 10) };
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { Ui.Icon(IconKind.Folder, 18, Ui.Muted), text } });
            _watchList.Children.Add(row);
        }
    }

    // ---------- building blocks ----------

    private static StackPanel Stack(params Control[] children)
    {
        var s = new StackPanel { Spacing = 18 };
        foreach (var c in children) s.Children.Add(c);
        return s;
    }

    /// <summary>A titled card of rows separated by hairlines.</summary>
    private static Control Group(string? title, params Control[] rows)
    {
        var list = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0) list.Children.Add(Ui.Divider());
            list.Children.Add(rows[i]);
        }
        var card = Ui.Card(list);
        return title is null ? card : new StackPanel { Spacing = 8, Children = { Ui.Label(title), card } };
    }

    /// <summary>Title and description on the left, the control on the right.</summary>
    private static Control Row(string title, string? description, Control control)
    {
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { Ui.Body(title, wrap: false) } };
        if (description is not null) text.Children.Add(Ui.Caption(description));
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(20, 0, 0, 0);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 12), MinHeight = 32 };
        row.Children.Add(text);
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private ToggleSwitch Toggle(bool value, Action<bool> set)
    {
        var t = new ToggleSwitch { IsChecked = value, OnContent = null, OffContent = null, MinWidth = 0 };
        t.IsCheckedChanged += (_, _) =>
        {
            set(t.IsChecked == true);
            Save();
        };
        return t;
    }

    private Control Choice(string[] options, int selected, Action<int> set)
    {
        var combo = new ComboBox { ItemsSource = options, SelectedIndex = selected, MinWidth = 180 };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0) return;
            set(combo.SelectedIndex);
            Save();
        };
        return combo;
    }

    /// <summary>Key caps, e.g. ⌥⇧V or Alt+Shift+V.</summary>
    private static Control Keys(string keys)
    {
        // "Alt+Shift+V" → Alt · Shift · V;  "⌥⇧V" / "⌥ Space" → ⌥ · ⇧ · V / ⌥ · Space
        string[] parts;
        if (keys.Contains('+'))
            parts = keys.Split('+');
        else
        {
            var mods = keys.TakeWhile(c => "⌘⌥⇧⌃ ".Contains(c)).Where(c => c != ' ').Select(c => c.ToString());
            var rest = new string(keys.SkipWhile(c => "⌘⌥⇧⌃ ".Contains(c)).ToArray()).Trim();
            parts = mods.Append(rest).ToArray();
        }
        if (parts.All(p => p.Trim().Length == 0))
            return Ui.Caption("Not set", wrap: false);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var p in parts.Where(p => p.Length > 0))
            panel.Children.Add(new Border
            {
                MinWidth = 24, Padding = new Thickness(7, 2), CornerRadius = new CornerRadius(5),
                Background = Ui.Raised, BorderBrush = Ui.Line, BorderThickness = new Thickness(1, 1, 1, 2),
                Child = new TextBlock { Text = p, FontSize = 12, Foreground = Ui.Text, HorizontalAlignment = HorizontalAlignment.Center },
            });
        return panel;
    }

    /// <summary>A path (shown, not typed) with Change… and optionally Reset.</summary>
    private Control FolderRow(string title, string description, Func<string> current, Action<string?> set, bool canReset)
    {
        var path = new TextBlock
        {
            Text = current(), FontSize = Ui.CaptionSize, Foreground = Ui.Muted, TextTrimming = TextTrimming.PrefixCharacterEllipsis,
        };
        ToolTip.SetTip(path, current());
        void Update()
        {
            path.Text = current();
            ToolTip.SetTip(path, current());
            Save();
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Ui.Secondary("Change…", async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is { } chosen)
            {
                set(chosen);
                Update();
            }
        }));
        if (canReset)
            buttons.Children.Add(Ui.Ghost("Reset", () => { set(null); Update(); }, tip: "Back to the default folder"));

        var text = new StackPanel { Spacing = 3, Children = { Ui.Body(title, wrap: false), Ui.Caption(description), path } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 12) };
        row.Children.Add(text);
        buttons.Margin = new Thickness(20, 0, 0, 0);
        buttons.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(buttons, 1);
        row.Children.Add(buttons);
        return row;
    }

    private Control FfmpegRow()
    {
        var holder = new ContentControl();
        void Refresh()
        {
            var found = FfmpegConverter.Locate();
            if (found is not null && found.StartsWith(AppContext.BaseDirectory, StringComparison.Ordinal))
                found = "Built into Desk Wheel";
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children =
                {
                    Ui.Secondary("Choose…", async () =>
                    {
                        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose the ffmpeg program", AllowMultiple = false });
                        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
                        {
                            _settings.FfmpegPath = path;
                            Save();
                            Refresh();
                        }
                    }),
                    Ui.Ghost("Auto", () => { _settings.FfmpegPath = null; Save(); Refresh(); }, tip: "Find ffmpeg automatically"),
                },
            };
            holder.Content = StatusRow("Video and audio (ffmpeg)", "Video, audio, AVIF and screen recording", found,
                OperatingSystem.IsMacOS() ? "brew install ffmpeg-full" : "winget install Gyan.FFmpeg", buttons);
        }
        Refresh();
        return holder;
    }

    /// <summary>"Microsoft Word, Excel and PowerPoint", plus LibreOffice when it's there too; null when neither.</summary>
    private static string? OfficeFound()
    {
        var apps = MicrosoftOffice.Installed();
        var names = new[] { OfficeApp.Word, OfficeApp.Excel, OfficeApp.PowerPoint }.Where(apps.Contains).Select(a => a.ToString()).ToList();
        var parts = new List<string>();
        if (names.Count > 0)
            parts.Add("Microsoft " + (names.Count == 1 ? names[0] : string.Join(", ", names[..^1]) + " and " + names[^1]));
        if (OfficeConverter.LocateLibreOffice() is { } lo)
            parts.Add(names.Count > 0 ? "LibreOffice as a fallback" : lo);
        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    private static Control Status(string title, string what, string? found, string install) => StatusRow(title, what, found, install, null);

    /// <summary>A helper program: found (with where) or not installed (with how to install).</summary>
    private static Control StatusRow(string title, string what, string? found, string install, Control? extra)
    {
        var badge = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center,
            Background = found is null ? Ui.NeutralSoft : Ui.SuccessSoft,
            Child = new TextBlock
            {
                Text = found is null ? "Not installed" : "Ready", FontSize = 11.5, FontWeight = FontWeight.SemiBold,
                Foreground = found is null ? Ui.Muted : Ui.SuccessText,
            },
        };
        var detail = new TextBlock
        {
            Text = found ?? $"Install: {install}", FontSize = Ui.CaptionSize, Foreground = Ui.Faint, TextTrimming = TextTrimming.PrefixCharacterEllipsis,
        };
        ToolTip.SetTip(detail, found ?? install);
        var text = new StackPanel { Spacing = 3, Children = { Ui.Body(title, wrap: false), Ui.Caption(what), detail } };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
        if (extra is not null) right.Children.Add(extra);
        right.Children.Add(badge);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 12) };
        row.Children.Add(text);
        Grid.SetColumn(right, 1);
        row.Children.Add(right);
        return row;
    }

    private static Control Notice(IconKind icon, string title, string text) => new Border
    {
        Background = Ui.InfoSoft, BorderBrush = Ui.InfoLine,
        BorderThickness = new Thickness(1), CornerRadius = Ui.CardRadius, Padding = new Thickness(14, 12),
        Child = new DockPanel
        {
            Children =
            {
                new ContentControl { Content = Ui.Icon(icon, 18, Ui.Info), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) },
                new StackPanel { Spacing = 3, Children = { Ui.Body(title, wrap: false), Ui.Caption(text) } },
            },
        },
    };

    private bool SafeLoginState()
    {
        try { return _platform.StartAtLogin; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private void Save()
    {
        _settings.Save();
        Changed?.Invoke();
    }
}
