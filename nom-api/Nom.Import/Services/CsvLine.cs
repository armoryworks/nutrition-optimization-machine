using System.Collections.Generic;
using System.Text;

namespace Nom.Import.Services
{
    /// <summary>
    /// Minimal RFC-4180 field splitter for the FDC bulk CSVs (every field is quoted; "" escapes
    /// a quote). Line-oriented so the multi-GB branded files can be streamed rather than buffered.
    /// </summary>
    internal static class CsvLine
    {
        public static string[] Split(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(ch);
                }
                else
                {
                    if (ch == '"') inQuotes = true;
                    else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(ch);
                }
            }
            fields.Add(sb.ToString());
            return fields.ToArray();
        }
    }
}
