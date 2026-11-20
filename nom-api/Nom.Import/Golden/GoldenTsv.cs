using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nom.Import.Golden
{
    /// <summary>One row of a golden set, its cells by column name and the line it was read from.</summary>
    public sealed class GoldenRow
    {
        private readonly Dictionary<string, string> _cells;

        public GoldenRow(int lineNumber, Dictionary<string, string> cells)
        {
            LineNumber = lineNumber;
            _cells = cells;
        }

        public int LineNumber { get; }

        public string Id => this["id"];

        /// <summary>The cell as written; an empty string when the column is blank or missing.</summary>
        public string this[string column]
        {
            get => _cells.TryGetValue(column, out var value) ? value : string.Empty;
            set => _cells[column] = value;
        }

        /// <summary>The cell, or null for <see cref="GoldenTsv.NoAnswer"/>.</summary>
        public string? Value(string column) => this[column] == GoldenTsv.NoAnswer ? null : this[column];
    }

    /// <summary>
    /// The tab-separated format every golden set uses: UTF-8, one header row, lines starting with
    /// <c>#</c> skipped, <see cref="NoAnswer"/> for "no answer" and <see cref="NotJudged"/> for a
    /// cell nobody has judged. A file is written back with its header, comment lines and row order
    /// kept, so a change to it reads as a plain diff.
    /// </summary>
    public sealed class GoldenTsv
    {
        public const string NoAnswer = "-";
        public const string NotJudged = "*";

        private readonly List<string> _lines;
        private readonly Dictionary<int, GoldenRow> _byLine;

        private GoldenTsv(string path, IReadOnlyList<string> header, List<string> lines, List<GoldenRow> rows)
        {
            Path = path;
            Header = header;
            _lines = lines;
            Rows = rows;
            _byLine = rows.ToDictionary(r => r.LineNumber);
        }

        public string Path { get; }
        public IReadOnlyList<string> Header { get; }
        public IReadOnlyList<GoldenRow> Rows { get; }

        public static GoldenTsv Read(string path)
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8).ToList();
            var headerAt = lines.FindIndex(l => l.Length > 0 && !l.StartsWith('#'));
            if (headerAt < 0) throw new InvalidDataException($"{path}: no header row");
            var header = lines[headerAt].Split('\t');
            var rows = new List<GoldenRow>();
            for (var i = headerAt + 1; i < lines.Count; i++)
            {
                if (lines[i].Length == 0 || lines[i].StartsWith('#')) continue;
                var cells = lines[i].Split('\t');
                if (cells.Length > header.Length)
                    throw new InvalidDataException($"{path}:{i + 1}: {cells.Length} cells for {header.Length} columns");
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var c = 0; c < header.Length; c++) map[header[c]] = c < cells.Length ? cells[c] : string.Empty;
                rows.Add(new GoldenRow(i, map));
            }
            return new GoldenTsv(path, header, lines, rows);
        }

        public void Write(string path)
        {
            var output = _lines.Select((line, i) => _byLine.TryGetValue(i, out var row)
                ? string.Join('\t', Header.Select(h => row[h]))
                : line);
            File.WriteAllLines(path, output, new UTF8Encoding(false));
        }
    }
}
