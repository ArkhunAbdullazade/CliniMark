namespace CliniMark.Core.Models;
public class MedDataKeyword
{
    public int Id { get; set; }
    public int MedDataId { get; set; }
    public MedData? MedData { get; set; }
    public string? Keyword { get; set; }
}