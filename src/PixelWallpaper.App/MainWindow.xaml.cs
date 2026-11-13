using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using PixelWallpaper.Shared;

namespace PixelWallpaper.App;

public partial class MainWindow : Window
{
    private sealed record WallpaperChoice(string Id, string Title) { public override string ToString() => Title; }
    private sealed record ModeChoice(PerformanceMode Mode, string Title) { public override string ToString() => Title; }
    private readonly RendererClient _renderer = new(store: new SettingsStore());
    private readonly StartupRegistration _startup = new(
        Path.Combine(AppContext.BaseDirectory, "Renderer", "PixelWallpaper.Renderer.exe"));
    private WallpaperSettings _settings = new();
    private bool _ready, _busy, _closing, _closed, _refreshPending, _updatingStartup;
    private Task _operation = Task.CompletedTask;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnWindowMessage);
    }
    private nint OnWindowMessage(nint window, int message, nint value, nint parameter, ref bool handled)
    {
        if (RendererProtocol.OpenSettingsMessage != 0 && message == RendererProtocol.OpenSettingsMessage)
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (_closing) return;
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
            });
        if ((RendererProtocol.ReadyMessage != 0 && message == RendererProtocol.ReadyMessage) ||
            (RendererProtocol.PreferencesChangedMessage != 0 && message == RendererProtocol.PreferencesChangedMessage))
            _ = Dispatcher.BeginInvoke(() => { if (_ready) RequestRefresh(); });
        return 0;
    }

    public MainWindow()
    {
        InitializeComponent();
        WallpaperBox.ItemsSource = new[]
        {
            new WallpaperChoice(WallpaperSettings.RainyForest, "Rainy Forest"),
            new WallpaperChoice(WallpaperSettings.Campfire, "Campfire"),
            new WallpaperChoice(WallpaperSettings.MapleTree, "Giant Maple Tree")
        };
        ModeBox.ItemsSource = new[]
        {
            new ModeChoice(PerformanceMode.Eco, "Eco · 15 FPS"),
            new ModeChoice(PerformanceMode.Balanced, "Balanced · 30 FPS"),
            new ModeChoice(PerformanceMode.Smooth, "Smooth · 60 FPS")
        };
        _settings = new SettingsStore().Load(out string? warning);
        WallpaperBox.SelectedValue = _settings.Wallpaper;
        ModeBox.SelectedValue = _settings.PerformanceMode;
        FullscreenBox.IsChecked = _settings.PauseWhileFullscreen;
        FeedbackText.Text = warning ?? "";
        try { RefreshStartup(); }
        catch (Exception error) { FeedbackText.Text = error.Message; }
        _renderer.StateChanged += OnRendererStateChanged;
        _ready = true;
        RefreshStatus();
    }

    private async void OnLoaded(object sender, RoutedEventArgs args) => await SynchronizeAsync();
    private async void OnActivated(object? sender, EventArgs args)
    {
        if (_ready) await SynchronizeAsync();
    }
    private Task SynchronizeAsync() => RunOperationAsync(async () =>
    {
        var live = await _renderer.ConnectAsync();
        if (_renderer.CurrentSettings is { } current) ShowSettings(current);
        RefreshStartup();
        if (live is not null) FeedbackText.Text = "";
    }, clearFeedback: false);

    private void RequestRefresh()
    {
        if (_closing) return;
        if (_busy) _refreshPending = true;
        else _ = SynchronizeAsync();
    }

    private void RefreshStartup()
    {
        _updatingStartup = true;
        try { StartupBox.IsChecked = _startup.IsRegistered; }
        finally { _updatingStartup = false; }
    }

    private void OnStartupChanged(object sender, RoutedEventArgs args)
    {
        if (!_ready || _updatingStartup || _closing) return;
        try { _startup.SetEnabled(StartupBox.IsChecked == true); FeedbackText.Text = ""; }
        catch (Exception error)
        {
            try { RefreshStartup(); } catch (Exception) { }
            FeedbackText.Text = error.Message;
        }
    }

    private void OnOpenTray(object sender, RoutedEventArgs args)
    {
        try
        {
            using var tray = Process.Start(new ProcessStartInfo(
                Path.Combine(AppContext.BaseDirectory, "Tray", "PixelWallpaper.Tray.exe")) { UseShellExecute = true });
            TrayHelpText.Text = "Look for Pixel Wallpaper in the notification area or under the taskbar overflow arrow.";
        }
        catch (Exception error) { FeedbackText.Text = "Could not open tray controls. " + error.Message; }
    }
    private void ShowSettings(WallpaperSettings settings)
    {
        _ready = false;
        _settings = settings;
        WallpaperBox.SelectedValue = settings.Wallpaper;
        ModeBox.SelectedValue = settings.PerformanceMode;
        FullscreenBox.IsChecked = settings.PauseWhileFullscreen;
        _ready = true;
    }
    private void OnRendererStateChanged()
    {
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(() => { if (!_closed) RequestRefresh(); });
    }
    private async void OnStart(object sender, RoutedEventArgs args) =>
        await RunOperationAsync(async () =>
        {
            var actual = await _renderer.StartAsync(_settings);
            ShowSettings(actual);
        });
    private async void OnStop(object sender, RoutedEventArgs args) =>
        await RunOperationAsync(async () =>
        {
            var actual = await _renderer.StopAsync();
            if (actual is not null) ShowSettings(actual);
        });
    private async void OnSettingsChanged(object sender, SelectionChangedEventArgs args) => await SaveSettingsAsync();
    private async void OnFullscreenChanged(object sender, RoutedEventArgs args) => await SaveSettingsAsync();

    private Task SaveSettingsAsync()
    {
        if (!_ready || _busy || _closing || WallpaperBox.SelectedValue is not string wallpaper ||
            ModeBox.SelectedValue is not PerformanceMode mode) return Task.CompletedTask;
        var desired = new WallpaperSettings { Wallpaper = wallpaper, PerformanceMode = mode, PauseWhileFullscreen = FullscreenBox.IsChecked == true };
        return RunOperationAsync(async () =>
        {
            var actual = await _renderer.ApplyAsync(desired);
            ShowSettings(actual);
        });
    }

    private Task RunOperationAsync(Func<Task> action, bool clearFeedback = true)
    {
        if (_busy || _closing) return Task.CompletedTask;
        _operation = ExecuteAsync(action, clearFeedback);
        return _operation;
    }
    private async Task ExecuteAsync(Func<Task> action, bool clearFeedback)
    {
        _busy = true;
        if (clearFeedback) FeedbackText.Text = "";
        RefreshStatus();
        try { await action(); }
        catch (Exception error)
        {
            if (_renderer.CurrentSettings is { } live) ShowSettings(live);
            FeedbackText.Text = error.Message;
        }
        finally
        {
            _busy = false; RefreshStatus();
            if (_refreshPending && !_closing)
            {
                _refreshPending = false;
                _ = Dispatcher.BeginInvoke(new Action(RequestRefresh));
            }
        }
    }
    private void RefreshStatus()
    {
        bool running = _renderer.IsRunning;
        StartButton.IsEnabled = !_busy && !_closing && !running;
        StopButton.IsEnabled = !_busy && !_closing && running;
        WallpaperBox.IsEnabled = ModeBox.IsEnabled = FullscreenBox.IsEnabled = !_busy && !_closing && !_renderer.IsStopping;
        StatusText.Text = _closing ? "Finishing command and closing controls…" : _busy ? "Applying…" :
            running ? (_renderer.IsStopping ? "Stopping wallpaper…" : "Running") : "Stopped";
    }

    private async void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_closed) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true; RefreshStatus();
        try
        {
            await _operation;
            _renderer.StateChanged -= OnRendererStateChanged;
            _renderer.Dispose();
            _closed = true;
            // Disconnect only; the renderer is independent. Queue Close to avoid
            // reentering WPF's original Closing callback synchronously.
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
        catch (Exception error)
        {
            _closing = false;
            FeedbackText.Text = error.Message;
            RefreshStatus();
        }
    }
}
