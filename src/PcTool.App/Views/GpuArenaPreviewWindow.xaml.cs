using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PcTool.Benchmarks;

namespace PcTool.App.Views;

public partial class GpuArenaPreviewWindow : Window
{
    private static GpuArenaPreviewWindow? _instance;

    private GpuStressSession? _session;
    private WriteableBitmap? _bitmap;
    private int _shownVersion;

    public static void ShowSingle(Window? owner)
    {
        if (_instance is not null)
        {
            if (_instance.WindowState == WindowState.Minimized)
                _instance.WindowState = WindowState.Normal;
            _instance.Activate();
            return;
        }

        _instance = new GpuArenaPreviewWindow { Owner = owner };
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
    }

    private GpuArenaPreviewWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            try
            {
                _session = new GpuStressSession();
                _bitmap = new WriteableBitmap(GpuStressSession.PreviewWidth, GpuStressSession.PreviewHeight, 96, 96, PixelFormats.Bgra32, null);
                Viewport.Source = _bitmap;
                CompositionTarget.Rendering += OnRendering;
            }
            catch (Exception ex)
            {
                ErrorText.Text = ex.Message;
                ErrorText.Visibility = Visibility.Visible;
            }
        };
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRendering;
            _session?.Dispose();
            _session = null;
        };
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_session is null || _bitmap is null) return;
        var fps = _session.Fps;
        if (fps > 0)
            FpsText.Text = $"{fps:0} FPS";

        _bitmap.Lock();
        try
        {
            if (_session.CopyPreview(_bitmap.BackBuffer, _bitmap.BackBufferStride, out var version) && version != _shownVersion)
            {
                _bitmap.AddDirtyRect(new Int32Rect(0, 0, GpuStressSession.PreviewWidth, GpuStressSession.PreviewHeight));
                _shownVersion = version;
            }
        }
        finally
        {
            _bitmap.Unlock();
        }
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
