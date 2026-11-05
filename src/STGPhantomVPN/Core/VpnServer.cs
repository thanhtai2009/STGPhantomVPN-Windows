using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace STGPhantomVPN.Core
{
    /// <summary>One row of the VPNGate "iphone" CSV API.</summary>
    public sealed class VpnServer
    {
        public string HostName { get; set; } = "";
        public string Ip { get; set; } = "";
        public int Score { get; set; }
        public int Ping { get; set; }
        public long Speed { get; set; }
        public string CountryLong { get; set; } = "";
        public string CountryShort { get; set; } = "";
        public int NumVpnSessions { get; set; }
        public string Operator { get; set; } = "";
        public string OpenVpnConfigBase64 { get; set; } = "";

        public string Key => HostName + "|" + Ip;
        public string DisplayName => string.IsNullOrWhiteSpace(HostName) ? Ip : HostName;
        public string CountryDisplay => string.IsNullOrWhiteSpace(CountryLong) ? CountryShort : CountryLong;
        public string CountryCode => string.IsNullOrWhiteSpace(CountryShort) ? "??" : CountryShort.ToUpperInvariant();
        public bool IsVietnam =>
            string.Equals(CountryShort, "VN", StringComparison.OrdinalIgnoreCase) ||
            CountryLong.IndexOf("Viet", StringComparison.OrdinalIgnoreCase) >= 0;

        public string PingText => Ping > 0 ? Ping + " ms" : "—";
        public string SpeedText => Formatters.FormatLinkSpeed(Speed);

        /// <summary>Decodes the embedded OpenVPN profile; null when it is not a plausible client config.</summary>
        public string DecodeOpenVpnConfig()
        {
            try
            {
                var raw = Encoding.UTF8.GetString(Convert.FromBase64String(OpenVpnConfigBase64.Trim()));
                var looksValid = raw.IndexOf("remote ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 raw.IndexOf("<ca>", StringComparison.OrdinalIgnoreCase) >= 0;
                return looksValid ? raw : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        static readonly Regex TcpProto = new Regex(@"(?im)^\s*proto\s+tcp", RegexOptions.Compiled);

        /// <summary>TCP profiles (usually port 443) pass restrictive networks more often than UDP.</summary>
        public bool PrefersTcp()
        {
            var raw = DecodeOpenVpnConfig();
            return raw != null && TcpProto.IsMatch(raw);
        }

        /// <summary>Parses one CSV row; returns null for headers, comments and unusable rows.</summary>
        public static VpnServer FromCsvLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line[0] == '*' || line[0] == '#' ||
                line.StartsWith("HostName", StringComparison.Ordinal))
                return null;
            var parts = line.Split(',');
            if (parts.Length < 15) return null;

            string configB64;
            string op = parts[12].Trim();
            if (parts.Length == 15)
            {
                configB64 = parts[14].Trim();
            }
            else
            {
                var lastIdx = -1;
                for (var i = parts.Length - 1; i >= 14; i--)
                {
                    if (LooksLikeBase64Config(parts[i].Trim())) { lastIdx = i; break; }
                }
                configB64 = lastIdx >= 14 ? parts[lastIdx].Trim() : string.Join(",", parts.Skip(14)).Trim();
            }
            if (!LooksLikeBase64Config(configB64)) return null;

            return new VpnServer
            {
                HostName = parts[0].Trim(),
                Ip = parts[1].Trim(),
                Score = ParseInt(parts[2]),
                Ping = ParseInt(parts[3]),
                Speed = ParseLong(parts[4]),
                CountryLong = parts[5].Trim(),
                CountryShort = parts[6].Trim(),
                NumVpnSessions = ParseInt(parts[7]),
                Operator = op,
                OpenVpnConfigBase64 = configB64,
            };
        }

        /// <summary>Parses a whole CSV body and keeps the best-scoring row per host/IP.</summary>
        public static List<VpnServer> ParseCsv(string body)
        {
            var best = new Dictionary<string, VpnServer>();
            var order = new List<string>();
            if (string.IsNullOrEmpty(body)) return new List<VpnServer>();
            foreach (var rawLine in body.Split('\n'))
            {
                var s = FromCsvLine(rawLine.TrimEnd('\r'));
                if (s == null) continue;
                if (best.TryGetValue(s.Key, out var prev))
                {
                    if (s.Score > prev.Score) best[s.Key] = s;
                }
                else
                {
                    best[s.Key] = s;
                    order.Add(s.Key);
                }
            }
            return order.Select(k => best[k]).ToList();
        }

        static bool LooksLikeBase64Config(string b64)
        {
            if (b64 == null || b64.Length < 64) return false;
            var sample = b64.Length <= 256 ? b64 : b64.Substring(0, 128) + b64.Substring(b64.Length - 128);
            foreach (var ch in sample)
            {
                var ok = char.IsLetterOrDigit(ch) || ch == '+' || ch == '/' || ch == '=';
                if (!ok) return false;
            }
            return true;
        }

        static int ParseInt(string s) => int.TryParse(s.Trim(), out var v) ? v : 0;
        static long ParseLong(string s) => long.TryParse(s.Trim(), out var v) ? v : 0L;
    }

    public enum ServerSort { Score, Ping, Speed, Country }

    /// <summary>Sorting and default-pick rules mirrored from the Android app.</summary>
    public static class ServerPicker
    {
        /// <summary>Server cards per row for the list width: one on the compact window, more when maximized.</summary>
        public static int ColumnsFor(double listWidth) => listWidth >= 1500 ? 3 : listWidth >= 980 ? 2 : 1;

        /// <summary>Splits the list into rows of <paramref name="columns"/> cards, keeping the order (row by row).</summary>
        public static List<List<VpnServer>> Rows(IReadOnlyList<VpnServer> list, int columns)
        {
            var rows = new List<List<VpnServer>>();
            if (list == null) return rows;
            columns = Math.Max(1, columns);
            for (var i = 0; i < list.Count; i += columns)
                rows.Add(list.Skip(i).Take(columns).ToList());
            return rows;
        }

        public static List<VpnServer> FilterAndSort(IEnumerable<VpnServer> servers, string query, ServerSort sort)
        {
            var q = (query ?? "").Trim().ToLowerInvariant();
            var list = servers;
            if (q.Length > 0)
            {
                list = list.Where(s =>
                    s.CountryLong.ToLowerInvariant().Contains(q) ||
                    s.CountryShort.ToLowerInvariant().Contains(q) ||
                    s.HostName.ToLowerInvariant().Contains(q) ||
                    s.Ip.Contains(q) ||
                    s.Operator.ToLowerInvariant().Contains(q));
            }
            IEnumerable<VpnServer> sorted;
            switch (sort)
            {
                case ServerSort.Ping: sorted = list.OrderBy(s => s.Ping <= 0).ThenBy(s => s.Ping); break;
                case ServerSort.Speed: sorted = list.OrderByDescending(s => s.Speed); break;
                case ServerSort.Country: sorted = list.OrderBy(s => s.CountryLong, StringComparer.OrdinalIgnoreCase); break;
                default: sorted = list.OrderByDescending(s => s.Score); break;
            }
            // Vietnam servers stay on top; OrderBy is stable so the chosen sort is kept inside each group.
            return sorted.OrderBy(s => s.IsVietnam ? 0 : 1).ToList();
        }

        /// <summary>TCP profiles with a reported ping first, then the highest score.</summary>
        public static VpnServer PickDefault(IList<VpnServer> list)
        {
            if (list == null || list.Count == 0) return null;
            var tcp = list.Where(s => s.PrefersTcp()).ToList();
            var pool = tcp.Count > 0 ? tcp : list.ToList();
            return pool.Where(s => s.Ping > 0).OrderBy(s => s.Ping).FirstOrDefault()
                   ?? pool.OrderByDescending(s => s.Score).FirstOrDefault();
        }

        /// <summary>Next candidate after a connect timeout, skipping every server that already timed out.</summary>
        public static VpnServer PickNext(IList<VpnServer> list, ISet<string> excludedKeys)
        {
            var pool = list.Where(s => !excludedKeys.Contains(s.Key)).ToList();
            if (pool.Count == 0) return null;
            return pool.Where(s => s.Ping > 0)
                       .OrderByDescending(s => s.Score).ThenBy(s => s.Ping)
                       .FirstOrDefault(s => s.PrefersTcp())
                   ?? PickDefault(pool);
        }
    }
}
