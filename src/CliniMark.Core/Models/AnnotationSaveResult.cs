namespace CliniMark.Core.Models;

public sealed record AnnotationSaveResult(bool Success, string? Message = null, bool Conflict = false);
