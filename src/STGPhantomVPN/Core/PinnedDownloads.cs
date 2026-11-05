using System;
using System.IO;
using System.Security.Cryptography;

namespace STGPhantomVPN.Core
{
    /// <summary>An official installer with a SHA-256 pinned at build time.</summary>
    public sealed class PinnedDownload
    {
        public string FileName { get; set; }
        public string Url { get; set; }
        public string Sha256 { get; set; }
    }

    /// <summary>
    /// Official installers the app may fetch on the user's PC. Hashes were computed from the files on
    /// build.openvpn.net on 2026-10-30; the OpenVPN MSIs also passed GnuPG
    /// verification with the OpenVPN security key (F554 A368 7412 CFFE BDEF E0A3 12F5 F7B4 2F2B 01E7).
    /// </summary>
    public static class PinnedDownloads
    {
        public const string OpenVpnVersion = "2.6.23-I002";

        /// <summary>ProductVersion and ProductCode of the pinned OpenVPN MSIs (Property table; the ProductCode is shared by all three).</summary>
        public const string OpenVpnProductVersion = "2.6.2302";
        public const string OpenVpnProductCode = "{8FB3B4AA-BC74-410D-8630-4DB839982DCB}";

        /// <summary>UpgradeCode of the OpenVPN 2.6 MSIs per architecture (Property table of the pinned MSIs).</summary>
        public static string OpenVpnUpgradeCode(string arch)
        {
            switch (arch)
            {
                case "arm64": return "{1E8C4DDC-9E93-4AE2-9495-DF86821EAA3A}";
                case "x86": return "{1195A47B-A37A-4055-9D34-B7A691F7E97B}";
                default: return "{461BDF86-D389-4471-BF36-99806B64C127}";
            }
        }

        /// <summary>SHA-256 of openvpn.exe shipped in the pinned MSIs (amd64 checked from the extracted 2.6.23-I002 MSI).</summary>
        public static readonly System.Collections.Generic.HashSet<string> OpenVpnExeHashes = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "100c2da3996ddb0da232487cb0bbade3f8289d812370b8efddac7a29f2b45401", // 2.6.23-I002 amd64
            "e5bd038526a69c8a08403d19f6ee17135f6afa3bb870c3c616cb128c8ff850f0", // 2.6.23-I002 arm64
            "3db4c42bc4b5aeaf11e49ebfc54325f491ddc5f1328ffe80d85f0574be2b9085", // 2.6.23-I002 x86
        };

        public static PinnedDownload OpenVpn(string arch)
        {
            switch (arch)
            {
                case "arm64": return Make("https://build.openvpn.net/downloads/releases/", "OpenVPN-2.6.23-I002-arm64.msi", "1c5e21a537fb920f251de9f842f2582d744901d45a1cc5f1e911eade87b2d63f");
                case "x86": return Make("https://build.openvpn.net/downloads/releases/", "OpenVPN-2.6.23-I002-x86.msi", "bbf4c4b76f6255ccb2fbec0203fe72e6c6269dfb7cb075436da69faff90c6101");
                default: return Make("https://build.openvpn.net/downloads/releases/", "OpenVPN-2.6.23-I002-amd64.msi", "31809e97debe44c12be06ed892c2a43e55b67b31cb852827b7ec8f2d6c0b8dc6");
            }
        }

        static PinnedDownload Make(string baseUrl, string file, string sha) =>
            new PinnedDownload { FileName = file, Url = baseUrl + file, Sha256 = sha };

        public static string Sha256OfFile(string path)
        {
            using (var fs = File.OpenRead(path)) return Sha256OfStream(fs);
        }

        public static string Sha256OfStream(Stream s)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        public static bool Verify(string path, PinnedDownload d) =>
            File.Exists(path) && string.Equals(Sha256OfFile(path), d.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
