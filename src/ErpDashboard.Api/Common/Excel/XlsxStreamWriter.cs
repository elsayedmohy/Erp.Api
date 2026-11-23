using System.Globalization;
using System.IO.Compression;
using System.Xml;

namespace ErpDashboard.Api.Common.Excel;

/// Writes a one-sheet .xlsx row by row, with memory that stays flat however many rows there are.
///
/// Why not a library: ClosedXML builds the whole workbook in memory, and the Open XML SDK's package
/// buffers parts in memory on .NET. Here the zip is opened in Create mode, which streams each entry
/// straight to the output, and the sheet is written with an XmlWriter, one row at a time.
///
/// Cells: string (inline, so no shared-strings table to hold in memory), decimal/int/long (number),
/// DateOnly (a real Excel date), null (empty). Money columns get a #,##0.00 format.
public sealed class XlsxStreamWriter : IAsyncDisposable
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // Indexes into cellXfs in Styles below.
    private const string DateStyle = "1";
    private const string MoneyStyle = "2";
    private const string HeaderStyle = "3";

    private static readonly DateOnly ExcelEpoch = new(1899, 12, 30);

    private readonly ZipArchive _zip;
    private readonly Stream _sheetStream;
    private readonly XmlWriter _sheet;
    private int _row;
    private bool _finished;

    private XlsxStreamWriter(ZipArchive zip, Stream sheetStream, XmlWriter sheet)
    {
        _zip = zip;
        _sheetStream = sheetStream;
        _sheet = sheet;
    }

    /// columnWidths are in Excel characters, one per column.
    public static async Task<XlsxStreamWriter> CreateAsync(
        Stream output, string sheetName, IReadOnlyList<double> columnWidths, CancellationToken ct)
    {
        var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        // In Create mode only one entry can be open at a time: the small fixed parts go first,
        // the sheet last, and it stays open while rows are written.
        await WriteEntryAsync(zip, "[Content_Types].xml", ContentTypes, ct);
        await WriteEntryAsync(zip, "_rels/.rels", RootRels, ct);
        await WriteEntryAsync(zip, "xl/workbook.xml", Workbook(sheetName), ct);
        await WriteEntryAsync(zip, "xl/_rels/workbook.xml.rels", WorkbookRels, ct);
        await WriteEntryAsync(zip, "xl/styles.xml", Styles, ct);

        var sheetStream = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest).Open();
        var sheet = XmlWriter.Create(sheetStream, new XmlWriterSettings { Async = true, Encoding = new System.Text.UTF8Encoding(false) });

        await sheet.WriteStartDocumentAsync(standalone: true);
        await sheet.WriteStartElementAsync(null, "worksheet", Main);

        // Keep the header row in view while scrolling.
        await sheet.WriteStartElementAsync(null, "sheetViews", Main);
        await sheet.WriteStartElementAsync(null, "sheetView", Main);
        await sheet.WriteAttributeStringAsync(null, "workbookViewId", null, "0");
        await sheet.WriteStartElementAsync(null, "pane", Main);
        await sheet.WriteAttributeStringAsync(null, "ySplit", null, "1");
        await sheet.WriteAttributeStringAsync(null, "topLeftCell", null, "A2");
        await sheet.WriteAttributeStringAsync(null, "activePane", null, "bottomLeft");
        await sheet.WriteAttributeStringAsync(null, "state", null, "frozen");
        await sheet.WriteEndElementAsync();   // pane
        await sheet.WriteEndElementAsync();   // sheetView
        await sheet.WriteEndElementAsync();   // sheetViews

        await sheet.WriteStartElementAsync(null, "cols", Main);
        for (var i = 0; i < columnWidths.Count; i++)
        {
            var index = (i + 1).ToString(CultureInfo.InvariantCulture);
            await sheet.WriteStartElementAsync(null, "col", Main);
            await sheet.WriteAttributeStringAsync(null, "min", null, index);
            await sheet.WriteAttributeStringAsync(null, "max", null, index);
            await sheet.WriteAttributeStringAsync(null, "width", null, columnWidths[i].ToString(CultureInfo.InvariantCulture));
            await sheet.WriteAttributeStringAsync(null, "customWidth", null, "1");
            await sheet.WriteEndElementAsync();
        }
        await sheet.WriteEndElementAsync();   // cols

        await sheet.WriteStartElementAsync(null, "sheetData", Main);
        return new XlsxStreamWriter(zip, sheetStream, sheet);
    }

    public Task WriteHeaderAsync(IReadOnlyList<string> titles) =>
        WriteRowAsync(titles.Cast<object?>().ToList(), header: true);

    /// decimal values are written as money (#,##0.00); use int or long for plain counts.
    public Task WriteRowAsync(IReadOnlyList<object?> cells) => WriteRowAsync(cells, header: false);

    private async Task WriteRowAsync(IReadOnlyList<object?> cells, bool header)
    {
        _row++;
        await _sheet.WriteStartElementAsync(null, "row", Main);
        await _sheet.WriteAttributeStringAsync(null, "r", null, _row.ToString(CultureInfo.InvariantCulture));

        foreach (var value in cells)
        {
            if (value is null)
            {
                await _sheet.WriteStartElementAsync(null, "c", Main);
                await _sheet.WriteEndElementAsync();
                continue;
            }

            await _sheet.WriteStartElementAsync(null, "c", Main);
            switch (value)
            {
                case string text:
                    await _sheet.WriteAttributeStringAsync(null, "t", null, "inlineStr");
                    if (header) await _sheet.WriteAttributeStringAsync(null, "s", null, HeaderStyle);
                    await _sheet.WriteStartElementAsync(null, "is", Main);
                    await _sheet.WriteStartElementAsync(null, "t", Main);
                    await _sheet.WriteAttributeStringAsync("xml", "space", null, "preserve");
                    await _sheet.WriteStringAsync(XmlSafe(text));
                    await _sheet.WriteEndElementAsync();   // t
                    await _sheet.WriteEndElementAsync();   // is
                    break;
                case DateOnly date:
                    await _sheet.WriteAttributeStringAsync(null, "s", null, DateStyle);
                    await WriteValueAsync((date.DayNumber - ExcelEpoch.DayNumber).ToString(CultureInfo.InvariantCulture));
                    break;
                case decimal money:
                    await _sheet.WriteAttributeStringAsync(null, "s", null, MoneyStyle);
                    await WriteValueAsync(money.ToString(CultureInfo.InvariantCulture));
                    break;
                case int or long:
                    await WriteValueAsync(Convert.ToString(value, CultureInfo.InvariantCulture)!);
                    break;
                default:
                    throw new ArgumentException($"XlsxStreamWriter cannot write a {value.GetType().Name}.");
            }
            await _sheet.WriteEndElementAsync();   // c
        }

        await _sheet.WriteEndElementAsync();   // row
    }

    private async Task WriteValueAsync(string value)
    {
        await _sheet.WriteStartElementAsync(null, "v", Main);
        await _sheet.WriteStringAsync(value);
        await _sheet.WriteEndElementAsync();
    }

    /// Closes the sheet and the zip. Without it the file is not a valid workbook.
    public async Task FinishAsync()
    {
        if (_finished) return;
        _finished = true;

        await _sheet.WriteEndElementAsync();   // sheetData
        await _sheet.WriteEndElementAsync();   // worksheet
        await _sheet.WriteEndDocumentAsync();
        await _sheet.FlushAsync();
        _sheet.Dispose();
        await _sheetStream.DisposeAsync();
        _zip.Dispose();                        // writes the zip's central directory
    }

    public async ValueTask DisposeAsync()
    {
        if (_finished) return;
        // Abandoned (an error part-way): release everything; the caller throws the partial file away.
        _finished = true;
        _sheet.Dispose();
        await _sheetStream.DisposeAsync();
        _zip.Dispose();
    }

    // XML 1.0 cannot carry most control characters; a name pasted from somewhere else may hold one.
    private static string XmlSafe(string text) =>
        text.Any(c => !XmlConvert.IsXmlChar(c))
            ? new string(text.Where(XmlConvert.IsXmlChar).ToArray())
            : text;

    private static async Task WriteEntryAsync(ZipArchive zip, string name, string content, CancellationToken ct)
    {
        await using var stream = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
        await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        await writer.WriteAsync(content.AsMemory(), ct);
    }

    private static string Workbook(string sheetName)
    {
        // Excel limits: 31 characters, none of : \ / ? * [ ]
        var safe = new string(sheetName.Where(c => !":\\/?*[]".Contains(c)).Take(31).ToArray());
        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="{Main}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="{System.Security.SecurityElement.Escape(safe)}" sheetId="1" r:id="rId1"/></sheets></workbook>
            """;
    }

    private const string ContentTypes = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>
        """;

    private const string RootRels = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
        """;

    private const string WorkbookRels = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
        """;

    // cellXfs: 0 default, 1 date (yyyy-mm-dd), 2 money (#,##0.00), 3 bold header.
    private const string Styles = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="yyyy-mm-dd"/></numFmts><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="4"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="4" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
        """;
}
