using CliniMark.Core.Models;

namespace CliniMark.Core.Services;

public interface IExcelLoaderService
{
    List<MedData> LoadMedDataFromExcel(string filePath);
    List<(int MedDataId, string Keyword)> LoadMedKeywordsFromExcel(string filePath);
}
