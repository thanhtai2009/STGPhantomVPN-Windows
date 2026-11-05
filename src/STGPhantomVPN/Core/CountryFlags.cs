using System;
using System.Collections.Generic;

namespace STGPhantomVPN.Core
{
    /// <summary>
    /// Maps a VPNGate CountryShort code (ISO 3166-1 alpha-2) to a bundled flag image. WPF cannot render
    /// regional-indicator emoji, so the flags ship as PNG resources rendered from flag-icons 7.6.0 (MIT).
    /// </summary>
    public static class CountryFlags
    {
        /// <summary>Resource folder of the flag PNGs inside the app assembly.</summary>
        public const string ResourceFolder = "Assets/Flags/";

        static readonly HashSet<string> Codes = new HashSet<string>(StringComparer.Ordinal)
        {
            "ad", "ae", "af", "ag", "ai", "al", "am", "ao", "aq", "ar", "as", "at", "au", "aw", "ax", "az",
            "ba", "bb", "bd", "be", "bf", "bg", "bh", "bi", "bj", "bl", "bm", "bn", "bo", "bq", "br", "bs",
            "bt", "bv", "bw", "by", "bz", "ca", "cc", "cd", "cf", "cg", "ch", "ci", "ck", "cl", "cm", "cn",
            "co", "cp", "cr", "cu", "cv", "cw", "cx", "cy", "cz", "de", "dg", "dj", "dk", "dm", "do", "dz",
            "ec", "ee", "eg", "eh", "er", "es", "et", "eu", "fi", "fj", "fk", "fm", "fo", "fr", "ga", "gb",
            "gd", "ge", "gf", "gg", "gh", "gi", "gl", "gm", "gn", "gp", "gq", "gr", "gs", "gt", "gu", "gw",
            "gy", "hk", "hm", "hn", "hr", "ht", "hu", "ic", "id", "ie", "il", "im", "in", "io", "iq", "ir",
            "is", "it", "je", "jm", "jo", "jp", "ke", "kg", "kh", "ki", "km", "kn", "kp", "kr", "kw", "ky",
            "kz", "la", "lb", "lc", "li", "lk", "lr", "ls", "lt", "lu", "lv", "ly", "ma", "mc", "md", "me",
            "mf", "mg", "mh", "mk", "ml", "mm", "mn", "mo", "mp", "mq", "mr", "ms", "mt", "mu", "mv", "mw",
            "mx", "my", "mz", "na", "nc", "ne", "nf", "ng", "ni", "nl", "no", "np", "nr", "nu", "nz", "om",
            "pa", "pc", "pe", "pf", "pg", "ph", "pk", "pl", "pm", "pn", "pr", "ps", "pt", "pw", "py", "qa",
            "re", "ro", "rs", "ru", "rw", "sa", "sb", "sc", "sd", "se", "sg", "sh", "si", "sj", "sk", "sl",
            "sm", "sn", "so", "sr", "ss", "st", "sv", "sx", "sy", "sz", "tc", "td", "tf", "tg", "th", "tj",
            "tk", "tl", "tm", "tn", "to", "tr", "tt", "tv", "tw", "tz", "ua", "ug", "um", "un", "us", "uy",
            "uz", "va", "vc", "ve", "vg", "vi", "vn", "vu", "wf", "ws", "xk", "xx", "ye", "yt", "za", "zm",
            "zw"
        };

        /// <summary>Codes that have a flag image in <see cref="ResourceFolder"/>, lower case.</summary>
        public static IReadOnlyCollection<string> Available => Codes;

        /// <summary>Trimmed, lower-case two-letter code, or null when the value is not two ASCII letters. "UK" becomes "gb".</summary>
        public static string Normalize(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            var c = code.Trim().ToLowerInvariant();
            if (c.Length != 2 || c[0] < 'a' || c[0] > 'z' || c[1] < 'a' || c[1] > 'z') return null;
            return c == "uk" ? "gb" : c;
        }

        /// <summary>Resource path of the flag for <paramref name="code"/>, or null when no flag is bundled (the UI then shows a globe).</summary>
        public static string ResourcePath(string code)
        {
            var c = Normalize(code);
            return c != null && Codes.Contains(c) ? ResourceFolder + c + ".png" : null;
        }
    }
}
