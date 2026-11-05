using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace STGPhantomVPN.Core
{
    /// <summary>
    /// Downloads the VPNGate server list over HTTPS only. There is deliberately no plain-HTTP
    /// fallback and redirects are not followed: on failure the caller keeps the disk cache or the
    /// bundled list instead (security audit 2026-10-30, VPN Connector item 1).
    /// </summary>
    public sealed class VpnGateClient
    {
        public const string ApiUrl = "https://www.vpngate.net/api/iphone/";
        public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);

        readonly string _cachePath;
        readonly string _bundledPath;

        public VpnGateClient(string cachePath, string bundledPath)
        {
            _cachePath = cachePath;
            _bundledPath = bundledPath;
        }

        public DateTime? CacheTimeUtc =>
            File.Exists(_cachePath) && new FileInfo(_cachePath).Length > 0 ? File.GetLastWriteTimeUtc(_cachePath) : (DateTime?)null;

        public bool IsCacheStale => CacheTimeUtc == null || DateTime.UtcNow - CacheTimeUtc.Value > StaleAfter;

        /// <summary>Disk cache when present, otherwise the list shipped with the app.</summary>
        public List<VpnServer> LoadInitial()
        {
            var cached = TryParseFile(_cachePath);
            return cached.Count > 0 ? cached : TryParseFile(_bundledPath);
        }

        public async Task<List<VpnServer>> FetchAsync(CancellationToken ct)
        {
            var uri = new Uri(ApiUrl);
            if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("VPNGate URL must be HTTPS");

            using (var handler = new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) })
            using (var req = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                req.Headers.TryAddWithoutValidation("User-Agent", "STGPhantomVPN/" + AppVersion.Current + " (Windows)");
                req.Headers.TryAddWithoutValidation("Accept", "text/plain,*/*");
                using (var resp = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false))
                {
                    if ((int)resp.StatusCode >= 300)
                        throw new HttpRequestException("HTTP " + (int)resp.StatusCode);
                    var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    var body = Encoding.UTF8.GetString(bytes);
                    var list = VpnServer.ParseCsv(body);
                    if (list.Count == 0) throw new InvalidDataException("No servers returned from VPNGate");
                    WriteCache(body);
                    return list;
                }
            }
        }

        void WriteCache(string body)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cachePath));
                var tmp = _cachePath + ".tmp";
                File.WriteAllText(tmp, body, new UTF8Encoding(false));
                if (File.Exists(_cachePath)) File.Delete(_cachePath);
                File.Move(tmp, _cachePath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static List<VpnServer> TryParseFile(string path)
        {
            try
            {
                return File.Exists(path) ? VpnServer.ParseCsv(File.ReadAllText(path, Encoding.UTF8)) : new List<VpnServer>();
            }
            catch (IOException) { return new List<VpnServer>(); }
            catch (UnauthorizedAccessException) { return new List<VpnServer>(); }
        }
    }

    public static class AppVersion
    {
        public const string Current = "1.0.11";
        public const string AppName = "STG Phantom VPN";
    }
}
