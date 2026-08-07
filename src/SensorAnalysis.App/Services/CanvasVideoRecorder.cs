using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SensorAnalysis.App.Controls;

namespace SensorAnalysis.App.Services;

/// <summary>
/// Accumulates JPEG frames captured from a <see cref="WorldCanvas"/> for later AVI export.
/// </summary>
public sealed class CanvasVideoRecorder
{
    private readonly List<byte[]> _frames = new();
    private int _width;
    private int _height;

    /// <summary>Number of frames captured in the current session.</summary>
    public int FrameCount => _frames.Count;

    /// <summary>True after <see cref="Begin"/> until <see cref="Clear"/>.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>Pixel width of captured frames (0 before first capture).</summary>
    public int Width => _width;

    /// <summary>Pixel height of captured frames (0 before first capture).</summary>
    public int Height => _height;

    /// <summary>Starts a new recording session, discarding any previous frames.</summary>
    public void Begin()
    {
        _frames.Clear();
        _width = 0;
        _height = 0;
        IsRecording = true;
    }

    /// <summary>
    /// Renders <paramref name="canvas"/> to a JPEG and appends it to the session.
    /// No-op when not recording.
    /// </summary>
    /// <param name="canvas">Live world canvas (UI thread).</param>
    /// <param name="quality">JPEG quality 1–100.</param>
    public void CaptureFrame(WorldCanvas canvas, int quality = 85)
    {
        if (!IsRecording) return;

        var w = Math.Max(1, (int)canvas.ActualWidth);
        var h = Math.Max(1, (int)canvas.ActualHeight);
        if (w < 10) w = 800;
        if (h < 10) h = 600;

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        canvas.Measure(new Size(w, h));
        canvas.Arrange(new Rect(0, 0, w, h));
        rtb.Render(canvas);

        var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        _frames.Add(ms.ToArray());

        if (_width == 0)
        {
            _width = w;
            _height = h;
        }
    }

    /// <summary>
    /// Ends the session and writes accumulated frames as an MJPEG AVI.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="framesPerSecond">Playback rate.</param>
    /// <returns><c>true</c> when a file was written; <c>false</c> if no frames were captured.</returns>
    public bool SaveAvi(string path, double framesPerSecond)
    {
        IsRecording = false;
        if (_frames.Count == 0) return false;
        AviMjpegWriter.Write(path, _frames, _width, _height, framesPerSecond);
        return true;
    }

    /// <summary>Discards frames and ends the session without writing.</summary>
    public void Clear()
    {
        _frames.Clear();
        _width = 0;
        _height = 0;
        IsRecording = false;
    }
}
