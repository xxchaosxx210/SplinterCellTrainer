using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SCTrainer;

internal sealed class EnemyOverlay : Window
{
    private readonly EnemyMemoryReader _reader;
    private readonly Action<string> _status;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly MarkerSurface _surface = new();
    private bool _busy, _closed;
    private IntPtr _hwnd;
    private DateTime _lastStatus;
    public EnemyOverlay(EnemyMemoryReader reader, Action<string> status)
    {
        _reader = reader; _status = status;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Content = _surface; Width = 1; Height = 1; Left = -10000; Top = -10000;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int style = GetWindowLong(_hwnd, -20);
            SetWindowLong(_hwnd, -20, style | 0x20 | 0x80 | 0x08000000); // Click-through, tool window, no activation.
        };
        _timer.Tick += Tick;
        Closed += (_, _) => { _closed = true; _timer.Stop(); _timer.Tick -= Tick; if (!_busy) _reader.Dispose(); };
        _timer.Start();
    }

    private async void Tick(object? sender, EventArgs e)
    {
        if (_busy || _closed) return;
        _busy = true;
        try
        {
            var game = _reader.GameWindow;
            if (!IsWindow(game)) { _status("Game closed. Enable ESP again after restarting."); Close(); return; }
            if (IsIconic(game) || GetForegroundWindow() != game)
            {
                _surface.Frame = null; _surface.InvalidateVisual();
                return;
            }
            if (!GetClientRect(game, out var rect)) return;
            var origin = new PointNative();
            if (!ClientToScreen(game, ref origin)) return;
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (width < 1 || height < 1) return;
            SetWindowPos(_hwnd, new IntPtr(-1), origin.X, origin.Y, width, height, 0x10 | 0x40);
            var snapshot = await Task.Run(_reader.Snapshot);
            if (_closed) return;
            // Focus may change while the snapshot is being read.
            _surface.Frame = GetForegroundWindow() == game ? snapshot : null;
            _surface.InvalidateVisual();
            if (DateTime.UtcNow - _lastStatus > TimeSpan.FromSeconds(1))
            {
                _lastStatus = DateTime.UtcNow;
                _status($"ESP enabled • {snapshot.Enemies.Length} live hostile characters tracked");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or OverflowException)
        {
            _surface.Frame = null; _surface.InvalidateVisual();
            _status(ex.Message);
        }
        finally { _busy = false; if (_closed) _reader.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out RectNative rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref PointNative point);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    private sealed class MarkerSurface : FrameworkElement
    {
        public EnemySnapshot? Frame { get; set; }
        private static readonly Pen Outline = new(Brushes.Black, 4);
        private static readonly Pen Green = new(new SolidColorBrush(Color.FromRgb(181, 239, 101)), 1.5);
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var f = Frame;
            if (f is null) return;
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
            foreach (var enemy in f.Enemies)
            {
                if (!EnemyProjection.TryProject(enemy.Position + new Vector3(0, 0, enemy.Height), f.Camera,
                    f.Pitch, f.Yaw, f.Roll, f.Fov, ActualWidth, ActualHeight, out var x, out var y)) continue;
                if (x < 0 || x > ActualWidth || y < 0 || y > ActualHeight) continue;
                var diamond = new StreamGeometry();
                using (var context = diamond.Open())
                {
                    context.BeginFigure(new Point(x, y - 6), false, true);
                    context.LineTo(new Point(x + 6, y), true, false);
                    context.LineTo(new Point(x, y + 6), true, false);
                    context.LineTo(new Point(x - 6, y), true, false);
                }
                dc.DrawGeometry(null, Outline, diamond); dc.DrawGeometry(null, Green, diamond);
                double distance = Vector3.Distance(f.Camera, enemy.Position);
                var label = new FormattedText($"{enemy.Name} • {distance:0} units", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Green.Brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)), null,
                    new Rect(x - label.Width / 2 - 4, y + 9, label.Width + 8, label.Height + 4));
                dc.DrawText(label, new Point(x - label.Width / 2, y + 11));
            }
            dc.Pop();
        }
    }
}
