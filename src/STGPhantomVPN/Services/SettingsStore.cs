using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace STGPhantomVPN.Services
{
    public enum ThemeMode { System, Light, Dark }

    [DataContract]
    public sealed class AppSettings
    {
        [DataMember] public ThemeMode Theme { get; set; } = ThemeMode.System;
        [DataMember] public string Language { get; set; } = "vi";
        [DataMember] public bool AllowLocalLan { get; set; } = true;
        [DataMember] public bool BlockOutsideDns { get; set; }
        [DataMember] public string SelectedHost { get; set; }
        [DataMember] public string SelectedIp { get; set; }
        /// <summary>The user chose "Not now" in the TAP-Windows6 dialog; it is then shown again only on connect.</summary>
        [DataMember] public bool TapPromptDeclined { get; set; }
        /// <summary>
        /// Close quits instead of hiding to the tray. Stored inverted so that settings files written before
        /// 1.0.6, which lack the member and deserialize it as false, get the default "hide to tray".
        /// </summary>
        [DataMember] public bool CloseQuitsApp { get; set; }
        /// <summary>The one-time "still running in the tray" notification was shown.</summary>
        [DataMember] public bool TrayHintShown { get; set; }
        /// <summary>DNS while connected: empty for the server's, a <see cref="Core.PublicDns"/> provider id, or "custom".</summary>
        [DataMember] public string DnsProvider { get; set; }
        [DataMember] public string CustomDns1 { get; set; }
        [DataMember] public string CustomDns2 { get; set; }
        /// <summary>
        /// The IPv6 leak block is off. Stored inverted so that settings files written before 1.0.9, which lack the
        /// member, get the default "block IPv6 while connected".
        /// </summary>
        [DataMember] public bool Ipv6BlockOff { get; set; }
    }

    public static class SettingsStore
    {
        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                {
                    using (var fs = File.OpenRead(AppPaths.SettingsFile))
                    {
                        var s = (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(fs);
                        if (s != null)
                        {
                            if (string.IsNullOrEmpty(s.Language)) s.Language = "vi";
                            return s;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Reading settings failed; using defaults", ex);
            }
            return new AppSettings();
        }

        public static void Save(AppSettings s)
        {
            try
            {
                var tmp = AppPaths.SettingsFile + ".tmp";
                using (var fs = File.Create(tmp))
                    new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(fs, s);
                if (File.Exists(AppPaths.SettingsFile)) File.Delete(AppPaths.SettingsFile);
                File.Move(tmp, AppPaths.SettingsFile);
            }
            catch (Exception ex)
            {
                Log.Error("Saving settings failed", ex);
            }
        }
    }
}
