using CliniMark.Core.Models;
using CliniMark.Core.Services;
using OfficeOpenXml;

namespace CliniMark.Infrastructure.Services;

public class ExcelLoaderService : IExcelLoaderService
{
    private const int FirstDataRow = 3;

    public List<MedData> LoadMedDataFromExcel(string filePath)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        using var package = new ExcelPackage(new FileInfo(filePath));
        var worksheet = GetFirstWorksheet(package, filePath);
        var medDataList = new List<MedData>();

        for (var row = FirstDataRow; row <= worksheet.Dimension.Rows; row++)
        {
            if (!TryGetMedDataId(worksheet, row, out var medDataId))
            {
                continue;
            }

            medDataList.Add(new MedData
            {
                Id = medDataId,
                Pmcid = GetCellValue(worksheet, row, 2),
                ImageUrl = GetCellValue(worksheet, row, 5),
                ImageDescription = GetCellValue(worksheet, row, 11),
                Sex = GetCellValue(worksheet, row, 19),
                Age = GetCellValue(worksheet, row, 20),
                SkinTone = GetCellValue(worksheet, row, 21),
                BodyRegion = GetCellValue(worksheet, row, 22),
                Diagnosis = GetCellValue(worksheet, row, 23),
                TreatmentName = GetCellValue(worksheet, row, 27),
                Speciality = GetCellValue(worksheet, row, 31),
                Modality = GetCellValue(worksheet, row, 32),
                IsAnnotated = false,
            });
        }

        return medDataList;
    }

    public List<(int MedDataId, string Keyword)> LoadMedKeywordsFromExcel(string filePath)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        using var package = new ExcelPackage(new FileInfo(filePath));
        var worksheet = GetFirstWorksheet(package, filePath);
        var keywordList = new List<(int MedDataId, string Keyword)>();

        for (var row = FirstDataRow; row <= worksheet.Dimension.Rows; row++)
        {
            if (!TryGetMedDataId(worksheet, row, out var medDataId))
            {
                continue;
            }

            var keywords = GetCellValue(worksheet, row, 12);
            if (string.IsNullOrWhiteSpace(keywords))
            {
                continue;
            }

            foreach (var keyword in keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                keywordList.Add((medDataId, keyword));
            }
        }

        return keywordList;
    }

    private static ExcelWorksheet GetFirstWorksheet(ExcelPackage package, string filePath)
    {
        return package.Workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidDataException($"Excel workbook '{filePath}' does not contain any worksheets.");
    }

    private static bool TryGetMedDataId(ExcelWorksheet worksheet, int row, out int medDataId)
    {
        var value = GetCellValue(worksheet, row, 1);
        return int.TryParse(value, out medDataId);
    }

    private static string? GetCellValue(ExcelWorksheet worksheet, int row, int column)
    {
        return worksheet.Cells[row, column].Value?.ToString();
    }
}
