using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using MedAnnotateApp.Core.Services;

namespace MedAnnotateApp.Tests;

public sealed class AnnotationMaskTests
{
    private static string Coordinates(int width, int height, params (string Type, double[][] Points)[] shapes) => JsonSerializer.Serialize(new
    {
        version = 1, coordinateSystem = "image-pixels", imageWidth = width, imageHeight = height,
        annotations = shapes.Select(shape => new { type = shape.Type, points = shape.Points })
    });

    private static double[][] Box(double left, double top, double right, double bottom) => [[left, top], [right, top], [right, bottom], [left, bottom]];

    // Column-major, as COCO run-length encoding reads: pixel (x, y) is entry x * height + y.
    private static bool[] Decode(AnnotationMask mask)
    {
        var pixels = new bool[mask.Width * mask.Height];
        var position = 0;
        for (var run = 0; run < mask.Counts.Count; run++)
        {
            if (run % 2 == 1) Array.Fill(pixels, true, position, (int)mask.Counts[run]);
            position += (int)mask.Counts[run];
        }
        Assert.Equal(pixels.Length, position);
        return pixels;
    }

    // An independent reference: tests every pixel centre with the winding number, one shape at a time.
    private static bool Inside(double[][] points, double x, double y)
    {
        var winding = 0;
        for (var index = 0; index < points.Length; index++)
        {
            var (from, to) = (points[index], points[(index + 1) % points.Length]);
            var side = (to[0] - from[0]) * (y - from[1]) - (x - from[0]) * (to[1] - from[1]);
            if (from[1] <= y && to[1] > y && side > 0) winding++;
            else if (from[1] > y && to[1] <= y && side < 0) winding--;
        }
        return winding != 0;
    }

    [Fact]
    public void ARectangleOnPixelEdgesCoversExactlyThosePixels()
    {
        var mask = AnnotationMask.FromCoordinates(Coordinates(50, 60, ("rectangle", Box(10, 30, 20, 40))));

        Assert.Equal(100, mask.Pixels);
        var pixels = Decode(mask);
        for (var x = 0; x < 50; x++)
            for (var y = 0; y < 60; y++)
                Assert.Equal(x is >= 10 and < 20 && y is >= 30 and < 40, pixels[x * 60 + y]);
    }

    [Fact]
    public void APixelCountsOnlyWhenItsCentreIsInside()
    {
        // Spans 0.6..2.4 on both axes: only pixel 1 has its centre (1.5) inside; 0.5 and 2.5 fall outside.
        var mask = AnnotationMask.FromCoordinates(Coordinates(4, 4, ("rectangle", Box(0.6, 0.6, 2.4, 2.4))));

        Assert.Equal(1, mask.Pixels);
        Assert.True(Decode(mask)[1 * 4 + 1]);
    }

    [Fact]
    public void TheWholeImageIsOneRunOfOnes()
    {
        var mask = AnnotationMask.FromCoordinates(Coordinates(7, 5, ("rectangle", Box(0, 0, 7, 5))));
        Assert.Equal([0L, 35L], mask.Counts);
    }

    [Fact]
    public void RunsContinueFromOneColumnToTheNext()
    {
        var mask = AnnotationMask.FromCoordinates(Coordinates(3, 4, ("rectangle", Box(1, 0, 3, 4))));
        Assert.Equal([4L, 8L], mask.Counts);
    }

    [Fact]
    public void ADiagonalContourMatchesThePixelCentreRule()
    {
        double[][] triangle = [[0, 0], [10, 0], [0, 10]];
        var pixels = Decode(AnnotationMask.FromCoordinates(Coordinates(10, 10, ("freehand", triangle))));
        for (var x = 0; x < 10; x++)
            for (var y = 0; y < 10; y++)
                Assert.Equal(Inside(triangle, x + 0.5, y + 0.5), pixels[x * 10 + y]);
    }

