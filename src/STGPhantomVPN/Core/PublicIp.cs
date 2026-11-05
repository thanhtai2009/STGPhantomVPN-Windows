using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace STGPhantomVPN.Core
{
    public enum IpFamily { V4, V6 }

    /// <summary>A public address as seen by an echo service, with whatever location data the service returned.</summary>
    public sealed class IpInfo
    {
        public string Ip { get; set; }
        public IpFamily Family { get; set; }
        /// <summary>ISO 3166-1 alpha-2, upper case; null when unknown.</summary>
        public string CountryCode { get; set; }
        public string Country { get; set; }
        public string City { get; set; }
        public string Isp { get; set; }
        public DateTime FetchedUtc { get; set; }

        public bool HasLocation => !string.IsNullOrEmpty(CountryCode) || !string.IsNullOrEmpty(City) || !string.IsNullOrEmpty(Isp);

        /// <summary>"City, Country · ISP" with the parts that are known.</summary>
        public string Details
        {
            get
            {
                var place = PublicIp.JoinNonEmpty(", ", City, Country ?? CountryCode);
                return PublicIp.JoinNonEmpty(" · ", place, Isp);
            }
        }
    }

    /// <summary>What the public addresses say about the tunnel.</summary>
    public sealed class IpAssessment
    {
        /// <summary>The IPv4 seen while connected equals the address seen before connecting.</summary>
        public bool V4NotTunneled { get; set; }
        /// <summary>A public IPv6 address is reachable while connected; VPNGate carries no IPv6, so it bypasses the tunnel.</summary>
        public bool V6Leak { get; set; }
        /// <summary>The IPv4 seen while connected is the VPN server's address.</summary>
        public bool MatchesServer { get; set; }
        public bool HasProblem => V4NotTunneled || V6Leak;
    }

    public static class PublicIp
    {
        /// <summary>Echo services that answer only over IPv4 or report the address the request came from; tried in order.</summary>
        public static readonly string[] V4Endpoints =
        {
            "https://ipinfo.io/json",
            "https://api.ipify.org?format=json",
            "https://ifconfig.co/json",
        };

        /// <summary>IPv6-only echo services (no IPv4 address record), then a dual-stack one whose answer is kept only if it is IPv6.</summary>
        public static readonly string[] V6Endpoints =
        {
            "https://api6.ipify.org?format=json",
            "https://v6.ipinfo.io/json",
            "https://ifconfig.co/json",
        };

        /// <summary>Location lookups for an address that came from a service without location data.</summary>
        public static string[] LocationEndpoints(string ip) => new[]
        {
            "https://ipinfo.io/" + ip + "/json",
            "https://ifconfig.co/json?ip=" + Uri.EscapeDataString(ip),
        };

        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

        /// <summary>Canonical text of a valid address of the family, else null.</summary>
        public static string Normalize(string ip, IpFamily family)
        {
            if (string.IsNullOrWhiteSpace(ip)) return null;
            var t = ip.Trim();
            if (!IPAddress.TryParse(t, out var a)) return null;
            if (family == IpFamily.V4)
            {
                if (a.AddressFamily != AddressFamily.InterNetwork || t.Split('.').Length != 4) return null;
                return a.ToString();
            }
            if (a.AddressFamily != AddressFamily.InterNetworkV6 || a.IsIPv4MappedToIPv6) return null;
            return a.ToString();
        }

        public static bool SameIp(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            return IPAddress.TryParse(a.Trim(), out var x) && IPAddress.TryParse(b.Trim(), out var y) && x.Equals(y);
        }

        /// <summary>
        /// Reads an echo-service answer: ipify {"ip"}, ipinfo {"ip","city","country":"JP","org":"AS123 Name"},
        /// ifconfig.co {"ip","country","country_iso","city","asn_org"}. Returns null when the address is missing or not of the family.
        /// </summary>
        public static IpInfo Parse(string json, IpFamily family)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var ip = Normalize(Field(json, "ip"), family);
            if (ip == null) return null;

            var iso = Field(json, "country_iso");
            var country = Field(json, "country");
            if (string.IsNullOrEmpty(iso) && country != null && Regex.IsMatch(country, "^[A-Za-z]{2}$"))
            {
                iso = country;
                country = null;
            }
            var isp = Field(json, "asn_org") ?? StripAsn(Field(json, "org")) ?? Field(json, "isp");
            return new IpInfo
            {
                Ip = ip,
                Family = family,
                CountryCode = string.IsNullOrEmpty(iso) ? null : iso.Trim().ToUpperInvariant(),
                Country = Empty(country),
                City = Empty(Field(json, "city")),
                Isp = Empty(isp),
                FetchedUtc = DateTime.UtcNow,
            };
        }

        /// <summary>Copies location data from a lookup about the same address.</summary>
        public static IpInfo Merge(IpInfo address, IpInfo location)
        {
            if (address == null) return location;
            if (location == null || !SameIp(address.Ip, location.Ip)) return address;
            address.CountryCode = address.CountryCode ?? location.CountryCode;
            address.Country = address.Country ?? location.Country;
            address.City = address.City ?? location.City;
            address.Isp = address.Isp ?? location.Isp;
            return address;
        }

        public static IpAssessment Assess(IpInfo realV4, IpInfo vpnV4, IpInfo vpnV6, string serverIp)
        {
            return new IpAssessment
            {
                V4NotTunneled = vpnV4 != null && realV4 != null && SameIp(vpnV4.Ip, realV4.Ip),
                V6Leak = vpnV6 != null,
                MatchesServer = vpnV4 != null && SameIp(vpnV4.Ip, serverIp),
            };
        }

        /// <summary>Value of a string field in a flat JSON object (unescaped); null when absent or not a string.</summary>
        public static string Field(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success) return null;
            return Unescape(m.Groups[1].Value);
        }

        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var b = new StringBuilder(s.Length);
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { b.Append(c); continue; }
                var n = s[++i];
                switch (n)
                {
                    case 'n': b.Append('\n'); break;
                    case 't': b.Append('\t'); break;
                    case 'r': b.Append('\r'); break;
                    case 'b': b.Append('\b'); break;
                    case 'f': b.Append('\f'); break;
                    case 'u':
                        if (i + 4 < s.Length && int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                        {
                            b.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: b.Append(n); break;
                }
            }
            return b.ToString();
        }

        static string StripAsn(string org)
        {
            if (string.IsNullOrWhiteSpace(org)) return null;
            var m = Regex.Match(org.Trim(), "^AS\\d+\\s+(.+)$");
            return m.Success ? m.Groups[1].Value : org.Trim();
        }

        static string Empty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        internal static string JoinNonEmpty(string separator, params string[] parts)
        {
            var kept = new List<string>();
            foreach (var p in parts) if (!string.IsNullOrWhiteSpace(p)) kept.Add(p.Trim());
            return string.Join(separator, kept);
        }

        /// <summary>
        /// Asks the echo services in order and returns the first answer of the family, with location data filled
        /// in from a second lookup when the first service had none. Null when offline or nothing answered.
        /// </summary>
        public static async Task<IpInfo> FetchAsync(IpFamily family, CancellationToken ct)
        {
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = true })
            using (var http = new HttpClient(handler) { Timeout = RequestTimeout })
            {
                IpInfo found = null;
                foreach (var url in family == IpFamily.V4 ? V4Endpoints : V6Endpoints)
                {
                    found = Parse(await GetAsync(http, url, ct).ConfigureAwait(false), family);
                    if (found != null) break;
                }
                if (found == null || found.HasLocation && found.CountryCode != null) return found;
                foreach (var url in LocationEndpoints(found.Ip))
                {
                    var loc = Parse(await GetAsync(http, url, ct).ConfigureAwait(false), family);
                    if (loc != null && loc.HasLocation) return Merge(found, loc);
                }
                return found;
            }
        }

        static async Task<string> GetAsync(HttpClient http, string url, CancellationToken ct)
        {
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.TryAddWithoutValidation("User-Agent", "STGPhantomVPN/" + AppVersion.Current + " (Windows)");
                    req.Headers.TryAddWithoutValidation("Accept", "application/json");
                    using (var resp = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        return bytes.Length > 64 * 1024 ? null : Encoding.UTF8.GetString(bytes);
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
            catch (HttpRequestException) { return null; }
            catch (WebException) { return null; }
            catch (IOException) { return null; }
        }
    }
}
