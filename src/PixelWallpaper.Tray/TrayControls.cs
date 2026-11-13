using System.Diagnostics;
using PixelWallpaper.Shared;

namespace PixelWallpaper.Tray;

internal sealed class TrayControls : ApplicationContext
{
    private readonly string _directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    private readonly RendererClient _renderer;
    private readonly SynchronizationContext _context;
    private readonly BroadcastWindow _window;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _status = new("Pixel Wallpaper — checking…") { Enabled = false };
    private readonly ToolStripMenuItem _scenes = new("Wallpaper");
    private readonly ToolStripMenuItem _modes = new("Performance");
    private readonly ToolStripMenuItem _pause = new("Pause While Fullscreen");
    private readonly ToolStripMenuItem _startStop = new("Start Wallpaper");
    private WallpaperSettings _settings = new();
    private bool _busy, _refreshPending, _exiting, _disposed;
    private bool _statusUnavailable;

    public TrayControls(SynchronizationContext context)
    {
        _context = context;
        _renderer = new RendererClient(Path.Combine(_directory, "Renderer", "PixelWallpaper.Renderer.exe"), new SettingsStore());
        _renderer.StateChanged += OnRendererChanged;
        _window = new BroadcastWindow(Refresh);

        var open = new ToolStripMenuItem("Open Settings", null, (_, _) => OpenSettings());
        AddScene("Rainy Forest", WallpaperSettings.RainyForest);
        AddScene("Campfire", WallpaperSettings.Campfire);
        AddScene("Giant Maple Tree", WallpaperSettings.MapleTree);
        AddMode("Eco (15 FPS)", PerformanceMode.Eco);
        AddMode("Balanced (30 FPS)", PerformanceMode.Balanced);
        AddMode("Smooth (60 FPS)", PerformanceMode.Smooth);
        _pause.Click += (_, _) => Apply(_settings with { PauseWhileFullscreen = !_settings.PauseWhileFullscreen });
        _startStop.Click += (_, _) =>
        {
            // Capture the action shown to the user; never turn a stale Stop click into Start.
            bool stop = _renderer.IsRunning;
            if (_startStop.Text != (stop ? "Stop Wallpaper" : "Start Wallpaper")) { Refresh(); return; }
            _ = RunAsync(async () =>
            {
                if (stop) await _renderer.StopAsync();
                else await _renderer.StartAsync(_settings);
            }, reportErrors: true);
        };
        var exit = new ToolStripMenuItem("Exit Tray", null, (_, _) => CloseTray());
        _menu.Items.AddRange([_status, new ToolStripSeparator(), open, _scenes, _modes, _pause,
            new ToolStripSeparator(), _startStop, exit]);
        _menu.Opening += (_, _) => Refresh();

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application, ContextMenuStrip = _menu,
            Text = "Pixel Wallpaper — checking", Visible = true
        };
        _icon.DoubleClick += (_, _) => OpenSettings();
        _context.Post(_ => Refresh(), null);
    }

    private void AddScene(string title, string scene) => _scenes.DropDownItems.Add(
        new ToolStripMenuItem(title, null, (_, _) => Apply(_settings with { Wallpaper = scene })) { Tag = scene });

    private void AddMode(string title, PerformanceMode mode) => _modes.DropDownItems.Add(
        new ToolStripMenuItem(title, null, (_, _) => Apply(_settings with { PerformanceMode = mode })) { Tag = mode });

    private void Apply(WallpaperSettings desired) => _ = RunAsync(() => _renderer.ApplyAsync(desired), reportErrors: true);
    private void OnRendererChanged() => _context.Post(_ => Refresh(), null);

    private void Refresh()
    {
        if (_exiting || _disposed) return;
        if (_busy) { _refreshPending = true; return; }
        _ = RunAsync(() => _renderer.ConnectAsync(), reportErrors: false);
    }

    private async Task RunAsync(Func<Task> command, bool reportErrors)
    {
        if (_busy || _exiting || _disposed) return;
        _busy = true;
        UpdateMenu();
        try { await command(); _statusUnavailable = false; }
        catch (Exception error)
        {
            if (!reportErrors) _statusUnavailable = true;
            if (reportErrors && !_exiting)
                MessageBox.Show(error.Message, "Pixel Wallpaper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _busy = false;
            if (_exiting) ExitThread();
            else
            {
                if (_renderer.CurrentSettings is { } actual) _settings = actual;
                UpdateMenu();
                if (_refreshPending) { _refreshPending = false; _context.Post(_ => Refresh(), null); }
            }
        }
    }

    private void UpdateMenu()
    {
        bool running = _renderer.IsRunning;
        string status = _busy ? "Updating…" : _statusUnavailable ? "Status unavailable" :
            _renderer.IsStopping ? "Stopping" : running ? "Running" : "Stopped";
        _status.Text = $"Pixel Wallpaper — {status}";
        _icon.Text = _status.Text;
        _startStop.Text = running ? "Stop Wallpaper" : "Start Wallpaper";
        _scenes.Enabled = _modes.Enabled = _pause.Enabled = _startStop.Enabled = !_busy && !_statusUnavailable && !_renderer.IsStopping;
        foreach (ToolStripMenuItem item in _scenes.DropDownItems) item.Checked = (string?)item.Tag == _settings.Wallpaper;
        foreach (ToolStripMenuItem item in _modes.DropDownItems) item.Checked = Equals(item.Tag, _settings.PerformanceMode);
        _pause.Checked = _settings.PauseWhileFullscreen;
    }

    private void OpenSettings()
    {
        try
        {
            using var settings = Process.Start(new ProcessStartInfo(
                Path.Combine(_directory, "PixelWallpaper.App.exe")) { UseShellExecute = true });
        }
        catch (Exception error) { MessageBox.Show(error.Message, "Pixel Wallpaper", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void CloseTray()
    {
        _exiting = true;
        _icon.Visible = false;
        if (!_busy) ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _renderer.StateChanged -= OnRendererChanged;
            _renderer.Dispose();
            _window.DestroyHandle();
            _icon.Dispose();
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class BroadcastWindow : NativeWindow
    {
        private readonly Action _refresh;
        public BroadcastWindow(Action refresh)
        {
            _refresh = refresh;
            CreateHandle(new CreateParams { Caption = "Pixel Wallpaper tray notifications", ExStyle = 0x80 });
        }
        protected override void WndProc(ref Message message)
        {
            uint id = (uint)message.Msg;
            if ((RendererProtocol.ReadyMessage != 0 && id == RendererProtocol.ReadyMessage) ||
                (RendererProtocol.PreferencesChangedMessage != 0 && id == RendererProtocol.PreferencesChangedMessage))
                _refresh();
            base.WndProc(ref message);
        }
    }
}
