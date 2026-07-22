using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class ReportTableWriter
{
    private const int ColumnCount = 9;
    private static readonly CultureInfo SlovakCulture = CultureInfo.GetCultureInfo("sk-SK");

    public static void Insert(
        Database database,
        Transaction transaction,
        Point3d position,
        TimberReport report,
        CultureInfo uiCulture)
    {
        ArgumentNullException.ThrowIfNull(uiCulture);
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

        var rows = report.Lines.Count + 3;
        var table = new Table
        {
            TableStyle = database.Tablestyle,
            Position = position,
        };

        table.SetSize(rows, ColumnCount);
        table.SetRowHeight(8);
        table.SetColumnWidth(22);
        table.Columns[0].Width = 18;
        table.Columns[1].Width = 34;
        table.Columns[2].Width = 28;
        table.Columns[5].Width = 27;
        table.Columns[7].Width = 30;
        table.Columns[8].Width = 28;

        table.Cells[0, 0].TextString = UiStrings.GetString("Report_Title", uiCulture);
        table.MergeCells(CellRange.Create(table, 0, 0, 0, ColumnCount - 1));

        var headers = new[]
        {
            UiStrings.GetString("Report_Column_Item", uiCulture),
            UiStrings.GetString("Report_Column_Type", uiCulture),
            UiStrings.GetString("Report_Column_Material", uiCulture),
            UiStrings.GetString("Report_Column_WidthMm", uiCulture),
            UiStrings.GetString("Report_Column_HeightMm", uiCulture),
            UiStrings.GetString("Report_Column_PieceLengthM", uiCulture),
            UiStrings.GetString("Report_Column_Count", uiCulture),
            UiStrings.GetString("Report_Column_TotalLengthM", uiCulture),
            UiStrings.GetString("Report_Column_VolumeM3", uiCulture),
        };

        for (var column = 0; column < ColumnCount; column++)
        {
            table.Cells[1, column].TextString = headers[column];
        }

        for (var index = 0; index < report.Lines.Count; index++)
        {
            var row = report.Lines[index];
            var tableRow = index + 2;

            table.Cells[tableRow, 0].TextString = row.ElementId;
            table.Cells[tableRow, 1].TextString = TimberElementTypeDisplayNameProvider.GetDisplayName(
                row.ElementType,
                uiCulture);
            table.Cells[tableRow, 2].TextString = row.Material;
            table.Cells[tableRow, 3].TextString = Format(row.WidthMm, 0);
            table.Cells[tableRow, 4].TextString = Format(row.HeightMm, 0);
            table.Cells[tableRow, 5].TextString = Format(row.CuttingLengthMm / 1000d, 3);
            table.Cells[tableRow, 6].TextString = row.Count.ToString(SlovakCulture);
            table.Cells[tableRow, 7].TextString = Format(row.TotalLengthMm / 1000d, 3);
            table.Cells[tableRow, 8].TextString = Format(row.TotalVolumeM3, 4);
        }

        var totalRow = rows - 1;
        table.Cells[totalRow, 0].TextString = string.Format(
            uiCulture,
            UiStrings.GetString("Report_TotalFormat", uiCulture),
            report.SourceElementCount);
        table.MergeCells(CellRange.Create(table, totalRow, 0, totalRow, ColumnCount - 2));
        table.Cells[totalRow, ColumnCount - 1].TextString = Format(report.TotalVolumeM3, 4);

        modelSpace.AppendEntity(table);
        transaction.AddNewlyCreatedDBObject(table, true);
    }

    private static string Format(double value, int decimals) => value.ToString($"N{decimals}", SlovakCulture);
}