    [Fact]
    public void AContourThatCrossesItselfIsFilledOnBothSides()
    {
        // A figure of eight: the two lobes wind in opposite directions, and both are inside what was drawn.
        double[][] eight = [[2, 2], [18, 18], [18, 2], [2, 18]];
        var pixels = Decode(AnnotationMask.FromCoordinates(Coordinates(20, 20, ("freehand", eight))));
        Assert.True(pixels[15 * 20 + 10], "right lobe");
        Assert.True(pixels[4 * 20 + 10], "left lobe");
        Assert.False(pixels[10 * 20 + 2], "the notch above the crossing stays outside");
    }

    [Fact]
    public void ShapesOfOneAnnotationAreMergedEvenWhenDrawnInOppositeDirections()
    {
        double[][] clockwise = Box(2, 2, 12, 12), anticlockwise = [.. Box(7, 7, 17, 17).Reverse()];
        var mask = AnnotationMask.FromCoordinates(Coordinates(20, 20, ("rectangle", clockwise), ("rectangle", anticlockwise)));

        Assert.Equal(100 + 100 - 25, mask.Pixels);
        Assert.True(Decode(mask)[9 * 20 + 9], "the overlap must not cancel out");
    }

    [Fact]
    public void RandomContoursAgreeWithTheIndependentReferenceOnEveryPixel()
    {
        var random = new Random(20261106);
        for (var round = 0; round < 300; round++)
        {
            var (width, height) = (random.Next(3, 70), random.Next(3, 70));
            var shapes = Enumerable.Range(0, random.Next(1, 4)).Select(_ =>
                Enumerable.Range(0, random.Next(3, 24)).Select(_ => new[]
                {
                    Math.Round(random.NextDouble() * width, 3), Math.Round(random.NextDouble() * height, 3)
                }).ToArray()).ToArray();

            var pixels = Decode(AnnotationMask.FromCoordinates(Coordinates(width, height, shapes.Select(points => ("freehand", points)).ToArray())));

            for (var x = 0; x < width; x++)
                for (var y = 0; y < height; y++)
                    Assert.Equal(shapes.Any(points => Inside(points, x + 0.5, y + 0.5)), pixels[x * height + y]);
        }
    }

    [Fact]
    public void JsonIsCocoRunLengthEncodingAndReadsBackUnchanged()
    {
        var mask = AnnotationMask.FromCoordinates(Coordinates(5, 8, ("rectangle", Box(1, 2, 3, 4))));

        using var json = JsonDocument.Parse(mask.ToJson());
        Assert.Equal([8, 5], json.RootElement.GetProperty("size").EnumerateArray().Select(item => item.GetInt32()));
        var read = AnnotationMask.Parse(mask.ToJson());
        Assert.Equal(mask.Counts, read.Counts);
        Assert.Equal((5, 8, 4L), (read.Width, read.Height, read.Pixels));
    }

    [Theory]
    [InlineData(13, 9)]   // A row width that doesn't fill its last byte.
    [InlineData(16, 8)]
    public void PngHoldsTheSamePixels(int width, int height)
    {
        var mask = AnnotationMask.FromCoordinates(Coordinates(width, height, ("freehand", [[1, 1], [width - 1.5, 2], [width - 2, height - 1], [2, height - 2.5]])));
        var png = mask.ToPng();

        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
        Assert.Equal((width, height), (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20))));
        Assert.Equal(1, png[24]); // Bit depth.
        Assert.Equal(0, png[25]); // Greyscale.

        var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33));
        Assert.Equal("IDAT", System.Text.Encoding.ASCII.GetString(png, 37, 4));
        using var zlib = new ZLibStream(new MemoryStream(png, 41, length), CompressionMode.Decompress);
        using var rows = new MemoryStream();
        zlib.CopyTo(rows);
        var data = rows.ToArray();
        var stride = (width + 7) / 8 + 1;
        Assert.Equal(stride * height, data.Length);
        var pixels = Decode(mask);
        for (var y = 0; y < height; y++)
        {
            Assert.Equal(0, data[y * stride]);
            for (var x = 0; x < width; x++)
                Assert.Equal(pixels[x * height + y], (data[y * stride + 1 + x / 8] & (0x80 >> (x % 8))) != 0);
        }
    }
}
