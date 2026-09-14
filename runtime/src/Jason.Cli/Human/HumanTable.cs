using System.Text;

namespace Jason.Cli.Human;

/// <summary>
/// The fixed-width table behind every <c>--human</c> listing: columns left-aligned and two spaces apart, the
/// header underlined, an absent cell shown as a dash. Machine-readable output is the JSON response, so this
/// only has to be readable.
/// </summary>
public sealed class HumanTable
{
    private const string Gap = "  ";
    private const string Absent = "-";

    private readonly string[] _headers;
    private readonly List<string[]> _rows = [];

    public HumanTable(params string[] headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        if (headers.Length == 0)
        {
            throw new ArgumentException("A table needs at least one column.", nameof(headers));
        }

        _headers = headers;
    }

    public HumanTable Row(params string?[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Length != _headers.Length)
        {
            throw new ArgumentException($"The table has {_headers.Length} columns but this row has {cells.Length} cells.", nameof(cells));
        }

        _rows.Add([.. cells.Select(cell => string.IsNullOrEmpty(cell) ? Absent : cell)]);
        return this;
    }

    public string Render()
    {
        var widths = new int[_headers.Length];
        for (var column = 0; column < widths.Length; column++)
        {
            var index = column;
            widths[column] = Math.Max(_headers[column].Length, _rows.Count == 0 ? 0 : _rows.Max(row => row[index].Length));
        }

        var lines = new List<string>(_rows.Count + 2)
        {
            Line(widths, _headers),
            Line(widths, [.. widths.Select(width => new string('-', width))]),
        };
        lines.AddRange(_rows.Select(row => Line(widths, row)));

        return string.Join(Environment.NewLine, lines);
    }

    private static string Line(int[] widths, IReadOnlyList<string> cells)
    {
        var builder = new StringBuilder();
        for (var column = 0; column < cells.Count; column++)
        {
            if (column > 0)
            {
                builder.Append(Gap);
            }

            builder.Append(cells[column].PadRight(widths[column]));
        }

        return builder.ToString().TrimEnd();
    }
}
