namespace ProtoTest.SampleApp.Northstar;

using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ProtoTest.SampleApp.Contracts;

/// <summary>Writes the monthly project report as a real OpenXML workbook, like a report library would.</summary>
internal static class MonthlyReportWriter
{
    public static byte[] Write(IReadOnlyList<ProjectResponse> projects)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);
            workbookPart.Workbook.AppendChild(new Sheets()).Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Summary"
            });

            var sharedStrings = workbookPart.AddNewPart<SharedStringTablePart>();
            sharedStrings.SharedStringTable = new SharedStringTable();

            Cell Text(string reference, string value)
            {
                sharedStrings.SharedStringTable.AppendChild(
                    new SharedStringItem(new DocumentFormat.OpenXml.Spreadsheet.Text(value)));
                return new Cell
                {
                    CellReference = reference,
                    DataType = CellValues.SharedString,
                    CellValue = new CellValue(
                        (sharedStrings.SharedStringTable.ChildElements.Count - 1).ToString(CultureInfo.InvariantCulture))
                };
            }

            Cell Number(string reference, double value) => new()
            {
                CellReference = reference,
                CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
            };

            sheetData.Append(new Row(
                Text("A1", "Name"),
                Text("B1", "Status"),
                Text("C1", "Environments")));
            var row = 2;
            foreach (var project in projects)
            {
                sheetData.Append(new Row(
                    Text($"A{row}", project.Name),
                    Text($"B{row}", project.Status),
                    Number($"C{row}", project.EnvironmentCount)));
                row++;
            }

            worksheetPart.Worksheet.Save();
            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }
}
