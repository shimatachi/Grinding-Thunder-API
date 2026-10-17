using System.IO.Compression;
using System.Text;
using System.Xml;

namespace GrindingThunder.Api.Infrastructure.Services;

/// <summary>
/// Parsed tabular row containing 1-based source row index and canonical column-value mappings.
/// </summary>
public record TabularRow(int RowNumber, Dictionary<string, string> Values);

/// <summary>
/// Result of parsing a tabular file (CSV or XLSX).
/// </summary>
public class TabularParseResult
{
    public List<string> Headers { get; set; } = new();
    public List<TabularRow> Rows { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Lightweight, zero-dependency parser for CSV and XLSX files.
/// Implements RFC 4180 compliant CSV parsing and OpenXML worksheet parsing using BCL ZipArchive & XmlReader.
/// </summary>
public static class TabularDataParser
{
    public static TabularParseResult Parse(Stream stream, string fileName)
    {
        var result = new TabularParseResult();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        List<List<string>> rawRows;
        try
        {
            rawRows = extension switch
            {
                ".xlsx" => ParseXlsx(stream),
                ".csv" or ".tsv" or ".txt" => ParseCsv(stream),
                _ => throw new NotSupportedException(
                    $"Unsupported file format '{extension}'. Please upload a CSV (.csv) or Excel (.xlsx) file.")
            };
        }
        catch (NotSupportedException ex)
        {
            result.Errors.Add(ex.Message);
            return result;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Failed to parse file: {ex.Message}");
            return result;
        }

        if (rawRows.Count == 0)
        {
            result.Errors.Add("The uploaded file contains no data rows.");
            return result;
        }

        // Row 1 is header
        var rawHeaders = rawRows[0];
        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < rawHeaders.Count; i++)
        {
            var rawHeader = rawHeaders[i];
            if (string.IsNullOrWhiteSpace(rawHeader)) continue;

            var normalized = NormalizeHeader(rawHeader);
            if (!headerMap.ContainsKey(normalized))
            {
                headerMap[normalized] = i;
                result.Headers.Add(normalized);
            }
        }

        // Validate required canonical headers
        var requiredHeaders = new[] { "VehicleName", "RankNumber", "RpCost", "SlCost", "TreeColumn", "TreeRow" };
        var missingRequired = requiredHeaders.Where(h => !headerMap.ContainsKey(h)).ToList();

        if (missingRequired.Count > 0)
        {
            result.Errors.Add(
                $"Missing required column(s): {string.Join(", ", missingRequired)}. " +
                "Expected canonical columns: VehicleName, RankNumber, RpCost, SlCost, TreeColumn, TreeRow, " +
                "[AcquisitionType], [FolderParentVehicleName], [PrerequisiteVehicleNames], [RequiredVehiclesUnlocked], [ImageUrl], [WikiUrl].");
            return result;
        }

        // Parse data rows (row index 2+ for display)
        for (int r = 1; r < rawRows.Count; r++)
        {
            var rawRow = rawRows[r];
            if (rawRow.All(string.IsNullOrWhiteSpace))
            {
                continue; // skip blank rows
            }

            var rowValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (header, colIdx) in headerMap)
            {
                var val = colIdx < rawRow.Count ? rawRow[colIdx].Trim() : string.Empty;
                rowValues[header] = val;
            }

            result.Rows.Add(new TabularRow(r + 1, rowValues));
        }

        if (result.Rows.Count == 0)
        {
            result.Errors.Add("The uploaded file contains no data rows.");
        }

        return result;
    }

    public static string NormalizeHeader(string header)
    {
        var clean = header.Trim().ToLowerInvariant()
            .Replace(" ", "")
            .Replace("_", "")
            .Replace("-", "");

        return clean switch
        {
            "vehiclename" or "vehicle" or "name" or "vehiclefullname" => "VehicleName",
            "ranknumber" or "rank" => "RankNumber",
            "rpcost" or "rp" or "researchcost" or "researchcost(rp)" or "researchpoints" => "RpCost",
            "slcost" or "sl" or "purchasecost" or "purchasecost(sl)" or "silverlions" => "SlCost",
            "treecolumn" or "column" or "col" => "TreeColumn",
            "treerow" or "row" => "TreeRow",
            "acquisitiontype" or "acquisition" or "status" or "vehiclestatus" or "availability" => "AcquisitionType",
            "folderparentvehiclename" or "folderparent" or "folderparentvehicle" => "FolderParentVehicleName",
            "prerequisitevehiclenames" or "prerequisites" or "prerequisite" or "prerequisitevehicle" or "prerequisitevehicles" => "PrerequisiteVehicleNames",
            "requiredvehiclesunlocked" or "rankquota" or "rankrequiredvehicles" or "rankunlockrequirement" => "RequiredVehiclesUnlocked",
            "imageurl" or "image" or "mainrenderimage" or "renderimage" => "ImageUrl",
            "wikiurl" or "wiki" or "directwikipageurl" or "wikipageurl" or "url" => "WikiUrl",
            _ => header.Trim()
        };
    }

