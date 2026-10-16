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
                _ = AttachAndApplyExistingAsync();
            });
        return 0;
    }

    public MainWindow()
    {
        InitializeComponent();
        WallpaperBox.ItemsSource = new[] { new WallpaperChoice(WallpaperSettings.RainyForest, "Rainy Forest") };
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

    private async void OnLoaded(object sender, RoutedEventArgs args) => await AttachAndApplyExistingAsync();
    private async Task AttachAndApplyExistingAsync()
    {
        try
        {
            _renderer.AttachExisting(); RefreshStatus();
            if (_renderer.CanApplySettings)
                await RunOperationAsync(() => _renderer.ApplyAsync(_settings));
        }
        catch (Exception error) { FeedbackText.Text = error.Message; }
    }
    private void OnRendererStateChanged()
    {
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(() => { if (!_closed) RefreshStatus(); });
    }
    private async void OnStart(object sender, RoutedEventArgs args) =>
        await RunOperationAsync(async () => { _store.Save(_settings); await _renderer.StartAsync(_settings); });
    private async void OnStop(object sender, RoutedEventArgs args) =>
        await RunOperationAsync(_renderer.StopAsync);
    private async void OnSettingsChanged(object sender, SelectionChangedEventArgs args) => await SaveSettingsAsync();
    private async void OnFullscreenChanged(object sender, RoutedEventArgs args) => await SaveSettingsAsync();

    private Task SaveSettingsAsync()
    {
        if (!_ready || _busy || _closing || WallpaperBox.SelectedValue is not string wallpaper ||
            ModeBox.SelectedValue is not PerformanceMode mode) return Task.CompletedTask;
        _settings = new() { Wallpaper = wallpaper, PerformanceMode = mode, PauseWhileFullscreen = FullscreenBox.IsChecked == true };
        return RunOperationAsync(async () =>
        {
            _store.Save(_settings);
            await _renderer.ApplyAsync(_settings);
        });
    }

    private Task RunOperationAsync(Func<Task> action)
    {
        if (_busy || _closing) return Task.CompletedTask;
        _operation = ExecuteAsync(action);
        return _operation;
    }
    private async Task ExecuteAsync(Func<Task> action)
    {
        _busy = true; FeedbackText.Text = ""; RefreshStatus();
        try { await action(); }
        catch (Exception error) { FeedbackText.Text = error.Message; }
        finally { _busy = false; RefreshStatus(); }
    }
    private void RefreshStatus()
    {
        bool running = _renderer.IsRunning;
        StartButton.IsEnabled = !_busy && !_closing && !running;
        StopButton.IsEnabled = !_busy && !_closing && running;
        WallpaperBox.IsEnabled = ModeBox.IsEnabled = FullscreenBox.IsEnabled = !_busy && !_closing;
        StatusText.Text = _closing ? "Stopping wallpaper…" : _busy ? "Applying…" : running ?
            (_renderer.StartedHere ? "Running" : "Running · started outside this controller") : "Stopped";
        if (running && !_renderer.CanApplySettings && !_busy)
            FeedbackText.Text = "An older renderer is running. Stop it, then Start to use these settings.";
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
            await _renderer.StopAsync();
            _renderer.StateChanged -= OnRendererStateChanged;
            _renderer.Dispose();
            _closed = true;
            // Stop may already be complete. Queue Close so we never reenter WPF's
            // Closing event synchronously while its original callback is active.
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
