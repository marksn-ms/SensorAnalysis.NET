using System.IO;
using System.Text;

namespace SensorAnalysis.App.Services;

/// <summary>
/// Writes a minimal Motion-JPEG AVI (RIFF) from pre-encoded JPEG frame payloads.
/// Pure managed — no ffmpeg / native codecs. Playback support varies by player;
/// Windows Media Player and VLC typically handle MJPG AVI; some browsers do not.
/// Classic AVI index limits apply (no OpenDML; keep sessions under ~2 GB).
/// </summary>
public sealed class AviMjpegWriter
{
    /// <summary>
    /// Writes an MJPEG AVI file containing the given JPEG frames.
    /// </summary>
    /// <param name="path">Destination <c>.avi</c> path.</param>
    /// <param name="frames">JPEG byte arrays in display order (same dimensions assumed).</param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="framesPerSecond">Playback rate (clamped to at least 1).</param>
    public static void Write(string path, IReadOnlyList<byte[]> frames, int width, int height, double framesPerSecond)
    {
        if (frames.Count == 0)
            throw new ArgumentException("At least one JPEG frame is required.", nameof(frames));
        if (width < 1 || height < 1)
            throw new ArgumentOutOfRangeException(nameof(width), "Frame size must be positive.");

        var fps = Math.Max(1.0, framesPerSecond);
        var microSecPerFrame = (uint)Math.Round(1_000_000.0 / fps);
        var maxFrame = frames.Max(f => f.Length);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: false);

        // Placeholder RIFF size; patched at end.
        WriteFourCC(bw, "RIFF");
        var riffSizePos = bw.BaseStream.Position;
        bw.Write(0);

        WriteFourCC(bw, "AVI ");

        // LIST hdrl
        WriteFourCC(bw, "LIST");
        var hdrlSizePos = bw.BaseStream.Position;
        bw.Write(0);
        WriteFourCC(bw, "hdrl");

        // avih
        WriteFourCC(bw, "avih");
        bw.Write(56);
        bw.Write(microSecPerFrame);
        bw.Write((uint)(maxFrame * fps)); // dwMaxBytesPerSec (approx)
        bw.Write(0); // dwPaddingGranularity
        bw.Write(0x10u); // AVIF_HASINDEX
        bw.Write((uint)frames.Count);
        bw.Write(0); // dwInitialFrames
        bw.Write(1); // dwStreams
        bw.Write((uint)maxFrame); // dwSuggestedBufferSize
        bw.Write(width);
        bw.Write(height);
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); // dwReserved

        // LIST strl
        WriteFourCC(bw, "LIST");
        var strlSizePos = bw.BaseStream.Position;
        bw.Write(0);
        WriteFourCC(bw, "strl");

        // strh
        WriteFourCC(bw, "strh");
        bw.Write(56);
        WriteFourCC(bw, "vids");
        WriteFourCC(bw, "MJPG");
        bw.Write(0); // dwFlags
        bw.Write((ushort)0); // wPriority
        bw.Write((ushort)0); // wLanguage
        bw.Write(0); // dwInitialFrames
        bw.Write(1); // dwScale
        bw.Write((int)Math.Round(fps)); // dwRate
        bw.Write(0); // dwStart
        bw.Write(frames.Count); // dwLength
        bw.Write(maxFrame); // dwSuggestedBufferSize
        bw.Write(-1); // dwQuality
        bw.Write(0); // dwSampleSize
        bw.Write((short)0); // left
        bw.Write((short)0); // top
        bw.Write((short)width); // right
        bw.Write((short)height); // bottom

        // strf — BITMAPINFOHEADER with MJPG
        WriteFourCC(bw, "strf");
        bw.Write(40);
        bw.Write(40); // biSize
        bw.Write(width);
        bw.Write(height);
        bw.Write((ushort)1); // biPlanes
        bw.Write((ushort)24); // biBitCount
        WriteFourCC(bw, "MJPG");
        bw.Write(maxFrame); // biSizeImage
        bw.Write(0); // biXPelsPerMeter
        bw.Write(0); // biYPelsPerMeter
        bw.Write(0); // biClrUsed
        bw.Write(0); // biClrImportant

        PatchChunkSize(bw, strlSizePos);
        PatchChunkSize(bw, hdrlSizePos);

        // LIST movi
        WriteFourCC(bw, "LIST");
        var moviSizePos = bw.BaseStream.Position;
        bw.Write(0);
        WriteFourCC(bw, "movi");

        var indexEntries = new List<(uint Offset, uint Size)>(frames.Count);
        var moviDataStart = bw.BaseStream.Position;

        foreach (var jpeg in frames)
        {
            // Offset relative to start of 'movi' list content (includes the 'movi' FourCC → first chunk @ 4).
            var chunkOffset = (uint)(bw.BaseStream.Position - moviDataStart + 4);
            WriteFourCC(bw, "00dc");
            bw.Write(jpeg.Length);
            bw.Write(jpeg);
            if ((jpeg.Length & 1) != 0)
                bw.Write((byte)0); // word align
            indexEntries.Add((chunkOffset, (uint)jpeg.Length));
        }

        PatchChunkSize(bw, moviSizePos);

        // idx1
        WriteFourCC(bw, "idx1");
        bw.Write(indexEntries.Count * 16);
        foreach (var (offset, size) in indexEntries)
        {
            WriteFourCC(bw, "00dc");
            bw.Write(0x10u); // AVIIF_KEYFRAME
            bw.Write(offset);
            bw.Write(size);
        }

        PatchChunkSize(bw, riffSizePos);
    }

    private static void WriteFourCC(BinaryWriter bw, string fourCc)
    {
        if (fourCc.Length != 4)
            throw new ArgumentException("FourCC must be 4 characters.", nameof(fourCc));
        bw.Write(Encoding.ASCII.GetBytes(fourCc));
    }

    private static void PatchChunkSize(BinaryWriter bw, long sizeFieldPos)
    {
        var end = bw.BaseStream.Position;
        var size = (int)(end - sizeFieldPos - 4);
        bw.BaseStream.Position = sizeFieldPos;
        bw.Write(size);
        bw.BaseStream.Position = end;
    }
}
