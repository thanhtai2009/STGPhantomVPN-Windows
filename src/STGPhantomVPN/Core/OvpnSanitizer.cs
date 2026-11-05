using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace STGPhantomVPN.Core
{
    /// <summary>Result of sanitizing an untrusted OpenVPN profile.</summary>
    public sealed class SanitizedOvpn
    {
        public string Config { get; set; } = "";
        public List<string> RemovedDirectives { get; } = new List<string>();
        public bool HasRemote { get; set; }
        public bool NeedsAuthUserPass { get; set; }
        public string Cipher { get; set; }
        /// <summary>First "proto" value of the profile (udp, tcp, tcp-client, ...), lower case; null if absent.</summary>
        public string Proto { get; set; }
        /// <summary>Host of the first "remote" line.</summary>
        public string RemoteHost { get; set; }
        public bool IsUdp => Proto == null || Proto.StartsWith("udp", StringComparison.Ordinal);
        public bool IsUsable => HasRemote && Config.Length > 0;
    }

    /// <summary>
    /// Allow-list filter for VPNGate .ovpn profiles. VPNGate servers are run by volunteers and the
    /// list is third-party data, so only directives a plain client needs are kept. Anything that can
    /// run commands, load code, open files or change the management/log setup is removed.
    /// </summary>
    public static class OvpnSanitizer
    {
        /// <summary>Directives kept as-is (arguments validated where they could reference files).</summary>
        static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "client", "tls-client", "pull", "dev", "dev-type", "proto", "remote", "remote-random",
            "resolv-retry", "nobind", "persist-key", "persist-tun", "float",
            "cipher", "data-ciphers", "data-ciphers-fallback", "ncp-ciphers", "auth", "tls-cipher",
            "tls-ciphersuites", "tls-version-min", "tls-version-max", "key-direction",
            "remote-cert-tls", "remote-cert-ku", "remote-cert-eku", "verify-x509-name", "ns-cert-type",
            "reneg-sec", "reneg-bytes", "hand-window", "tran-window",
            "ping", "ping-restart", "ping-exit", "keepalive", "connect-retry", "connect-retry-max",
            "connect-timeout", "server-poll-timeout", "explicit-exit-notify",
            "sndbuf", "rcvbuf", "tun-mtu", "mssfix", "fragment", "comp-lzo", "compress",
            "verb", "mute", "mute-replay-warnings", "auth-nocache", "auth-retry", "redirect-gateway",
            "route-delay", "route-metric", "block-outside-dns",
            "auth-user-pass",
            // "allow-compression" is not accepted: OpenVPN's default (asym) avoids VORACLE-style leaks.
        };

        /// <summary>Inline blocks that carry key material only.</summary>
        static readonly HashSet<string> AllowedBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ca", "cert", "key", "tls-auth", "tls-crypt", "tls-crypt-v2", "extra-certs",
        };

        /// <summary>Directives treated as hostile; listed separately so the log can flag them.</summary>
        public static readonly HashSet<string> Dangerous = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "up", "down", "route-up", "route-pre-down", "ipchange", "client-connect", "client-disconnect",
            "learn-address", "auth-user-pass-verify", "tls-verify", "plugin", "script-security", "setenv",
            "setenv-safe", "management", "management-client", "management-hold", "management-query-passwords",
            "log", "log-append", "config", "cd", "chroot", "writepid", "status", "daemon", "service",
            "askpass", "tls-export-cert", "dev-node", "up-delay", "up-restart", "down-pre", "iproute",
            "route-pre-down", "x509-track", "echo", "pkcs11-providers", "engine", "providers",
            "http-proxy", "socks-proxy", "http-proxy-user-pass", "crl-verify", "capath", "dh",
            "secret", "replay-persist", "pull-filter", "route-nopull", "ifconfig", "route", "dhcp-option",
            "windows-driver", "ip-win32", "route-method", "tap-sleep", "register-dns",
        };

        static readonly Regex BlockOpen = new Regex(@"^<([A-Za-z0-9-]+)>$", RegexOptions.Compiled);
        static readonly Regex BlockClose = new Regex(@"^</([A-Za-z0-9-]+)>$", RegexOptions.Compiled);
        static readonly Regex SafeToken = new Regex(@"^[A-Za-z0-9._:\-+/=,@\[\]]+$", RegexOptions.Compiled);
        static readonly Regex AsciiName = new Regex(@"^[a-z0-9-]{1,40}$", RegexOptions.Compiled);
        static readonly Regex PemArmor = new Regex(@"^-----(BEGIN|END) [A-Za-z0-9 ]{1,40}-----$", RegexOptions.Compiled);

        /// <summary>OpenVPN reads config lines into a 256-byte buffer (OPTION_LINE_SIZE); longer lines are split.</summary>
        const int MaxLineLength = 250;

        /// <summary>Options whose value must not disable encryption or authentication.</summary>
        static readonly HashSet<string> CryptoOptions = new HashSet<string>(StringComparer.Ordinal)
        {
            "cipher", "data-ciphers", "data-ciphers-fallback", "ncp-ciphers", "auth",
        };

        public static SanitizedOvpn Sanitize(string raw)
        {
            var result = new SanitizedOvpn();
            if (string.IsNullOrWhiteSpace(raw)) return result;

            var sb = new StringBuilder();
            string openBlock = null;
            var keepBlock = false;
            var blockBody = new StringBuilder();
            string remoteProto = null;

            // A leading UTF-8 BOM would otherwise hide the first directive.
            if (raw[0] == '\uFEFF') raw = raw.Substring(1);

            foreach (var rawLine in raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = rawLine.Trim();

                if (openBlock != null)
                {
                    var close = BlockClose.Match(line);
                    if (close.Success && string.Equals(close.Groups[1].Value, openBlock, StringComparison.OrdinalIgnoreCase))
                    {
                        if (keepBlock)
                        {
                            sb.Append('<').Append(openBlock.ToLowerInvariant()).Append(">\n");
                            sb.Append(blockBody);
                            sb.Append("</").Append(openBlock.ToLowerInvariant()).Append(">\n");
                        }
                        openBlock = null;
                        blockBody.Clear();
                        continue;
                    }
                    // Key material is PEM/hex text only; reject anything that looks like nested directives.
                    if (keepBlock && line.Length > 0 && !IsKeyMaterialLine(line))
                    {
                        keepBlock = false;
                        result.RemovedDirectives.Add("<" + openBlock + "> (invalid content)");
                    }
                    // Comments are not needed inside key blocks and could carry a long line that OpenVPN splits.
                    if (keepBlock && line.Length > 0 && line[0] != '#') blockBody.Append(line).Append('\n');
                    continue;
                }

                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;

                var open = BlockOpen.Match(line);
                if (open.Success)
                {
                    openBlock = open.Groups[1].Value;
                    keepBlock = AllowedBlocks.Contains(openBlock);
                    if (!keepBlock) result.RemovedDirectives.Add("<" + openBlock + ">");
                    continue;
                }

                var tokens = Tokenize(line);
                if (tokens.Count == 0) continue;
                var name = (tokens[0].StartsWith("--", StringComparison.Ordinal) ? tokens[0].Substring(2) : tokens[0]).ToLowerInvariant();
                var args = tokens.Skip(1).ToList();

                // Emit only plain-ASCII names from the allow-list; never echo non-ASCII look-alikes.
                if (!AsciiName.IsMatch(name) || !Allowed.Contains(name))
                {
                    result.RemovedDirectives.Add(Printable(name));
                    continue;
                }
                if (name == "auth-user-pass")
                {
                    // Credentials are always answered by the app over the management interface; a file path from the profile is never used.
                    result.NeedsAuthUserPass = true;
                    continue;
                }
                if (!args.All(a => SafeToken.IsMatch(a)))
                {
                    result.RemovedDirectives.Add(name + " (unsafe argument)");
                    continue;
                }

                if (name.Length + args.Sum(a => a.Length + 1) > MaxLineLength)
                {
                    result.RemovedDirectives.Add(name + " (line too long)");
                    continue;
                }
                if (CryptoOptions.Contains(name) &&
                    args.Any(a => a.Split(':').Any(c => c.Equals("none", StringComparison.OrdinalIgnoreCase))))
                {
                    result.RemovedDirectives.Add(name + " (none)");
                    continue;
                }

                switch (name)
                {
                    case "remote":
                        if (args.Count == 0) { result.RemovedDirectives.Add("remote (empty)"); continue; }
                        if (!result.HasRemote)
                        {
                            result.RemoteHost = args[0];
                            if (args.Count > 2) remoteProto = args[2].ToLowerInvariant();
                        }
                        result.HasRemote = true;
                        break;
                    case "proto":
                        if (args.Count > 0 && result.Proto == null) result.Proto = args[0].ToLowerInvariant();
                        break;
                    case "dev":
                        if (args.Count != 1 || !(args[0].StartsWith("tun", StringComparison.OrdinalIgnoreCase) ||
                                                 args[0].StartsWith("tap", StringComparison.OrdinalIgnoreCase)))
                        {
                            result.RemovedDirectives.Add("dev (unsupported)");
                            continue;
                        }
                        break;
                    case "cipher":
                        if (args.Count > 0) result.Cipher = args[0];
                        break;
                    case "verb":
                        // Keep logs readable; very high verbosity can leak key material to the log file.
                        args = new List<string> { "3" };
                        break;
                }

                sb.Append(name);
                foreach (var a in args) sb.Append(' ').Append(a);
                sb.Append('\n');
            }

            if (openBlock != null) result.RemovedDirectives.Add("<" + openBlock + "> (unterminated)");
            if (result.Proto == null) result.Proto = remoteProto;
            result.Config = sb.ToString();
            return result;
        }

        static bool IsKeyMaterialLine(string line)
        {
            if (line[0] == '#') return true;            // dropped by the caller
            if (line.Length > MaxLineLength) return false;
            if (line.StartsWith("-----", StringComparison.Ordinal)) return PemArmor.IsMatch(line);
            foreach (var ch in line)
            {
                var ok = ch >= 'A' && ch <= 'Z' || ch >= 'a' && ch <= 'z' || ch >= '0' && ch <= '9' || ch == '+' || ch == '/' || ch == '=';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>Log-safe form of an untrusted directive name.</summary>
        static string Printable(string s)
        {
            var clean = new string(s.Where(c => c >= 0x20 && c < 0x7F).ToArray());
            return clean.Length > 40 ? clean.Substring(0, 40) + "…" : clean;
        }

        /// <summary>Whitespace tokenizer that also understands simple double-quoted arguments.</summary>
        static List<string> Tokenize(string line)
        {
            var tokens = new List<string>();
            var cur = new StringBuilder();
            var inQuote = false;
            foreach (var ch in line)
            {
                if (ch == '"') { inQuote = !inQuote; continue; }
                if (!inQuote && (ch == ' ' || ch == '\t'))
                {
                    if (cur.Length > 0) { tokens.Add(cur.ToString()); cur.Clear(); }
                    continue;
                }
                if (!inQuote && (ch == '#' || ch == ';') && cur.Length == 0) break;
                cur.Append(ch);
            }
            if (cur.Length > 0) tokens.Add(cur.ToString());
            return tokens;
        }
    }
}