    private static List<List<string>> ParseCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = reader.ReadToEnd();

        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<List<string>>();
        }

        // Detect delimiter: inspect first non-empty line
        char delimiter = ',';
        using var lineReader = new StringReader(content);
        string? firstLine = null;
        while ((firstLine = lineReader.ReadLine()) != null)
        {
            if (!string.IsNullOrWhiteSpace(firstLine))
            {
                break;
            }
        }

        if (firstLine != null)
        {
            int semicolons = firstLine.Count(c => c == ';');
            int commas = firstLine.Count(c => c == ',');
            int tabs = firstLine.Count(c => c == '\t');

            if (semicolons > commas && semicolons > tabs)
            {
                delimiter = ';';
            }
            else if (tabs > commas && tabs > semicolons)
            {
                delimiter = '\t';
            }
        }

        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var currentCell = new StringBuilder();
        bool inQuotes = false;
        int i = 0;

        while (i < content.Length)
        {
            char c = content[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < content.Length && content[i + 1] == '"')
                {
                    currentCell.Append('"');
                    i += 2;
                    continue;
                }
                inQuotes = !inQuotes;
                i++;
                continue;
            }

            if (!inQuotes && c == delimiter)
            {
                currentRow.Add(currentCell.ToString().Trim());
                currentCell.Clear();
                i++;
                continue;
            }

            if (!inQuotes && (c == '\r' || c == '\n'))
            {
                if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                {
                    i++;
                }

                currentRow.Add(currentCell.ToString().Trim());
                currentCell.Clear();

                if (currentRow.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                {
                    rows.Add(currentRow);
                }
                currentRow = new List<string>();
                i++;
                continue;
            }

            currentCell.Append(c);
            i++;
        }

        // Add last cell/row if stream ended without newline
        if (currentCell.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentCell.ToString().Trim());
            if (currentRow.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            {
                rows.Add(currentRow);
            }
        }

        return rows;
    }

    private static List<List<string>> ParseXlsx(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        // 1. Load shared strings table if present
        var sharedStrings = new List<string>();
        var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry != null)
        {
            using var ssStream = sharedStringsEntry.Open();
            using var xml = XmlReader.Create(ssStream, new XmlReaderSettings { IgnoreWhitespace = true });

            while (xml.Read())
            {
                if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "si")
                {
                    var sb = new StringBuilder();
                    var sub = xml.ReadSubtree();
                    while (sub.Read())
                    {
                        if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "t")
                        {
                            sb.Append(sub.ReadElementContentAsString());
                        }
                    }
                    sharedStrings.Add(sb.ToString());
                }
            }
        }

        // 2. Find the primary worksheet
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
                      ?? archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

        if (sheetEntry == null)
        {
            throw new InvalidOperationException("No worksheet found in XLSX archive.");
        }

        var rows = new List<List<string>>();
        using var sheetStream = sheetEntry.Open();
        using var sheetXml = XmlReader.Create(sheetStream, new XmlReaderSettings { IgnoreWhitespace = true });

        while (sheetXml.Read())
        {
            if (sheetXml.NodeType == XmlNodeType.Element && sheetXml.LocalName == "row")
            {
                var rowCells = new SortedDictionary<int, string>();
                var rowSub = sheetXml.ReadSubtree();

                while (rowSub.Read())
                {
                    if (rowSub.NodeType == XmlNodeType.Element && rowSub.LocalName == "c")
                    {
                        var cellRef = rowSub.GetAttribute("r");
                        var cellType = rowSub.GetAttribute("t");
                        int colIndex = cellRef != null ? ColumnNameToIndex(cellRef) : rowCells.Count;

                        var cellSub = rowSub.ReadSubtree();
                        string cellValue = string.Empty;

                        while (cellSub.Read())
                        {
                            if (cellSub.NodeType == XmlNodeType.Element)
                            {
                                if (cellSub.LocalName == "v")
                                {
                                    var val = cellSub.ReadElementContentAsString();
                                    if (cellType == "s" && int.TryParse(val, out var sIndex) && sIndex >= 0 && sIndex < sharedStrings.Count)
                                    {
                                        cellValue = sharedStrings[sIndex];
                                    }
                                    else
                                    {
                                        cellValue = val;
                                    }
                                }
                                else if (cellSub.LocalName == "t" && cellType == "inlineStr")
                                {
                                    cellValue = cellSub.ReadElementContentAsString();
                                }
                            }
                        }

                        rowCells[colIndex] = cellValue.Trim();
                    }
                }

                if (rowCells.Count > 0)
                {
                    int maxCol = rowCells.Keys.Max();
                    var rowList = new List<string>(maxCol + 1);
                    for (int c = 0; c <= maxCol; c++)
                    {
                        rowList.Add(rowCells.TryGetValue(c, out var val) ? val : string.Empty);
                    }

                    if (rowList.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                    {
                        rows.Add(rowList);
                    }
                }
            }
        }

        return rows;
    }

    private static int ColumnNameToIndex(string cellRef)
    {
        int colIndex = 0;
        foreach (char c in cellRef)
        {
            if (char.IsLetter(c))
            {
                colIndex = colIndex * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
            }
            else
            {
                break;
            }
        }
        return Math.Max(0, colIndex - 1);
    }
}

