using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CPSystemMonitor.Core.Input;
using CPSystemMonitor.Core.Model;
using CPSystemMonitor.Core.Monitoring;
using CPSystemMonitor.Core.Popup;
using CPSystemMonitor.Core.Storage;
using CPSystemMonitor.Platform;
using CPSystemMonitor.Platform.MacOS;
using CPSystemMonitor.Platform.Windows;
namespace CPSystemMonitor.App;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private IPlatformServices? _platform;
    private AppDataStore? _store;
    private SettingsHub? _hub;
    private MonitorWindow? _window;
    private SettingsWindow? _settings;
    private PermissionGuideWindow? _permissionGuide;
    private TrayIcon? _tray;
    private MonitorSampler? _sampler;
    private readonly SampleHistory _history = new();
    private readonly CancellationTokenSource _cancel = new();
    private Task? _sampling;
    private readonly DispatcherTimer _pointerPoll = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _permissions = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _quitting;
    private long _lastTrigger;
    private volatile bool _attributionRequested;
    private string _monitorStatus = "disabled";
    private nint _previousForeground;
    private BoardWindowAppearance? _appearance;
    public override void Initialize() { AvaloniaXamlLoader.Load(this); AppTheme.Install(this); }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop; desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()) { Notice.Show(Strings.Notice_UnsupportedOs_Title, Strings.Notice_UnsupportedOs); desktop.Shutdown(); return; }
            Start();
        }
        base.OnFrameworkInitializationCompleted();
    }
    private void Start()
    {
        var paths = DataPaths.Resolve(DataPaths.ExecutableDirectory(Environment.ProcessPath, AppContext.BaseDirectory));
        paths.EnsureCreated(); _store = new(paths);
        var loaded = _store.SettingsStore.Load(); var settings = loaded.Value;
        AppLog.Init(paths.LogsDirectory, settings.Advanced.Logging);
        _hub = new(_store, settings);
        if (loaded.Status == LoadStatus.CreatedDefault) _store.SaveSettings(settings);
        else if (loaded.Status != LoadStatus.Loaded) Notice.Show(Strings.Notice_Load_Title, MonitorText.T("Settings were recovered or reset. The original file is preserved; newer settings are read-only.", "設定を復旧または初期化しました。元ファイルは保存され、新しい版の設定は読み取り専用です。") + "\n" + loaded.Status);
        _platform = OperatingSystem.IsMacOS() ? new MacPlatformServices() : new WindowsPlatformServices();
        IMonitorSource source = OperatingSystem.IsMacOS() ? new MacMonitorSource() : new WindowsMonitorSource();
        _sampler = new(source);
        Toast.Appearance = () => _hub.Current.Appearance;
        ApplyTheme(); CreateWindow(); CreateTray();
        _hub.Changed += SettingsChanged;
        RegisterTriggers();
        _platform.Hotkey.Pressed += time => DispatchTrigger(null, time, PopupTriggerKind.Keyboard);
        _platform.InputHook.DoubleTapped += time => DispatchTrigger(null, time, PopupTriggerKind.Keyboard);
        _platform.MouseTrigger.Triggered += (point, time) => DispatchTrigger(point, time, PopupTriggerKind.Mouse);
        _platform.MouseTrigger.PassedThroughDown += point => Dispatcher.UIThread.Post(() =>
        {
            if (_window?.IsVisible != true || _window.HasOpenMenu || !_hub.Current.Popup.CloseOnOutsideClick || _settings is { IsVisible: true }) return;
            double scale = _platform.Window.UsesLogicalScreenCoordinates ? 1 : _window.RenderScaling;
            var bounds = new PixelRect(_window.Position, new PixelSize((int)(_window.Bounds.Width * scale), (int)(_window.Bounds.Height * scale)));
            if (!bounds.Contains(new PixelPoint(point.X, point.Y))) HideMonitor();
        });
        _platform.InputHook.Failed += error => Dispatcher.UIThread.Post(() => { AppLog.Info("Trigger: " + error); SetTrayWarning(true); });
        _permissions.Tick += (_, _) =>
        {
            if (!_platform.InputHook.IsRunning && _platform.Permissions.Accessibility == PermissionState.Granted) _platform.InputHook.Start();
            SetTrayWarning(!_platform.InputHook.IsRunning);
        };
        _pointerPoll.Tick += (_, _) => CheckMouseLeave();
        _permissions.Start();
        _platform.InputHook.Start();
        if (Program.Instance is { } instance) _ = instance.Listen(() => Dispatcher.UIThread.Post(() => ShowMonitor()));
        _sampling = SamplingLoopAsync();
        _desktop!.ShutdownRequested += (_, e) => { if (!_quitting) { e.Cancel = true; _ = QuitAsync(); } };
        _desktop.Exit += (_, _) => Cleanup();
        ShowMonitor(restore: true);
        if (!settings.General.HideStartupGuide)
        {
            var guide = new StartupGuideWindow(_hub, paths, _platform);
            WindowActivation.ShowAndActivate(guide, _platform);
        }
    }
    private void CreateWindow()
    {
        if (_hub is null || _platform is null || _store is null) return;
        _window = new(_hub, _history);
        _desktop!.MainWindow = _window;
        _window.SettingsRequested += OpenSettings;
        _window.ExitRequested += () => _ = QuitAsync();
        _window.HideRequested += HideMonitor;
        _window.SizeChanged += (_, _) => ClampWindow();
        _appearance = new BoardWindowAppearance(_window, _hub.Current.Appearance, _store.Paths, _platform);
        // Closing is tray minimization; hide effects are driven by HideMonitor, not Close().
        _window.WindowHideEffect = Core.Effects.EffectSpec.None;
        _window.WindowShowEffect = Core.Effects.EffectSpec.None;
        _window.ReducedMotion = () => _platform.Window.PrefersReducedMotion;
        _window.PointerExited += (_, _) =>
        {
            if (!_hub.Current.Popup.CloseOnMouseLeave) return;
            Dispatcher.UIThread.Post(CheckMouseLeave);
        };
    }
    private void CheckMouseLeave()
    {
        if (_window?.IsVisible != true || _window.HasOpenMenu || _platform is null || _hub is null || !_hub.Current.Popup.CloseOnMouseLeave) return;
        var p = _platform.Window.GetCursorPosition(); double scale = _platform.Window.UsesLogicalScreenCoordinates ? 1 : _window.RenderScaling;
        double d = _hub.Current.Popup.MouseLeaveDistance * scale;
        if (p.X < _window.Position.X - d || p.Y < _window.Position.Y - d || p.X > _window.Position.X + _window.Bounds.Width * scale + d || p.Y > _window.Position.Y + _window.Bounds.Height * scale + d) HideMonitor();
    }
    private async Task SamplingLoopAsync()
    {
        if (_hub is null || _sampler is null) return;
        try
        {
            while (!_cancel.IsCancellationRequested)
            {
                // Read immutable options on UI thread, and do all native sampling on one worker.
                var options = await Dispatcher.UIThread.InvokeAsync(() => (_hub.Current.Monitor.Drive, _hub.Current.Monitor.RefreshSeconds, _hub.Current.Monitor.HistorySeconds, _hub.Current.Monitor.DetailedAttribution));
                var sample = await Task.Run(() =>
                {
                    _sampler.ConfigureAttribution(options.DetailedAttribution);
                    if (_attributionRequested) { _sampler.ConfigureAttribution(false); _sampler.ConfigureAttribution(options.DetailedAttribution); _attributionRequested = false; }
                    return _sampler.Sample(options.Drive);
                }, _cancel.Token);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_quitting) return;
                    _monitorStatus = _sampler.AttributionStatus;
                    _history.Add(sample, options.HistorySeconds);
                    if (_window?.IsVisible == true) _window.Update(sample);
                    if (sample.Status is not null) AppLog.Info(sample.Status);
                });
                await Task.Delay(TimeSpan.FromSeconds(options.RefreshSeconds), _cancel.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Info("Sampling failed: " + ex); await Dispatcher.UIThread.InvokeAsync(() => Notice.Show(MonitorText.T("Monitoring stopped", "監視が停止しました"), ex.Message)); }
    }
    private void DispatchTrigger(ScreenPoint? point, long time, PopupTriggerKind kind) => Dispatcher.UIThread.Post(() =>
    {
        if (_quitting || _hub is null || Stopwatch.GetElapsedTime(_lastTrigger, time).TotalMilliseconds < 250) return;
        _lastTrigger = time;
        if (_window?.IsVisible == true && _hub.Current.Popup.ToggleOnTrigger) HideMonitor();
        else ShowMonitor(point, kind);
    });
    private void ShowMonitor(ScreenPoint? point = null, PopupTriggerKind kind = PopupTriggerKind.Mouse, bool restore = false)
    {
        if (_window is null || _hub is null || _platform is null) return;
        _previousForeground = _platform.Window.CaptureForeground();
        int? savedX = _hub.Current.Monitor.X, savedY = _hub.Current.Monitor.Y;
        bool wasVisible = _window.IsVisible;
        _window.CancelTransition();
        _window.Show();
        if (_history.Samples.LastOrDefault() is { } last) _window.Update(last);
        if (restore && savedX is { } x && savedY is { } y) _window.Position = new(x, y);
        else if (!wasVisible && !restore)
        {
            var cursor = point ?? _platform.Window.GetCursorPosition();
            ScreenRect Area(ScreenPoint p)
            {
                var area = (_window.Screens.ScreenFromPoint(new PixelPoint(p.X, p.Y)) ?? _window.Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
                return new(area.X, area.Y, area.Width, area.Height);
            }
            double scale = _platform.Window.UsesLogicalScreenCoordinates ? 1 : _window.RenderScaling;
            var at = PopupPlacement.Compute(_hub.Current.Popup.PlacementFor(kind), cursor, (int)(_window.Width * scale), (int)(_window.Height * scale), Area, Area(cursor), _hub.Current.Popup.CursorAnchor, scale);
            _window.Position = new(at.X, at.Y);
        }
        ClampWindow();
        if (_window.TryGetPlatformHandle()?.Handle is { } handle) { _platform.Window.ConfigureBoardWindow(handle); _platform.Window.BringToForeground(handle); }
        _window.Activate();
        if (_hub.Current.Popup.CloseOnMouseLeave) _pointerPoll.Start();
    }
    private void ClampWindow()
    {
        if (_window is null) return;
        var screen = _window.Screens.ScreenFromPoint(_window.Position) ?? _window.Screens.Primary;
        if (screen is null) return;
        double scale = OperatingSystem.IsMacOS() ? 1 : screen.Scaling;
        var a = screen.WorkingArea;
        _window.Position = new(Math.Clamp(_window.Position.X, a.X, Math.Max(a.X, a.Right - (int)(_window.Width * scale))), Math.Clamp(_window.Position.Y, a.Y, Math.Max(a.Y, a.Bottom - (int)(_window.Height * scale))));
    }
    private async void HideMonitor()
    {
        if (_window is null || !_window.IsVisible) return;
        var window = _window;
        if (!await window.AnimateAsync(false) || !ReferenceEquals(_window, window) || _quitting) return;
        window.Hide(); _pointerPoll.Stop(); _hub?.Flush();
        if (_previousForeground != 0) _platform?.Window.RestoreForeground(_previousForeground);
    }
    private void ApplyTheme()
    {
        if (_hub is null) return;
        var appearance = _hub.Current.Appearance;
        if (Core.Theming.RgbColor.TryParse(appearance.Colors.Primary) is { } p && Core.Theming.RgbColor.TryParse(appearance.Colors.Secondary) is { } s) AppTheme.SetColors(this, p, s);
        ChromeWindow.ShowEffect = Core.Effects.EffectCatalog.Resolve(appearance, Core.Effects.EffectCatalog.BoardShow);
        ChromeWindow.HideEffect = Core.Effects.EffectCatalog.Resolve(appearance, Core.Effects.EffectCatalog.BoardHide);
        ChromeWindow.GlitchLook = Core.Effects.GlitchLook.Scaled(appearance.GlitchIntensity);
    }
    private void SettingsChanged(SettingsChange change)
    {
        if (_hub is null) return;
        if ((change & SettingsChange.Colors) != 0) ApplyTheme();
        if ((change & SettingsChange.Triggers) != 0) RegisterTriggers();
        if ((change & SettingsChange.Popup) != 0)
        {
            if (_window?.IsVisible == true && _hub.Current.Popup.CloseOnMouseLeave) _pointerPoll.Start(); else _pointerPoll.Stop();
        }
        if ((change & SettingsChange.Logging) != 0) AppLog.SetEnabled(_hub.Current.Advanced.Logging);
        if ((change & (SettingsChange.Effects | SettingsChange.Background)) != 0 && _window is not null)
        {
            ApplyTheme(); bool visible = _window.IsVisible; var pos = _window.Position;
            _window.AllowClose = true; _window.WindowHideEffect = Core.Effects.EffectSpec.None; _window.Close();
            CreateWindow(); _window!.Position = pos; if (visible) ShowMonitor(restore: true);
        }
        if ((change & (SettingsChange.Hud | SettingsChange.Opacity)) != 0) _appearance?.ApplyLiveSettings(_hub.Current.Appearance);
        if ((change & SettingsChange.BoardLayout) != 0) ClampWindow();
    }
    private void RegisterTriggers()
    {
        if (_platform is null || _hub is null) return;
        _platform.Hotkey.Register(_hub.Current.Triggers.Hotkey, out var error);
        if (error is not null) AppLog.Info(error);
        _platform.MouseTrigger.Configure(_hub.Current.Triggers);
        _platform.InputHook.ConfigureDoubleTap(_hub.Current.Triggers.DoubleTap);
    }
    private void OpenSettings()
    {
        if (_hub is null || _platform is null || _store is null) return;
        if (_settings is null)
        {
            _settings = new(new(_hub, _platform, _store.Paths, () => _window?.Position, path => _platform.Shell.Open(path), ShowPermissions,
                FlushAll: _hub.Flush, ImportAndRestart: contents =>
                {
                    try { _store.Import(contents, DateTime.Now); Restart(); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { Toast.Show(Strings.FormatToast_ImportFailed(ex.Message)); }
                },
                Restart: Restart, RequestMonitorPermissions: RequestMonitorPermissions, MonitorStatus: () => _monitorStatus));
            _ = new BoardWindowAppearance(_settings, _hub.Current.Appearance, _store.Paths, _platform, fixedOpacity: 1);
            _settings.Closed += (_, _) => _settings = null;
        }
        WindowActivation.ShowAndActivate(_settings, _platform);
    }
    private void ShowPermissions()
    {
        if (_platform is null) return;
        if (_permissionGuide is null)
        {
            _permissionGuide = new(_platform, () => _platform.InputHook.Start(), Restart);
            _permissionGuide.Closed += (_, _) => _permissionGuide = null;
        }
        WindowActivation.ShowAndActivate(_permissionGuide, _platform);
    }
    private async void RequestMonitorPermissions()
    {
        if (_hub is null || _settings is null) return;
        if (OperatingSystem.IsWindows())
        {
            if (!await Dialogs.ConfirmAsync(_settings, MonitorText.T("Administrator permission", "管理者権限"), MonitorText.T("Restart CPSystemMonitor as administrator to collect disk and network top apps? Windows will ask for approval.", "ディスク・通信の最大負荷アプリ取得のため、管理者として再起動します。Windowsの許可画面が表示されます。"), MonitorText.T("Restart", "再起動"))) return;
            _hub.Update(s => s.Monitor.DetailedAttribution = true, SettingsChange.BoardLayout); _hub.Flush();
            Restart(elevate: true);
        }
        else
        {
            _hub.Update(s => s.Monitor.DetailedAttribution = true, SettingsChange.BoardLayout);
            _attributionRequested = true;
        }
    }
    private void CreateTray()
    {
        var menu = new NativeMenu();
        void Item(string title, Action action) { var item = new NativeMenuItem(title); item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item(MonitorText.T("Show monitor", "モニターを表示"), () => ShowMonitor());
        Item(Strings.Common_Settings, OpenSettings); Item(Strings.Tray_Permissions, ShowPermissions);
        menu.Items.Add(new NativeMenuItemSeparator()); Item(Loc.Os(Strings.Tray_Quit_Win, Strings.Tray_Quit_Mac), () => _ = QuitAsync());
        _tray = new TrayIcon { Icon = AppIcon.Create(), ToolTipText = AppInfo.ProductName, Menu = menu, IsVisible = true };
        MacOSProperties.SetIsTemplateIcon(_tray, AppIcon.IsTemplate(false));
        _tray.Clicked += (_, _) => ShowMonitor(); TrayIcon.SetIcons(this, new TrayIcons { _tray });
    }
    private void SetTrayWarning(bool warning)
    {
        if (_tray is null) return;
        _tray.Icon = AppIcon.Create(warning); MacOSProperties.SetIsTemplateIcon(_tray, AppIcon.IsTemplate(warning));
        _tray.ToolTipText = warning ? Strings.Tray_TriggerDisabled_Tooltip : AppInfo.ProductName;
    }
    private void Restart() => Restart(false);
    private void Restart(bool elevate)
    {
        if (Environment.ProcessPath is not { } exe || _quitting) return;
        _hub?.Flush();
        var start = new ProcessStartInfo(exe) { UseShellExecute = elevate };
        if (elevate) start.Verb = "runas";
        else
        {
            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--wait-for") { i++; continue; }
                start.ArgumentList.Add(args[i]);
            }
        }
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Insert(0, typeof(App).Assembly.Location);
        start.ArgumentList.Add("--wait-for"); start.ArgumentList.Add(Environment.ProcessId.ToString());
        try { Process.Start(start); _ = QuitAsync(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Keep the original instance and its lock when UAC is cancelled.
            Toast.Show(MonitorText.T("Restart cancelled: ", "再起動を中止：") + ex.Message);
        }
    }
    private async Task QuitAsync()
    {
        if (_quitting) return;
        _quitting = true; _cancel.Cancel();
        if (_sampling is not null) await _sampling;
        Cleanup(); _desktop?.Shutdown();
    }
    private void Cleanup()
    {
        _permissions.Stop(); _pointerPoll.Stop(); _cancel.Cancel(); _hub?.Flush();
        _sampler?.Dispose(); _sampler = null;
        if (_tray is not null) { _tray.IsVisible = false; _tray.Dispose(); _tray = null; }
        if (_window is not null) { _window.AllowClose = true; _window.Close(); }
        _platform?.Dispose(); _platform = null; Program.Instance?.Dispose();
    }
}
