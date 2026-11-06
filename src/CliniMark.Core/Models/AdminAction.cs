namespace CliniMark.Core.Models;

/// <summary>Audit row for every change made from the admin panel or by the lock sweeper. Ids only, never emails.</summary>
public class AdminAction
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public string? AdminUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public int? MedDataId { get; set; }
    public string? TargetUserId { get; set; }
    public string? Details { get; set; }
}
