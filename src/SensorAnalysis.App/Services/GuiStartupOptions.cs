using System.IO;

namespace SensorAnalysis.App.Services;

/// <summary>
/// Command-line options for unattended GUI runs (load recipe → run → record → exit).
/// </summary>
public sealed class GuiStartupOptions
{
    /// <summary>Recipe JSON path, if any.</summary>
    public string? RecipePath { get; init; }

    /// <summary>Start Run after the window loads.</summary>
    public bool AutoRun { get; init; }

    /// <summary>Stop when world tick reaches this value (null = never auto-stop).</summary>
    public int? AutoStopTicks { get; init; }

    /// <summary>Enable MJPEG video capture.</summary>
    public bool RecordVideo { get; init; }

    /// <summary>AVI output path (skips SaveFileDialog when set).</summary>
    public string? VideoPath { get; init; }

    /// <summary>Enable PNG frame capture.</summary>
    public bool RecordFrames { get; init; }

    /// <summary>PNG output directory (default under experiments/ when recording).</summary>
    public string? FramesDirectory { get; init; }

    /// <summary>Run-timer delay in milliseconds.</summary>
    public int? TickDelayMs { get; init; }

    /// <summary>Capture every N ticks.</summary>
    public int? FrameEveryNTicks { get; init; }

    /// <summary>Shut down the app when the run stops.</summary>
    public bool ExitOnStop { get; init; }

    /// <summary>Parse process args. First existing *.json is the recipe; flags use --name [value].</summary>
    public static GuiStartupOptions Parse(string[] args)
    {
        string? recipe = null;
        var autoRun = false;
        int? stopTicks = null;
        var recordVideo = false;
        string? videoPath = null;
        var recordFrames = false;
        string? framesDir = null;
        int? delayMs = null;
        int? frameEvery = null;
        var exitOnStop = false;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(a) && recipe is null)
            {
                recipe = Path.GetFullPath(a);
                continue;
            }

            static bool IsFlag(string s, string name) =>
                s.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                s.Equals(name.TrimStart('-'), StringComparison.OrdinalIgnoreCase);

            if (IsFlag(a, "--run") || IsFlag(a, "-run"))
            {
                autoRun = true;
                continue;
            }

            if (IsFlag(a, "--exit-on-stop") || IsFlag(a, "--exit"))
            {
                exitOnStop = true;
                continue;
            }

            if (IsFlag(a, "--record-video") || IsFlag(a, "--record-avi"))
            {
                recordVideo = true;
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-') &&
                    !args[i + 1].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    videoPath = Path.GetFullPath(args[++i]);
                }
                continue;
            }

            if (IsFlag(a, "--record-frames"))
            {
                recordFrames = true;
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-') &&
                    !args[i + 1].EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                    !args[i + 1].EndsWith(".avi", StringComparison.OrdinalIgnoreCase))
                {
                    framesDir = Path.GetFullPath(args[++i]);
                }
                continue;
            }

            if ((IsFlag(a, "--ticks") || IsFlag(a, "--auto-stop-ticks")) && i + 1 < args.Length &&
                int.TryParse(args[i + 1], out var ticks))
            {
                stopTicks = ticks;
                i++;
                continue;
            }

            if ((IsFlag(a, "--delay-ms") || IsFlag(a, "--tick-delay-ms")) && i + 1 < args.Length &&
                int.TryParse(args[i + 1], out var delay))
            {
                delayMs = delay;
                i++;
                continue;
            }

            if ((IsFlag(a, "--frame-every") || IsFlag(a, "--frame-every-n")) && i + 1 < args.Length &&
                int.TryParse(args[i + 1], out var every))
            {
                frameEvery = every;
                i++;
                continue;
            }
        }

        // Recording implies a finite run when ticks were given; ticks + record implies auto-run.
        if ((recordVideo || recordFrames) && stopTicks is not null)
            autoRun = true;
        if (autoRun && stopTicks is not null && (recordVideo || recordFrames))
            exitOnStop = exitOnStop || recordVideo || recordFrames;

        return new GuiStartupOptions
        {
            RecipePath = recipe,
            AutoRun = autoRun,
            AutoStopTicks = stopTicks,
            RecordVideo = recordVideo,
            VideoPath = videoPath,
            RecordFrames = recordFrames,
            FramesDirectory = framesDir,
            TickDelayMs = delayMs,
            FrameEveryNTicks = frameEvery,
            ExitOnStop = exitOnStop
        };
    }
}
