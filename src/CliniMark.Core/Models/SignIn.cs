namespace CliniMark.Core.Models;

/// <summary>A successful sign-in, so the weekly report can say how often each participant came in. Account id only.</summary>
public class SignIn
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime At { get; set; }
}
