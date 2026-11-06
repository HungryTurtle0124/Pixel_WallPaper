using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using PixelWallpaper.Shared;

namespace PixelWallpaper.App;

public partial class MainWindow : Window
{
    private sealed record WallpaperChoice(string Id, string Title) { public override string ToString() => Title; }
    private sealed record ModeChoice(PerformanceMode Mode, string Title) { public override string ToString() => Title; }
    private readonly SettingsStore _store = new();
    private readonly RendererClient _renderer = new();
    private WallpaperSettings _settings = new();
    private bool _ready, _busy, _closing, _closed;
    private Task _operation = Task.CompletedTask;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnWindowMessage);
    }
    private nint OnWindowMessage(nint window, int message, nint value, nint parameter, ref bool handled)
    {
        if (RendererProtocol.ReadyMessage != 0 && message == RendererProtocol.ReadyMessage)
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (!_ready || _closing || _busy) return;
                _ = SynchronizeAsync();
            });
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
        _settings = _store.Load(out string? warning);
        WallpaperBox.SelectedValue = _settings.Wallpaper;
        ModeBox.SelectedValue = _settings.PerformanceMode;
        FullscreenBox.IsChecked = _settings.PauseWhileFullscreen;
        FeedbackText.Text = warning ?? "";
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
        if (live is null) return;
        bool changed = live != _settings;
        ShowSettings(live);
        if (changed) _store.Save(live); // Live renderer wins over stale disk preferences.
        FeedbackText.Text = "";
    }, clearFeedback: false);
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
            Dispatcher.BeginInvoke(() => { if (!_closed) RefreshStatus(); });
    }
    private async void OnStart(object sender, RoutedEventArgs args) =>
        await RunOperationAsync(async () =>
        {
            var actual = await _renderer.StartAsync(_settings);
            ShowSettings(actual); _store.Save(actual);
        });
    private async void OnStop(object sender, RoutedEventArgs args) =>
        await RunOperationAsync(async () =>
        {
            var actual = await _renderer.StopAsync();
            if (actual is not null) { ShowSettings(actual); _store.Save(actual); }
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
            ShowSettings(actual); _store.Save(actual);
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
        finally { _busy = false; RefreshStatus(); }
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
