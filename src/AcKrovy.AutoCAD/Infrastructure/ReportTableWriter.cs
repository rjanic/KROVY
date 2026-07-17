using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class ReportTableWriter
{
    private const int ColumnCount = 8;
    private static readonly CultureInfo SlovakCulture = CultureInfo.GetCultureInfo("sk-SK");

    public static void Insert(Database database, Transaction transaction, Point3d position, TimberReport report)
    {
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
        table.Columns[0].Width = 36;
        table.Columns[1].Width = 28;
        table.Columns[4].Width = 27;
        table.Columns[6].Width = 30;
        table.Columns[7].Width = 28;

        table.Cells[0, 0].TextString = "ACAD KROVY – výkaz reziva";
        table.MergeCells(CellRange.Create(table, 0, 0, 0, ColumnCount - 1));

        var headers = new[]
        {
            "Typ", "Materiál", "Šírka [mm]", "Výška [mm]", "Dĺžka kusu [m]", "Počet", "Celková dĺžka [m]", "Kubatúra [m³]",
        };

        for (var column = 0; column < ColumnCount; column++)
        {
            table.Cells[1, column].TextString = headers[column];
        }

        for (var index = 0; index < report.Lines.Count; index++)
        {
            var row = report.Lines[index];
            var tableRow = index + 2;

            table.Cells[tableRow, 0].TextString = TimberElementLabels.ToSlovak(row.ElementType);
            table.Cells[tableRow, 1].TextString = row.Material;
            table.Cells[tableRow, 2].TextString = Format(row.WidthMm, 0);
            table.Cells[tableRow, 3].TextString = Format(row.HeightMm, 0);
            table.Cells[tableRow, 4].TextString = Format(row.CuttingLengthMm / 1000d, 3);
            table.Cells[tableRow, 5].TextString = row.Count.ToString(SlovakCulture);
            table.Cells[tableRow, 6].TextString = Format(row.TotalLengthMm / 1000d, 3);
            table.Cells[tableRow, 7].TextString = Format(row.TotalVolumeM3, 4);
        }

        var totalRow = rows - 1;
        table.Cells[totalRow, 0].TextString = $"Spolu: {report.SourceElementCount} prvkov";
        table.MergeCells(CellRange.Create(table, totalRow, 0, totalRow, ColumnCount - 2));
        table.Cells[totalRow, ColumnCount - 1].TextString = Format(report.TotalVolumeM3, 4);

        modelSpace.AppendEntity(table);
        transaction.AddNewlyCreatedDBObject(table, true);
    }

    private static string Format(double value, int decimals) => value.ToString($"N{decimals}", SlovakCulture);
}
