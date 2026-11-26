using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using SkiaSharp;
using Wheel.App.Platform;
using Wheel.Core;
using Wheel.Core.Capture;

namespace Wheel.App;

/// <summary>
/// The app, shared by macOS and Windows: the floating button, the shortcut, the menu-bar / tray icon,
/// the optional key gesture, and result cards.
/// </summary>
internal sealed class App : Application, INotifier, IScreenToolHost
{
    private const string IdleTip = "Desk Wheel: " + Wheel.Core.Brand.Tagline;

    private IPlatform _platform = null!;
    private AppSettings _settings = null!;
    private Capabilities _caps = new(false);
    private JobRunner _jobs = null!;
    private TrayIcon? _tray;
    private WheelWindow? _wheel;
    private ToastWindow? _toast;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Ui.Install(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();

        _platform = IPlatform.Create();
        _settings = AppSettings.Load(_platform.DataFolder);
        ApplySettings();
        Usage.Start(_settings, _platform.DataFolder);
        _caps = Capabilities.Detect() with { HasOcr = OcrEngine.IsAvailable, HasVision = OcrEngine.CanRemoveBackground };
        Log.Trace($"Text recognition: {OcrEngine.Description}");
        Log.Trace($"Markdown reader: {(_caps.HasMarkdown ? Wheel.Core.Markdown.MarkdownAssets.FindFolder() : "page files missing from " + System.IO.Path.Combine(AppContext.BaseDirectory, "engines", "markdown"))}");
        if (Program.Debug)
            _ = Task.Run(async () =>
            {
                try { Log.Trace($"  Vision languages: {await OcrEngine.LanguagesAsync()}"); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException) { Log.Write(ex); }
            });
        _jobs = new JobRunner(this, _platform);

        if (Program.View is { } view)
            StartViewer(view);
        else if (Program.Files.Length > 0)
            StartOneShot(Program.Files);
        else
            StartBackground();
    }

    // ---------- background app (menu bar / tray) ----------

    private BubbleWindow? _button;

    private void StartBackground()
    {
        _tray = new TrayIcon { Icon = AppIcons.Tray ?? MakeIcon(), ToolTipText = IdleTip, Menu = BuildMenu(), IsVisible = true };
        if (OperatingSystem.IsMacOS() && AppIcons.Tray is not null)
            MacOSProperties.SetIsTemplateIcon(_tray, true); // macOS tints it to match the menu bar
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
        Log.Trace($"Menu-bar/tray icon created. ffmpeg: {(_caps.HasFfmpeg ? Wheel.Core.Converters.FfmpegConverter.Locate() : "not found")}, sips: {_caps.HasSips}");
        Log.Trace($"Settings: on={_settings.Enabled}, button={_settings.ShowButton}, key gesture={_settings.KeyGesture}, folder={_platform.DataFolder}");

        _platform.HotkeyPressed += OnHotkey;
        _platform.CaptureHotkeyPressed += kind => _ = CaptureAsync(kind);
        _platform.ClipboardHotkeyPressed += OpenClipboard;
        _clipboard = new ClipboardMonitor(_platform, _platform.DataFolder);
        if (_settings.ClipboardHistory) _clipboard.Start();
        _watch = new WatchService(_jobs, () => _caps);
        _watch.Apply(_settings.WatchFolders);
        _platform.WheelKeyTapped += OnWheelKey;
        _platform.EscapePressed += () => _wheel?.CloseWheel();

        var hotkey = _platform.RegisterHotkey(out var hotkeyProblem);
        Log.Trace(hotkey ? $"Shortcut ready: {_platform.HotkeyName}" : $"Shortcut unavailable: {hotkeyProblem}");

        if (_settings.KeyGesture)
            StartKeyGesture();
        if (_settings.ShowButton)
            ShowButton();

        StartUsageAndUpdates();

        if (!_settings.SeenWelcome)
        {
            Notify("Desk Wheel is ready",
                   "Click the orange button at the edge of your screen for screenshots (click the wheel's centre for more), or drop a file on it to convert it.");
            _settings.SeenWelcome = true;
            _settings.Save();
        }
    }

    // ---------- usage statistics and updates ----------

    private Updater? _updater;
    private NativeMenuItem? _updateItem;
    private Avalonia.Threading.DispatcherTimer? _housekeeping;

    private void StartUsageAndUpdates()
    {
        Usage.Track("app_start");
        _updater = new Updater(_settings, (title, text, error, click) => Avalonia.Threading.Dispatcher.UIThread.Post(() => NotifyWithAction(title, text, error, click)), Quit);
        _updater.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshUpdateItem);

