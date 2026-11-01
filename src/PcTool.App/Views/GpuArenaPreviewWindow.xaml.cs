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
    private bool _closeWhenBenchmarkEnds;

    public static void ShowSingle(Window? owner, bool closeWhenBenchmarkEnds = false)
    {
        if (_instance is not null)
        {
            if (_instance.WindowState == WindowState.Minimized)
                _instance.WindowState = WindowState.Normal;
            _instance.Activate();
            return;
        }

        _instance = new GpuArenaPreviewWindow { Owner = owner, _closeWhenBenchmarkEnds = closeWhenBenchmarkEnds };
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
    }

    public static void CloseBenchmarkPreview()
    {
        if (_instance is { _closeWhenBenchmarkEnds: true })
            _instance.Close();
    }

    private GpuArenaPreviewWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            try
            {
                _session = GpuStressHub.Acquire();
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
            if (_session is not null)
            {
                GpuStressHub.Release();
                _session = null;
            }
        };
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_session is null || _bitmap is null) return;
        var gpu = _session.Fps;
        var preview = _session.PreviewFps;
        if (gpu > 0)
            FpsText.Text = $"{gpu:0} FPS";
        if (gpu > 0 || preview > 0)
        {
            FrameText.Text = preview > 0
                ? $"1440p GPU · preview {preview:0} fps to window"
                : "1440p GPU · measuring preview…";
        }

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
