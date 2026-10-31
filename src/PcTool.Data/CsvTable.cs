namespace PcTool.Data;

/// <summary>Minimal reader for the bundled tier tables: '#' comment lines, a header row, comma-separated values without quoting.</summary>
internal sealed class CsvTable
{
    private readonly Dictionary<string, int> _columns;

    public IReadOnlyList<string[]> Rows { get; }

    private CsvTable(Dictionary<string, int> columns, List<string[]> rows)
    {
        _columns = columns;
        Rows = rows;
    }

    public static CsvTable Parse(string text)
    {
        var lines = text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();
        if (lines.Count == 0) return new CsvTable([], []);

        var header = lines[0].Split(',');
        var columns = header.Select((h, i) => (h.Trim(), i)).ToDictionary(x => x.Item1, x => x.i, StringComparer.OrdinalIgnoreCase);
        var rows = lines.Skip(1).Select(l => l.Split(',')).ToList();
        return new CsvTable(columns, rows);
    }

    public string Get(string[] row, string column) =>
        _columns.TryGetValue(column, out var i) && i < row.Length ? row[i].Trim() : "";

    public int Int(string[] row, string column) => int.TryParse(Get(row, column), out var v) ? v : 0;

    public double Double(string[] row, string column, double fallback = 0) =>
        double.TryParse(Get(row, column), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public string[] List(string[] row, string column) =>
        Get(row, column).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