        // Ask about statistics once, a little after the first start (not on top of the welcome card).
        if (Usage.ShouldAsk)
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                if (!Usage.ShouldAsk) return;
                new ConsentWindow(yes =>
                {
                    _settings.ShareUsage = yes;
                    _settings.Save();
                    if (yes) { Usage.Track("app_start"); _ = Usage.FlushAsync(); } else Usage.Forget();
                }).Show();
            }, TimeSpan.FromSeconds(_settings.SeenWelcome ? 8 : 40));

        // Every 30 minutes: send waiting statistics, look for an update (the updater itself checks at most twice a day).
        async void Housekeeping()
        {
            try
            {
                await Usage.FlushAsync();
                if (_updater is not null) await _updater.CheckInBackgroundAsync();
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or IOException) { Log.Write(ex); }
        }
        Avalonia.Threading.DispatcherTimer.RunOnce(Housekeeping, TimeSpan.FromSeconds(45));
        _housekeeping = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _housekeeping.Tick += (_, _) => Housekeeping();
        _housekeeping.Start();
    }

    /// <summary>The tray menu's update line: "Check for updates…" or "Install update 0.18.0".</summary>
    private void RefreshUpdateItem()
    {
        if (_updateItem is null || _updater is null) return;
        _updateItem.Header = _updater.Latest is { Available: true } u ? $"Install update {u.Info!.Version}" : "Check for updates…";
    }

    private async Task UpdateFromMenuAsync()
    {
        if (_updater is null) return;
        if (_updater.Latest is not { Available: true })
        {
            var result = await _updater.CheckAsync();
            if (!result.Available)
            {
                Notify(result.Problem is null ? "You're up to date" : "Couldn't check for updates",
                       result.Problem ?? $"Desk Wheel {BuildConfig.Version} is the latest version.", error: result.Problem is not null);
                return;
            }
        }
        await _updater.InstallAsync();
    }

    private void NotifyWithAction(string title, string text, bool error, Action? click)
    {
        Log.Trace($"Card: {title}: {text}");
        _toast?.Close();
        _toast = new ToastWindow(title, text, error, click);
        _toast.ShowInCorner();
    }

    /// <summary>The optional "drag a file, tap Shift" gesture (needs Input Monitoring on macOS).</summary>
    private void StartKeyGesture()
    {
        var listening = _platform.StartListening(out var problem);
        Log.Trace(listening ? "Key gesture: ON (permission granted)" : $"Key gesture: OFF. {problem}");
        if (!listening)
            Notify("The key gesture needs a permission", problem ?? "Couldn't watch the keyboard.", error: true);
    }

    private void ShowButton()
    {
        if (_button is not null) return;
        _button = new BubbleWindow();
        _button.FilesDraggedIn += centre =>
        {
            if (!_settings.Enabled || _wheel is not null) return;
            Log.Trace("A file was dragged onto the floating button.");
            OpenWheel(new WheelWindow(_platform, _caps, WheelMode.Convert, Array.Empty<string>(), clickMode: false),
                      w => w.ShowAt(centre, closeWhenButtonUp: false));
        };
        _button.RightClicked += OpenClipboard;
        _button.Clicked += centre =>
        {
            Log.Trace("Floating button clicked: opening the Screenshot wheel.");
            if (_wheel is not null) { _wheel.CloseWheel(); return; } // a second click closes it
            var buttonWheel = new WheelWindow(_platform, _caps, WheelMode.Capture, Array.Empty<string>(), clickMode: true)
            {
                RequestSelection = async () =>
                {
                    var (files, problem) = await _platform.GetSelectedFilesAsync();
                    Log.Trace($"  Desk Wheel asked for the selection: {(files is null ? "unreadable: " + problem : files.Length + " item(s)")}");
                    if (files is null && problem is not null) Notify("Desk Wheel can't see your selection", problem, error: true);
                    return files;
                },
            };
            OpenWheel(buttonWheel, w => w.ShowAt(centre, closeWhenButtonUp: false));
        };
        _button.Moved += pos =>
        {
            _settings.ButtonX = pos.X;
            _settings.ButtonY = pos.Y;
            _settings.Save();
        };
        _button.ShowAt(_settings.ButtonX is int x && _settings.ButtonY is int y ? new PixelPoint(x, y) : null);
        Log.Trace($"Floating button shown at {_button.Position}.");
    }

    // ---------- screenshots ----------

    private bool _capturing;
    private PreviewWindow? _preview;

    private string CaptureFolder => _settings.CaptureFolder ?? CaptureNaming.DefaultFolder();

    /// <summary>Area / window / full screen → saved PNG, copied to the clipboard, preview card.</summary>
    private async Task CaptureAsync(CaptureKind kind, int delaySeconds = 0)
    {
        if (_capturing) return;
        _capturing = true;
        Log.Trace($"Screenshot: {kind}{(delaySeconds > 0 ? $" in {delaySeconds} s" : "")}");
        Usage.Track("screenshot", new() { ["kind"] = kind.ToString().ToLowerInvariant() });
        try
        {
            _wheel?.CloseWheel();
            if (delaySeconds > 0)
            {
                Notify("Screenshot", $"Capturing in {delaySeconds} seconds…");
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }

            // Keep Wheel's own windows out of the picture.
            _toast?.Close();
            _preview?.Close();
            _button?.Hide();
            await Task.Delay(120); // let them disappear from the screen

            var path = CaptureNaming.NewPath(CaptureFolder, DateTime.Now);
            // Windows: area and window always use Desk Wheel's own overlay (never the Snipping Tool).
            // Mark-up turned off in Settings: it saves as soon as the area or window is chosen.
            if ((kind == CaptureKind.Area && (_settings.EditAfterCapture || _platform.OwnCaptureOverlay))
                || (kind == CaptureKind.Window && _platform.OwnCaptureOverlay))
            {
                await SnipAsync(path, pickWindows: kind == CaptureKind.Window, finishOnSelect: !_settings.EditAfterCapture);
                return;
            }
            var (ok, problem) = await _platform.CaptureAsync(kind, path);
            if (!ok)
            {
                if (problem is not null) Notify("Screenshot", problem, error: true);
                return;
            }

            if (_settings.CaptureFormat == "jpg")
                path = await Task.Run(() => ImageEngine.ConvertCaptureToJpg(path));

            if (_settings.CaptureToClipboard)
            {
                try { await _platform.CopyImageAsync(path); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException) { Log.Write(ex); }
            }
            if (_settings.ShowCapturePreview)
                ShowPreview(path);
            else
                Notify("Screenshot saved", System.IO.Path.GetFileName(path), reveal: path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write(ex);
            Notify("Screenshot failed", ex.Message, error: true);
        }
        finally
        {
            if (_settings.ShowButton) _button?.Show();
            _capturing = false;
        }
    }

    private Window? _screensProbe;

    /// <summary>
    /// Area or window screenshot in Desk Wheel's own overlay (<see cref="SnipWindow"/>), marked up in place,
    /// then saved, copied, pinned or edited.
    /// </summary>
    private async Task SnipAsync(string path, bool pickWindows = false, bool finishOnSelect = false)
    {
        Usage.Screen(pickWindows ? "snip-window" : "snip");
        var anyWindow = (Window?)_button ?? (_screensProbe ??= new Window());
        // The window list comes first: it must describe the screen as it is in the frozen picture.
        var windows = pickWindows ? _platform.VisibleWindows() : null;
        if (await Tools.FreezeScreenAsync(anyWindow) is not { } frozen) return;
        SnipResult? result;
        try
        {
            result = await SnipWindow.ShowAsync(frozen.Picture, frozen.Screen.Bounds, frozen.Screen.Scaling, windows, finishOnSelect);
        }
        finally
        {
            try { File.Delete(frozen.Picture); } catch (IOException) { }
        }
        if (result is null) return;

        using (result.Image)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            if (_settings.CaptureFormat == "jpg") path = System.IO.Path.ChangeExtension(path, ".jpg");
            await Task.Run(() => ImageEngine.SaveEdited(result.Image, path));
        }
        Log.Trace($"  Snip saved {path} ({result.Action})");
        AppHistory.Record("Screenshot", path);
        Usage.Track("snip", new() { ["action"] = result.Action.ToString().ToLowerInvariant() });

        var copy = result.Action is SnipAction.Copy || (result.Action == SnipAction.Done && _settings.CaptureToClipboard);
        if (copy)
        {
            try { await _platform.CopyImageAsync(path); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException) { Log.Write(ex); }
        }
        switch (result.Action)
        {
            case SnipAction.Pin:
                new PinWindow(path).ShowNearCentre();
                break;
            case SnipAction.Edit:
                await OpenEditorAsync(path);
                break;
            default:
                if (finishOnSelect && _settings.ShowCapturePreview)
                    ShowPreview(path, recorded: true);
                else
                    Notify(copy ? "Copied and saved" : "Screenshot saved", System.IO.Path.GetFileName(path) + " · click to show it", reveal: path);
                break;
        }
    }

    private void ShowPreview(string path, bool recorded = false)
    {
        Log.Trace($"  Saved {path}");
        if (!recorded) AppHistory.Record("Screenshot", path);
        var preview = new PreviewWindow(path);
        Usage.Screen("preview-card");
        preview.CopyRequested += async () =>
        {
            try
            {
                await _platform.CopyImageAsync(path);
                Notify("Copied", "Screenshot copied to the clipboard");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException) { Notify("Couldn't copy", ex.Message, error: true); }
        };
        preview.PinRequested += () => new PinWindow(path).ShowNearCentre();
        preview.ShowsTextButton = _caps.HasOcr;
        preview.TextRequested += () => _ = CopyTextFromAsync(path);
        preview.EditRequested += () => _ = OpenEditorAsync(path);
        preview.ShowRequested += () => _platform.Reveal(path);
        preview.WheelRequested += () =>
        {
            if (_wheel is not null) return;
            OpenWheel(new WheelWindow(_platform, _caps, WheelMode.Tools, new[] { path }, clickMode: true), w => w.ShowAtCursor());
        };
        preview.Closed += (_, _) => { if (ReferenceEquals(_preview, preview)) _preview = null; };
        _preview = preview;
        preview.ShowInCorner();
    }

    private HistoryWindow? _history;

    private void OpenRename(System.Collections.Generic.IReadOnlyList<string> paths)
    {
        _platform.BringToFront();
        var window = new RenameWindow(paths, (title, text) => Notify(title, text));
        window.Show();
        window.Activate();
    }
    private ScreenTools? _screenTools;
    private ScreenTools Tools => _screenTools ??= new ScreenTools(this);

    // ---------- IScreenToolHost ----------
    IPlatform IScreenToolHost.Platform => _platform;
    string IScreenToolHost.CaptureFolder => CaptureFolder;
    void IScreenToolHost.ShowPreview(string imagePath) => ShowPreview(imagePath);

    void IScreenToolHost.Notify(string title, string text, bool error, Action? onClick)
    {
        Log.Trace($"Card: {title}: {text}");
        _toast?.Close();
        _toast = new ToastWindow(title, string.IsNullOrWhiteSpace(text) ? " " : text, error, onClick);
        _toast.ShowInCorner();
    }

    void IScreenToolHost.HideChrome()
    {
        _wheel?.CloseWheel();
        _toast?.Close();
        _preview?.Close();
        _button?.Hide();
    }

    void IScreenToolHost.RestoreChrome()
    {
        if (_settings.ShowButton && !Tools.IsRecording) _button?.Show();
    }

    /// <summary>A window to read screens and the pointer from (the button, else a hidden helper).</summary>
    private Window AnyWindow() => (Window?)_button ?? (Window?)_wheel ?? new Window();
    private ClipboardMonitor _clipboard = null!;
    private WatchService? _watch;
    private ClipboardWindow? _clipboardWindow;

    /// <summary>Clipboard history, near the pointer (button right-click, ⌥⇧V / Alt+Shift+V, the wheel, the menu).</summary>
    private void OpenClipboard()
    {
        Usage.Screen("clipboard");
        _wheel?.CloseWheel();
        if (_clipboardWindow is { } open)
        {
            open.Activate();
            return;
        }
        _platform.BringToFront();
        _clipboardWindow = new ClipboardWindow(_platform, _clipboard, (title, text) => Notify(title, text), OpenSettings);
        _clipboardWindow.Closed += (_, _) => _clipboardWindow = null;
        _clipboardWindow.ShowNear(_platform.CursorPixel(_clipboardWindow.Screens));
    }

    /// <summary>Recent screenshots and converted files. One window; asking again brings it to the front.</summary>
    private void OpenHistory()
    {
        _wheel?.CloseWheel();
        if (_history is { } open)
        {
            open.Activate();
            return;
        }
        _history = new HistoryWindow(_platform, CaptureFolder);
        _history.Closed += (_, _) => _history = null;
        _history.Show();
        _history.Activate();
    }

    /// <summary>The Disk Space window, scanning the given folder (the home folder if none).</summary>
    private void OpenDisk(string? folder)
    {
        _wheel?.CloseWheel();
        folder ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Usage.Screen("disk");
        var window = new DiskWindow(_platform, folder, paths =>
            OpenWheel(new WheelWindow(_platform, _caps, WheelMode.Convert, System.Linq.Enumerable.ToArray(paths), clickMode: true), w => w.ShowAtCursor()));
        _platform.BringToFront();
        window.Show();
        window.Activate();
    }

    private async Task CopyTextFromAsync(string imagePath)
    {
        try
        {
            Notify("Reading text…", System.IO.Path.GetFileName(imagePath));
            _jobs.ReportText(await OcrEngine.RecognizeAsync(imagePath));
        }
        catch (IOException ex)
        {
            Log.Write(ex);
            Notify("Couldn't read the text", ex.Message, error: true);
        }
    }

    /// <summary>
    /// "Text" on the Screenshot wheel: drag over anything on screen and its text is copied.
    /// The picture is only a temporary file; nothing is saved to the screenshots folder.
    /// </summary>
    private async Task CaptureTextAsync()
    {
        if (_capturing) return;
        _capturing = true;
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wheel-text-{Guid.NewGuid():N}.png");
        Log.Trace("Text capture");
        try
        {
            _wheel?.CloseWheel();
            _toast?.Close();
            _preview?.Close();
            _button?.Hide();
            await Task.Delay(120);

            var (ok, problem) = await _platform.CaptureAsync(CaptureKind.Area, temp);
            if (!ok)
            {
                if (problem is not null) Notify("Text", problem, error: true);
                return;
            }
            _jobs.ReportText(await OcrEngine.RecognizeAsync(temp));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write(ex);
            Notify("Couldn't read the text", ex.Message, error: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (_settings.ShowButton) _button?.Show();
            _capturing = false;
        }
    }

    /// <summary>A segment of the Screenshot wheel.</summary>
    private void RunCaptureOption(WheelOption option)
    {
        switch (option.Id)
        {
            case "cap.area": _ = CaptureAsync(CaptureKind.Area); break;
            case "cap.window": _ = CaptureAsync(CaptureKind.Window); break;
            case "cap.full": _ = CaptureAsync(CaptureKind.FullScreen); break;
            case "cap.text": _ = CaptureTextAsync(); break;
            case "cap.delay": _ = CaptureAsync(CaptureKind.Area, 5); break;
            case "cap.delay10": _ = CaptureAsync(CaptureKind.Area, 10); break;
            case "cap.scroll": _ = Tools.ScrollCaptureAsync(AnyWindow()); break;
            case "cap.ruler": _ = Tools.MeasureAsync(AnyWindow()); break;
            case "cap.gif": _ = Tools.RecordAsync(AnyWindow(), gif: true); break;
            case "cap.selected": _ = OpenWheelForSelectionAsync(null); break;
            case "cap.settings": OpenSettings(); break;
            case "cap.history": OpenHistory(); break;
            case "cap.disk": OpenDisk(null); break;
            case "cap.clipboard": OpenClipboard(); break;
            case "cap.color": _ = Tools.PickColorAsync(AnyWindow()); break;
            case "cap.qr": _ = Tools.ScanQrAsync(); break;
            case "cap.qr-make": Tools.MakeQr(); break;
            case "cap.record": _ = Tools.RecordAsync(AnyWindow()); break;
        }
    }

    // ---------- settings and editor ----------

    private SettingsWindow? _settingsWindow;

    /// <summary>Pushes settings into the engines and the platform. Called at start and after every change.</summary>
    private void ApplySettings()
    {
        Ui.SetMode(_settings.Theme);
        OutputPaths.OverrideFolder = _settings.ConvertFolder;
        Wheel.Core.Converters.FfmpegConverter.SetOverride(_settings.FfmpegPath);
        ImageEngine.Quality = _settings.ImageQuality;
        _platform.CaptureWindowShadow = _settings.WindowShadow;
        _watch?.Apply(_settings.WatchFolders);
        if (_clipboard is not null)
        {
            if (_settings.ClipboardHistory && !_clipboard.Running) _clipboard.Start();
            else if (!_settings.ClipboardHistory && _clipboard.Running) _clipboard.Stop();
        }
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _platform.BringToFront();
            _settingsWindow.Activate();
            return;
        }
        var window = new SettingsWindow(_settings, _platform, _updater);
        var gestureWas = _settings.KeyGesture;
        window.Changed += () =>
        {
            ApplySettings();
            _caps = Capabilities.Detect() with { HasOcr = OcrEngine.IsAvailable, HasVision = OcrEngine.CanRemoveBackground }; // ffmpeg may have been found or removed
            if (_settings.ShowButton) ShowButton(); else HideButton();
            if (_settings.KeyGesture && !gestureWas) StartKeyGesture();
            gestureWas = _settings.KeyGesture;
        };
        window.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow = window;
        _platform.BringToFront();
        window.Show();
        window.Activate();
    }

    /// <summary>Opens the screenshot editor on any image Wheel can read.</summary>
    /// <summary>The picture viewer: one picture → its whole folder (Next / Back); several → those; a folder → its pictures.</summary>
    private ViewerWindow? OpenViewer(System.Collections.Generic.IReadOnlyList<string> paths)
    {
        var (pictures, start) = Wheel.Core.Files.ImageFolder.For(paths);
        if (pictures.Count == 0)
        {
            Notify("No pictures", "There are no pictures here to view.");
            return null;
        }
        var viewer = new ViewerWindow(pictures, start, _platform, OpenEditorAsync);
        _platform.BringToFront();
        viewer.Show();
        viewer.Activate();
        Log.Trace($"Viewer opened: {pictures.Count} picture(s), starting at {pictures[start]}");
        return viewer;
    }

    private async Task OpenEditorAsync(string path)
    {
        try
        {
            var bitmap = await ImageEngine.LoadForEditingAsync(path);
            var editor = new EditorWindow(path, bitmap, _platform, (title, text) => Notify(title, text));
            _platform.BringToFront();
            editor.Show();
            editor.Activate();
            Log.Trace($"Editor opened for {path} ({bitmap.Width}×{bitmap.Height})");
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
        {
            Log.Write(ex);
            Notify("Can't edit this image", ex.Message, error: true);
        }
    }

    private void HideButton()
    {
        _button?.Close();
        _button = null;
    }

    private void OnHotkey()
    {
        Log.Trace($"Shortcut {_platform.HotkeyName} pressed.");
        if (_wheel is not null)
        {
            _wheel.CloseWheel(); // pressing it again closes the wheel
            return;
        }
        _ = OpenWheelForSelectionAsync(null);
    }

    /// <summary>Clickable wheel for the files selected in Finder / File Explorer.</summary>
    private async Task OpenWheelForSelectionAsync(PixelPoint? centre)
    {
        if (!_settings.Enabled || _wheel is not null)
            return;

        var (files, problem) = await _platform.GetSelectedFilesAsync();
        Log.Trace($"  Selected in {_platform.FileManagerName}: {(files is null ? "unreadable: " + problem : files.Length + " item(s)")}");
        if (files is null)
        {
            Notify("Desk Wheel can't see your selection", problem ?? "", error: true);
            return;
        }
        if (files.Length == 0)
        {
            Notify("Select files first",
                   $"Select files in {_platform.FileManagerName}, then click the orange button or press {_platform.HotkeyName}. Or just drag a file onto the button.");
            return;
        }

        OpenWheel(new WheelWindow(_platform, _caps, WheelMode.Convert, files, clickMode: true),
                  w => { if (centre is { } c) w.ShowAt(c, closeWhenButtonUp: false); else w.ShowAtCursor(); });
    }

    private void OpenWheel(WheelWindow wheel, Action<WheelWindow> show)
    {
        Usage.Screen("wheel-" + wheel.Mode);
        wheel.Pinned = () => _settings.PinnedOptions;
        wheel.TogglePin = id =>
        {
            if (!_settings.PinnedOptions.Remove(id)) _settings.PinnedOptions.Add(id);
            _settings.Save();
        };
        wheel.OptionChosen += (option, paths) =>
        {
            Log.Trace($"  Chosen: {option.Id} for {paths.Count} item(s): {string.Join(", ", paths)}");
            if (option.Engine == EngineKind.Capture)
                RunCaptureOption(option);
            else if (option.Id == "img.edit")
                _ = OpenEditorAsync(paths[0]);
            else if (option.Id == "any.rename")
                OpenRename(paths);
            else if (option.Id == "img.view")
                OpenViewer(paths);
            else if (option.Id == "act.disk")
                OpenDisk(paths.Count == 1 && System.IO.Directory.Exists(paths[0]) ? paths[0] : System.IO.Path.GetDirectoryName(paths[0]));
            else
                _ = _jobs.RunAsync(option, paths);
        };
        wheel.Closed += (_, _) => { if (ReferenceEquals(_wheel, wheel)) _wheel = null; Log.Trace("  Desk Wheel closed."); };
        _wheel = wheel;
        show(wheel);
        Log.Trace($"  Desk Wheel opened at {wheel.Position}.");
    }

    private void OnWheelKey(WheelMode mode)
    {
        Log.Trace($"Key for {mode}. Mouse held: {_platform.IsPrimaryButtonDown()} ({_platform.DescribeInput()}), {_platform.FileManagerName} in front: {_platform.ForegroundIsFileManager()}, wheel open: {_wheel is not null}");
        if (!_settings.Enabled)
            return;

        // Already open: the key switches wheels.
        if (_wheel is not null)
        {
            _wheel.SetMode(mode);
            return;
        }

        if (!_settings.KeyGesture)
            return;
        if (!_platform.IsPrimaryButtonDown())
        {
            Log.Trace("  Not opening: no drag in progress.");
            return;
        }
        if (!_settings.WorkInAllApps && !_platform.ForegroundIsFileManager())
        {
            Log.Trace($"  Not opening: {_platform.FileManagerName} isn't the front app.");
            return;
        }

        OpenWheel(new WheelWindow(_platform, _caps, mode, Array.Empty<string>(), clickMode: false), w => w.ShowAtCursor());
    }

    private NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();

        var enabled = Check("Desk Wheel is on", _settings.Enabled, on => { _settings.Enabled = on; _settings.Save(); });
        var allApps = Check($"Work in all apps (not just {_platform.FileManagerName})", _settings.WorkInAllApps,
                            on => { _settings.WorkInAllApps = on; _settings.Save(); });
        NativeMenuItem login;
        if (PackageInfo.IsPackaged)
        {
            // Store version: a Windows startup task, switched in Windows' own Startup apps list.
            login = new NativeMenuItem("Start at sign-in…");
            login.Click += (_, _) => Links.Open(PackageInfo.StartupSettings);
        }
        else
            login = Check("Start at login", SafeGet(() => _platform.StartAtLogin), on =>
            {
                try { _platform.StartAtLogin = on; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notify("Couldn't change that", ex.Message, error: true); }
            });

        var button = Check("Show the floating Desk Wheel button", _settings.ShowButton, on =>
        {
            _settings.ShowButton = on;
            _settings.Save();
            if (on) ShowButton(); else HideButton();
        });
        var clipboard = Check("Copy screenshots to the clipboard", _settings.CaptureToClipboard, on =>
        {
            _settings.CaptureToClipboard = on;
            _settings.Save();
        });
        var gesture = Check("Drag + key gesture (advanced, needs permission)", _settings.KeyGesture, on =>
        {
            _settings.KeyGesture = on;
            _settings.Save();
            if (on) StartKeyGesture();
        });

        foreach (var (label, kind, delay) in new[]
                 {
                     ("Screenshot area", CaptureKind.Area, 0),
                     ("Screenshot window", CaptureKind.Window, 0),
                     ("Screenshot full screen", CaptureKind.FullScreen, 0),
                     ("Screenshot area in 5 seconds", CaptureKind.Area, 5),
                 })
        {
            var shortcut = delay == 0 ? $"   {_platform.CaptureShortcut(kind)}" : "";
            var item = new NativeMenuItem(label + shortcut);
            item.Click += (_, _) => _ = CaptureAsync(kind, delay);
            menu.Items.Add(item);
        }
        var settingsItem = new NativeMenuItem("Settings…");
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        var clipItem = new NativeMenuItem($"Clipboard history…    {(OperatingSystem.IsMacOS() ? "⌥⇧V" : "Alt+Shift+V")}");
        clipItem.Click += (_, _) => OpenClipboard();
        menu.Items.Add(clipItem);

        foreach (var (label, run) in new (string, Action)[]
        {
            ("Record the screen (or stop)", () => _ = Tools.RecordAsync(AnyWindow())),
            ("Pick a colour", () => _ = Tools.PickColorAsync(AnyWindow())),
            ("Read a QR code", () => _ = Tools.ScanQrAsync()),
        })
        {
            var toolItem = new NativeMenuItem(label);
            toolItem.Click += (_, _) => run();
            menu.Items.Add(toolItem);
        }

        var diskItem = new NativeMenuItem("Disk space…");
        diskItem.Click += (_, _) => OpenDisk(null);
        menu.Items.Add(diskItem);

        var historyItem = new NativeMenuItem("History…");
        historyItem.Click += (_, _) => OpenHistory();
        menu.Items.Add(historyItem);

        var openShots = new NativeMenuItem("Open screenshots folder");
        openShots.Click += (_, _) =>
        {
            System.IO.Directory.CreateDirectory(CaptureFolder);
            _platform.OpenFolder(CaptureFolder);
        };
        menu.Items.Add(openShots);
        menu.Items.Add(new NativeMenuItemSeparator());

        menu.Items.Add(enabled);
        menu.Items.Add(button);
        menu.Items.Add(login);
        menu.Items.Add(clipboard);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(gesture);
        menu.Items.Add(allApps);
        menu.Items.Add(new NativeMenuItemSeparator());

        if (OperatingSystem.IsWindows() && !PackageInfo.IsPackaged)
        {
            var sendTo = new NativeMenuItem("Add to right-click 'Send to'");
            sendTo.Click += (_, _) =>
            {
                if (!OperatingSystem.IsWindows()) return;
                var ok = SendToShortcut.Install(out var message);
                Notify(ok ? "Added to Send to" : "Couldn't add to Send to", message, error: !ok);
            };
            menu.Items.Add(sendTo);
        }

        var help = new NativeMenuItem("How to use");
        help.Click += (_, _) =>
        {
            Notify("How to use",
                   $"Drop a file on the orange button, or select files and click it ({_platform.HotkeyName}). Screenshots: {_platform.CaptureShortcut(CaptureKind.Area)} area, {_platform.CaptureShortcut(CaptureKind.Window)} window, {_platform.CaptureShortcut(CaptureKind.FullScreen)} full screen, or right-click the button.");
        };
        menu.Items.Add(help);

        menu.Items.Add(new NativeMenuItem(_caps.HasFfmpeg ? "Video & audio: ready" : "Video & audio: install ffmpeg to enable") { IsEnabled = false });
        menu.Items.Add(new NativeMenuItemSeparator());

        if (Updater.Available)
        {
            _updateItem = new NativeMenuItem("Check for updates…");
            _updateItem.Click += (_, _) => _ = UpdateFromMenuAsync();
            menu.Items.Add(_updateItem);
            RefreshUpdateItem();
        }
        var support = new NativeMenuItem("Contact support (" + Wheel.Core.Brand.SupportEmail + ")");
        support.Click += (_, _) => Links.Open(Links.SupportMailto());
        menu.Items.Add(support);
        var site = new NativeMenuItem("Powered by " + Wheel.Core.Brand.PoweredBy);
        site.Click += (_, _) => Links.Open(Wheel.Core.Brand.Website);
        menu.Items.Add(site);
        menu.Items.Add(new NativeMenuItemSeparator());

        var quit = new NativeMenuItem("Quit Desk Wheel");
        quit.Click += (_, _) => Quit();
        menu.Items.Add(quit);
        return menu;
    }

    private static NativeMenuItem Check(string header, bool initial, Action<bool> changed)
    {
        var item = new NativeMenuItem(header) { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = initial };
        item.Click += (_, _) =>
        {
            item.IsChecked = !item.IsChecked;
            changed(item.IsChecked);
        };
        return item;
    }

    private static bool SafeGet(Func<bool> get)
    {
        try { return get(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    // ---------- one-shot: files given on the command line ----------

    /// <summary>"DeskWheel --view picture.jpg" (Open with › Desk Wheel): just the viewer; quits when it's closed.</summary>
    private void StartViewer(string path)
    {
        var viewer = OpenViewer([path]);
        if (viewer is null) { Quit(); return; }
        // The editor can be opened from the viewer; quit once no window is left.
        viewer.Closed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(QuitWhenNoWindows, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void QuitWhenNoWindows()
    {
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { Windows.Count: > 0 } desktop)
        {
            foreach (var w in desktop.Windows) w.Closed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(QuitWhenNoWindows, Avalonia.Threading.DispatcherPriority.Background);
            return;
        }
        Quit();
    }

    private void StartOneShot(string[] files)
    {
        var wheel = new WheelWindow(_platform, _caps, WheelMode.Convert, files, clickMode: true);
        wheel.OptionChosen += async (option, paths) =>
        {
            if (option.Id == "img.view")
            {
                var viewer = OpenViewer(paths);
                if (viewer is null) Quit();
                else viewer.Closed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(QuitWhenNoWindows, Avalonia.Threading.DispatcherPriority.Background);
                return;
            }
            await _jobs.RunAsync(option, paths);
            await Task.Delay(TimeSpan.FromSeconds(5)); // let the result card be seen
            Quit();
        };
        wheel.Closed += (_, _) => { if (!wheel.HasChoice) Quit(); };
        wheel.ShowAtCursor();
    }

    // ---------- INotifier ----------

    public void Notify(string title, string text, bool error = false, string? reveal = null)
    {
        Log.Trace($"Card: {title}: {text}");
        _toast?.Close();
        Action? onClick = reveal is null ? null : () => _platform.Reveal(reveal);
        _toast = new ToastWindow(title, string.IsNullOrWhiteSpace(text) ? " " : text, error, onClick);
        _toast.ShowInCorner();
    }

    public void SetBusy(int runningJobs)
    {
        if (_tray is not null)
            _tray.ToolTipText = runningJobs > 0 ? $"Desk Wheel: {runningJobs} job{(runningJobs == 1 ? "" : "s")} running" : IdleTip;
    }

    private void Quit()
    {
        _platform.Dispose();
        if (_tray is not null) _tray.IsVisible = false;
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    /// <summary>Fallback when the built-in icon is missing: an orange ring, drawn in code.</summary>
    private static WindowIcon MakeIcon()
    {
        using var bitmap = new SKBitmap(64, 64);
        using (var canvas = new SKCanvas(bitmap))
        using (var ring = new SKPaint { Color = new SKColor(232, 98, 44), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 13 })
        using (var dot = new SKPaint { Color = new SKColor(232, 98, 44), IsAntialias = true })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawCircle(32, 32, 22, ring);
            canvas.DrawCircle(32, 32, 6, dot);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new WindowIcon(new MemoryStream(png.ToArray()));
    }
}
