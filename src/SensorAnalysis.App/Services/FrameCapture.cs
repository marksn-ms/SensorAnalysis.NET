using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SensorAnalysis.App.Controls;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.App.Services;

/// <summary>
/// Renders the current world to a PNG file for publication figures.
/// </summary>
public static class FrameCapture
{
    /// <summary>
    /// Capture <paramref name="canvas"/> to <paramref name="filePath"/> (PNG).
    /// Uses the control's current ActualWidth/Height; falls back to 800×600.
    /// </summary>
    public static void CaptureToPng(WorldCanvas canvas, string filePath, int? width = null, int? height = null)
    {
        var w = width ?? Math.Max(1, (int)canvas.ActualWidth);
        var h = height ?? Math.Max(1, (int)canvas.ActualHeight);
        if (w < 10) w = 800;
        if (h < 10) h = 600;

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        // Ensure layout is up to date at the desired size
        canvas.Measure(new Size(w, h));
        canvas.Arrange(new Rect(0, 0, w, h));
        rtb.Render(canvas);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var fs = File.Create(filePath);
        encoder.Save(fs);
    }

    /// <summary>
    /// Capture at a fixed cadence into <paramref name="directory"/>/tick-NNNNNN.png.
    /// Call from the UI thread after each tick when recording is enabled.
    /// </summary>
    public static string CaptureTick(WorldCanvas canvas, string directory, long tick)
    {
        var path = Path.Combine(directory, $"tick-{tick:D6}.png");
        CaptureToPng(canvas, path);
        return path;
    }
}
