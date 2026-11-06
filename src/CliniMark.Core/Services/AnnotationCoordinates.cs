using System.Text.Json;

namespace CliniMark.Core.Services;

/// <summary>Validates new, versioned original-image-pixel annotations. Never converts legacy data.</summary>
public static class AnnotationCoordinates
{
    public static bool TryValidate(string? json, out string error)
    {
        error = "Invalid or unsupported annotation coordinates.";
        if (string.IsNullOrWhiteSpace(json) || json.Length > 4_000_000) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var v) || v != 1 ||
                !root.TryGetProperty("coordinateSystem", out var system) || system.GetString() != "image-pixels" ||
                !root.TryGetProperty("imageWidth", out var widthValue) || !widthValue.TryGetInt32(out var width) || width < 1 || width > 100000 ||
                !root.TryGetProperty("imageHeight", out var heightValue) || !heightValue.TryGetInt32(out var height) || height < 1 || height > 100000 ||
                !root.TryGetProperty("annotations", out var annotations) || annotations.ValueKind != JsonValueKind.Array ||
                annotations.GetArrayLength() is < 1 or > 200) return false;

            foreach (var annotation in annotations.EnumerateArray())
            {
                if (annotation.ValueKind != JsonValueKind.Object || !annotation.TryGetProperty("type", out var typeValue)) return false;
                var type = typeValue.GetString();
                if (type is not ("rectangle" or "freehand") || !annotation.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array ||
                    points.GetArrayLength() is < 3 or > 20000 || (type == "rectangle" && points.GetArrayLength() != 4)) return false;
                var coordinates = new List<(double X, double Y)>();
                foreach (var point in points.EnumerateArray())
                {
                    if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 2 ||
                        !point[0].TryGetDouble(out var x) || !point[1].TryGetDouble(out var y) ||
                        !double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x > width || y > height) return false;
                    coordinates.Add((x, y));
                }
                double twiceArea = 0;
                for (var i = 0; i < coordinates.Count; i++)
                {
                    var next = coordinates[(i + 1) % coordinates.Count];
                    twiceArea += coordinates[i].X * next.Y - next.X * coordinates[i].Y;
                }
                if (Math.Abs(twiceArea) < 2) return false;
                if (type == "rectangle")
                {
                    var a = coordinates[0]; var b = coordinates[1]; var c = coordinates[2]; var d = coordinates[3];
                    var ux = b.X - a.X; var uy = b.Y - a.Y; var vx = c.X - b.X; var vy = c.Y - b.Y;
                    // Vertex rounding is at 0.001px; allow only that small error
                    // when checking opposite-edge closure and perpendicularity.
                    const double tolerance = 0.003;
                    if (Math.Abs(a.X + c.X - b.X - d.X) > tolerance || Math.Abs(a.Y + c.Y - b.Y - d.Y) > tolerance ||
                        Math.Abs(ux * vx + uy * vy) > tolerance * (Math.Sqrt(ux * ux + uy * uy) + Math.Sqrt(vx * vx + vy * vy))) return false;
                }
            }
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            return false;
        }
    }
}
