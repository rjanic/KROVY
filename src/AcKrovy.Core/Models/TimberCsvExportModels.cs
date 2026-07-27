using System.Globalization;

namespace AcKrovy.Core.Models;

public enum TimberCsvExportMode
{
    Individual,
    Summarized,
}

public sealed record TimberCsvHeaders(
    string ElementId,
    string ElementType,
    string Material,
    string WidthMm,
    string HeightMm,
    string CuttingLengthMm,
    string Quantity,
    string TotalLengthM,
    string VolumeM3,
    string Note);

public sealed class TimberCsvLocalization
{
    public TimberCsvLocalization(
        TimberCsvHeaders headers,
        Func<TimberElementType, string?, string> elementTypeDisplay,
        Func<string, string> materialDisplay)
    {
        Headers = headers ?? throw new ArgumentNullException(nameof(headers));
        ElementTypeDisplay = elementTypeDisplay ??
            throw new ArgumentNullException(nameof(elementTypeDisplay));
        MaterialDisplay = materialDisplay ??
            throw new ArgumentNullException(nameof(materialDisplay));
    }

    public TimberCsvHeaders Headers { get; }
    public Func<TimberElementType, string?, string> ElementTypeDisplay { get; }
    public Func<string, string> MaterialDisplay { get; }
}

public sealed record TimberCsvDocument(
    TimberCsvExportMode Mode,
    int SourceElementCount,
    int RowCount,
    string Content)
{
    public byte[] ToUtf8WithBom()
    {
        var encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(Content);
        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }
}
