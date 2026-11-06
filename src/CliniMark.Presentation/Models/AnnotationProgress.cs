namespace CliniMark.Presentation.Models;

public static class AnnotationProgress
{
    public static bool IsComplete(string? counter)
    {
        var parts = counter?.Split('/');
        return parts is { Length: 2 } && int.TryParse(parts[0], out var completed) &&
            int.TryParse(parts[1], out var total) && total > 0 && completed == total;
    }
}
