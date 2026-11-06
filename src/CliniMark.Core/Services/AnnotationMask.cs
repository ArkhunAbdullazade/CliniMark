using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CliniMark.Core.Services;

/// <summary>
/// One annotation as a binary pixel mask of the whole image: 1 inside what the annotator drew, 0 everywhere else, for
/// comparing with segmentation models (Dice, IoU). The saved coordinates stay the original record; the mask is computed
/// from them by this one rule, on the server, so it never depends on the annotator's browser.
///
/// A pixel is inside when its centre is. Pixel (column x, row y) covers x..x+1, y..y+1 in the saved coordinates, so its
/// centre is (x + 0.5, y + 0.5). A contour that crosses itself is filled throughout (non-zero winding), and the shapes of
/// one annotation are merged into one mask.
///
/// Stored as COCO uncompressed run-length encoding: {"size":[height,width],"counts":[...]}, alternating runs of 0s and 1s,
/// starting with 0s, reading the image column by column. pycocotools reads it directly.
/// </summary>
public sealed class AnnotationMask
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(value =>
    {
        var crc = (uint)value;
        for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        return crc;
    }).ToArray();

    private AnnotationMask(int width, int height, IReadOnlyList<long> counts)
    {
        Width = width;
        Height = height;
        Counts = counts;
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<long> Counts { get; }

    /// <summary>How many pixels are 1: the annotation's area.</summary>
    public long Pixels => Counts.Where((_, index) => index % 2 == 1).Sum();

    /// <summary>Rasterizes coordinates already accepted by <see cref="AnnotationCoordinates.TryValidate"/>.</summary>
    public static AnnotationMask FromCoordinates(string coordinates)
    {
        using var document = JsonDocument.Parse(coordinates);
        var root = document.RootElement;
        var width = root.GetProperty("imageWidth").GetInt32();
        var height = root.GetProperty("imageHeight").GetInt32();
        var columns = new List<(int Start, int End)>?[width];
        foreach (var shape in root.GetProperty("annotations").EnumerateArray())
            FillShape(shape.GetProperty("points").EnumerateArray().Select(point => (X: point[0].GetDouble(), Y: point[1].GetDouble())).ToArray(), height, columns);
        return new AnnotationMask(width, height, RunLengths(columns, height));
    }

    public static AnnotationMask Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var size = document.RootElement.GetProperty("size");
        return new AnnotationMask(size[1].GetInt32(), size[0].GetInt32(),
            document.RootElement.GetProperty("counts").EnumerateArray().Select(count => count.GetInt64()).ToList());
    }

    public string ToJson() => JsonSerializer.Serialize(new { size = new[] { Height, Width }, counts = Counts });

    // Walks each pixel column, so a shape never needs a whole-image bitmap, however large the image.
    private static void FillShape((double X, double Y)[] points, int height, List<(int Start, int End)>?[] columns)
    {
        var first = Math.Max(0, (int)Math.Ceiling(points.Min(point => point.X) - 0.5));
        var last = Math.Min(columns.Length - 1, (int)Math.Ceiling(points.Max(point => point.X) - 0.5) - 1);
        var crossings = new List<(double Y, int Direction)>();
        for (var x = first; x <= last; x++)
        {
            var centre = x + 0.5;
            crossings.Clear();
            for (var index = 0; index < points.Length; index++)
            {
                var (from, to) = (points[index], points[(index + 1) % points.Length]);
                // Half-open, so a contour running exactly through a pixel centre is counted once.
                if (from.X <= centre == to.X <= centre) continue;
                crossings.Add((from.Y + (centre - from.X) * (to.Y - from.Y) / (to.X - from.X), from.X < to.X ? 1 : -1));
            }
            crossings.Sort((left, right) => left.Y.CompareTo(right.Y));
            var winding = 0;
            for (var index = 0; index + 1 < crossings.Count; index++)
            {
                winding += crossings[index].Direction;
                if (winding == 0) continue;
                var start = Math.Max(0, (int)Math.Ceiling(crossings[index].Y - 0.5));
                var end = Math.Min(height, (int)Math.Ceiling(crossings[index + 1].Y - 0.5));
                if (start < end) (columns[x] ??= []).Add((start, end));
            }
        }
    }

    private static List<long> RunLengths(List<(int Start, int End)>?[] columns, int height)
    {
        var counts = new List<long>();
        long written = 0;
        for (var x = 0; x < columns.Length; x++)
        {
            if (columns[x] is not { } spans) continue;
            spans.Sort();
            var offset = (long)x * height;
            for (var index = 0; index < spans.Count;)
            {
                var (start, end) = spans[index++];
                while (index < spans.Count && spans[index].Start <= end) end = Math.Max(end, spans[index++].End);
                // Touching the previous run (the end of one column and the top of the next) continues it.
                if (offset + start == written && counts.Count > 0) counts[^1] += end - start;
                else
                {
                    counts.Add(offset + start - written);
                    counts.Add(end - start);
                }
                written = offset + end;
            }
        }
        var total = (long)columns.Length * height;
        if (written < total) counts.Add(total - written);
        return counts;
    }

    /// <summary>The mask as a 1-bit PNG the size of the image: white is 1, black is 0.</summary>
    public byte[] ToPng()
    {
        var stride = (Width + 7) / 8 + 1; // Each PNG row starts with a filter byte, left at 0 (none).
        var rows = new byte[(long)stride * Height];
        long position = 0;
        for (var run = 0; run < Counts.Count; run++)
        {
            if (run % 2 == 1)
                for (var pixel = position; pixel < position + Counts[run]; pixel++)
                {
                    var (x, y) = ((int)(pixel / Height), (int)(pixel % Height));
                    rows[(long)y * stride + 1 + x / 8] |= (byte)(0x80 >> (x % 8));
                }
            position += Counts[run];
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(rows);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Height);
        header[8] = 1; // Bit depth 1, colour type 0 (greyscale); the remaining fields are 0.

        using var png = new MemoryStream();
        png.Write(PngSignature);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        var name = Encoding.ASCII.GetBytes(type);
        output.Write(name);
        output.Write(data);
        var crc = 0xFFFFFFFFu;
        foreach (var value in name.Concat(data)) crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(number, crc ^ 0xFFFFFFFFu);
        output.Write(number);
    }
}
