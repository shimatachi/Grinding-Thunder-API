using System.IO.Compression;
using System.Text;
using GrindingThunder.Api.Infrastructure.Services;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class TabularDataParserTests
{
    [Fact]
    public void Parse_StandardCsv_ReturnsHeadersAndRows()
    {
        var csv = """
            VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow,AcquisitionType,FolderParentVehicleName,PrerequisiteVehicleNames,RequiredVehiclesUnlocked
            Spitfire Mk Ia,1,10000,50000,1,1,Researchable,,
            Spitfire Mk IIa,2,20000,100000,1,2,Researchable,,Spitfire Mk Ia,1
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var result = TabularDataParser.Parse(stream, "tree.csv");

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Rows.Count);

        var row1 = result.Rows[0];
        Assert.Equal(2, row1.RowNumber);
        Assert.Equal("Spitfire Mk Ia", row1.Values["VehicleName"]);
        Assert.Equal("1", row1.Values["RankNumber"]);
        Assert.Equal("10000", row1.Values["RpCost"]);
        Assert.Equal("50000", row1.Values["SlCost"]);
        Assert.Equal("1", row1.Values["TreeColumn"]);
        Assert.Equal("1", row1.Values["TreeRow"]);
        Assert.Equal("Researchable", row1.Values["AcquisitionType"]);
        Assert.Equal("", row1.Values["FolderParentVehicleName"]);
        Assert.Equal("", row1.Values["PrerequisiteVehicleNames"]);

        var row2 = result.Rows[1];
        Assert.Equal(3, row2.RowNumber);
        Assert.Equal("Spitfire Mk IIa", row2.Values["VehicleName"]);
        Assert.Equal("2", row2.Values["RankNumber"]);
        Assert.Equal("Spitfire Mk Ia", row2.Values["PrerequisiteVehicleNames"]);
        Assert.Equal("1", row2.Values["RequiredVehiclesUnlocked"]);
    }

    [Fact]
    public void Parse_CsvWithQuotedCommasAndEscapedQuotes_ParsesProperly()
    {
        var csv = """
            VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow,PrerequisiteVehicleNames
            "Bf 109 ""Friedrich"", Late",3,33000,120000,2,3,"Bf 109 F-1; Bf 109 F-2"
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var result = TabularDataParser.Parse(stream, "planes.csv");

        Assert.Empty(result.Errors);
        Assert.Single(result.Rows);
        Assert.Equal("Bf 109 \"Friedrich\", Late", result.Rows[0].Values["VehicleName"]);
        Assert.Equal("Bf 109 F-1; Bf 109 F-2", result.Rows[0].Values["PrerequisiteVehicleNames"]);
    }

    [Fact]
    public void Parse_SemicolonDelimitedCsv_AutoDetectsDelimiter()
    {
        var csv = """
            VehicleName;RankNumber;RpCost;SlCost;TreeColumn;TreeRow
            P-51D-5;3;40000;150000;2;1
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var result = TabularDataParser.Parse(stream, "europe.csv");

        Assert.Empty(result.Errors);
        Assert.Single(result.Rows);
        Assert.Equal("P-51D-5", result.Rows[0].Values["VehicleName"]);
        Assert.Equal("3", result.Rows[0].Values["RankNumber"]);
    }

    [Fact]
    public void Parse_TabDelimitedTsv_AutoDetectsDelimiter()
    {
        var tsv = "VehicleName\tRankNumber\tRpCost\tSlCost\tTreeColumn\tTreeRow\nP-51D-5\t3\t40000\t150000\t2\t1";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(tsv));
        var result = TabularDataParser.Parse(stream, "export.tsv");

        Assert.Empty(result.Errors);
        Assert.Single(result.Rows);
        Assert.Equal("P-51D-5", result.Rows[0].Values["VehicleName"]);
    }

    [Fact]
    public void Parse_FlexibleHeaderAliases_NormalizesToCanonicalKeys()
    {
        var csv = """
            Vehicle Name,Rank,RP Cost,SL Cost,Column,Row,Availability,Folder Parent
            Yak-3,3,25000,90000,1,1,Premium,Yak-1
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var result = TabularDataParser.Parse(stream, "import.csv");

        Assert.Empty(result.Errors);
        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal("Yak-3", row.Values["VehicleName"]);
        Assert.Equal("3", row.Values["RankNumber"]);
        Assert.Equal("25000", row.Values["RpCost"]);
        Assert.Equal("90000", row.Values["SlCost"]);
        Assert.Equal("1", row.Values["TreeColumn"]);
        Assert.Equal("1", row.Values["TreeRow"]);
        Assert.Equal("Premium", row.Values["AcquisitionType"]);
        Assert.Equal("Yak-1", row.Values["FolderParentVehicleName"]);
    }

    [Fact]
    public void Parse_MissingRequiredColumn_ReturnsError()
    {
        var csv = """
            VehicleName,RankNumber,RpCost,SlCost
            Yak-3,3,25000,90000
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var result = TabularDataParser.Parse(stream, "invalid.csv");

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("Missing required column") && e.Contains("TreeColumn") && e.Contains("TreeRow"));
    }

    [Fact]
    public void Parse_EmptyFile_ReturnsError()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(""));
        var result = TabularDataParser.Parse(stream, "empty.csv");

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("contains no data rows"));
    }

    [Fact]
    public void Parse_UnsupportedFormat_ReturnsError()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("some binary content"));
        var result = TabularDataParser.Parse(stream, "document.pdf");

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("Unsupported file format"));
    }

    [Fact]
    public void Parse_ValidXlsx_ParsesWorksheetAndSharedStrings()
    {
        using var stream = CreateSampleXlsx(
            headers: new[] { "VehicleName", "RankNumber", "RpCost", "SlCost", "TreeColumn", "TreeRow" },
            rows: new[] {
                new[] { "Bf 109 E-3", "2", "14000", "60000", "1", "2" },
                new[] { "Fw 190 A-1", "3", "33000", "130000", "2", "1" }
            });

        var result = TabularDataParser.Parse(stream, "test.xlsx");

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("Bf 109 E-3", result.Rows[0].Values["VehicleName"]);
        Assert.Equal("2", result.Rows[0].Values["RankNumber"]);
        Assert.Equal("Fw 190 A-1", result.Rows[1].Values["VehicleName"]);
        Assert.Equal("3", result.Rows[1].Values["RankNumber"]);
    }

    private static MemoryStream CreateSampleXlsx(string[] headers, string[][] rows)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Collect unique strings for sharedStrings.xml
            var uniqueStrings = new List<string>();
            var stringMap = new Dictionary<string, int>();

            int GetStringIndex(string s)
            {
                if (!stringMap.TryGetValue(s, out var idx))
                {
                    idx = uniqueStrings.Count;
                    stringMap[s] = idx;
                    uniqueStrings.Add(s);
                }
                return idx;
            }

            // SharedStrings.xml
            var ssEntry = archive.CreateEntry("xl/sharedStrings.xml");
            using (var ssStream = ssEntry.Open())
            using (var writer = new StreamWriter(ssStream, Encoding.UTF8))
            {
                // Prepopulate with all text
                foreach (var h in headers) GetStringIndex(h);
                foreach (var r in rows)
                {
                    foreach (var c in r) GetStringIndex(c);
                }

                writer.Write($"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="{uniqueStrings.Count}" uniqueCount="{uniqueStrings.Count}">""");
                foreach (var s in uniqueStrings)
                {
                    writer.Write($"<si><t>{System.Security.SecurityElement.Escape(s)}</t></si>");
                }
                writer.Write("</sst>");
            }

            // Worksheets/sheet1.xml
            var sheetEntry = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using (var sStream = sheetEntry.Open())
            using (var writer = new StreamWriter(sStream, Encoding.UTF8))
            {
                writer.Write("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");

                // Header row (r=1)
                writer.Write("""<row r="1">""");
                for (int c = 0; c < headers.Length; c++)
                {
                    string colLetter = ((char)('A' + c)).ToString();
                    int sIdx = GetStringIndex(headers[c]);
                    writer.Write($"""<c r="{colLetter}1" t="s"><v>{sIdx}</v></c>""");
                }
                writer.Write("</row>");

                // Data rows (r=2+)
                for (int r = 0; r < rows.Length; r++)
                {
                    int rowNum = r + 2;
                    writer.Write($"""<row r="{rowNum}">""");
                    for (int c = 0; c < rows[r].Length; c++)
                    {
                        string colLetter = ((char)('A' + c)).ToString();
                        int sIdx = GetStringIndex(rows[r][c]);
                        writer.Write($"""<c r="{colLetter}{rowNum}" t="s"><v>{sIdx}</v></c>""");
                    }
                    writer.Write("</row>");
                }

                writer.Write("</sheetData></worksheet>");
            }
        }

        stream.Position = 0;
        return stream;
    }
}

