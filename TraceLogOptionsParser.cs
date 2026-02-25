using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZXTL
{

    public enum TraceLogOptionKeyword
    {
        TABBED,
        ENDTAB,
        NOPAIRS,
        SLICE,
        ONLYJUMPS,
        HEX,
        VIEWMEM
    }

    public static class TraceLogOptionKeys
    {
        public const string PREFIXED = "PREFIXED";
        public const string M = "M";
        public const string S = "S";
    }

    public sealed class TraceLogOptions
    {
        // Store only explicitly defined flags/params
        public HashSet<TraceLogOptionKeyword> Flags { get; } = new();
        public Dictionary<string, string> Parameters { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        // Defaults (choose your spec defaults here)
        public bool IsTabbed => Flags.Contains(TraceLogOptionKeyword.TABBED);
        public bool IsEndTab => Flags.Contains(TraceLogOptionKeyword.ENDTAB);
        public bool NoPairs => Flags.Contains(TraceLogOptionKeyword.NOPAIRS);
        public bool Slice => Flags.Contains(TraceLogOptionKeyword.SLICE);
        public bool OnlyJumps => Flags.Contains(TraceLogOptionKeyword.ONLYJUMPS);
        public bool Hex => Flags.Contains(TraceLogOptionKeyword.HEX);
        public bool ViewMem => Flags.Contains(TraceLogOptionKeyword.VIEWMEM);

        // Parameter defaults
        public string? HexPrefix =>
            GetParamOrDefaultNullable(TraceLogOptionKeys.PREFIXED); // default: not provided

        public string Model =>
            GetParamOrDefault(TraceLogOptionKeys.M, "48");        // example default: 48

        public string? SnapshotFile =>
            GetParamOrDefaultNullable(TraceLogOptionKeys.S);      // default: not provided

        public string GetParamOrDefault(string key, string defaultValue)
            => Parameters.TryGetValue(key, out var v) ? v : defaultValue;

        public string? GetParamOrDefaultNullable(string key)
            => Parameters.TryGetValue(key, out var v) ? v : null;
    }
    public static class TraceLogOptionsParser
    {
        public static TraceLogOptions ParseDefineLine(string line)
        {
            var opts = new TraceLogOptions();
            foreach (var token in Tokenize(line))
            {
                if (TryParseKeyValue(token, out var key, out var value))
                {
                    opts.Parameters[key] = value;
                    continue;
                }

                if (Enum.TryParse<TraceLogOptionKeyword>(token, ignoreCase: true, out var kw))
                {
                    opts.Flags.Add(kw);
                }
                // else: unknown token -> ignore or record diagnostics
            }
            return opts;
        }

        private static bool TryParseKeyValue(string token, out string key, out string value)
        {
            int eq = token.IndexOf('=');
            if (eq <= 0)
            {
                key = "";
                value = "";
                return false;
            }

            key = token.Substring(0, eq).Trim();
            value = token.Substring(eq + 1).Trim();

            // Strip surrounding quotes if present: S="match day.sna"
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value.Substring(1, value.Length - 2);

            return key.Length > 0;
        }

        private static IEnumerable<string> Tokenize(string line)
        {
            // Splits by whitespace, but keeps quoted substrings together.
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    sb.Append(c);
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0)
                    {
                        yield return sb.ToString();
                        sb.Clear();
                    }
                    continue;
                }

                sb.Append(c);
            }

            if (sb.Length > 0)
                yield return sb.ToString();
        }
    }
}
