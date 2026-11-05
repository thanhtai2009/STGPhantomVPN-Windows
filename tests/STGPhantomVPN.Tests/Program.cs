using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Threading;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Tests
{
    /// <summary>Minimal self-contained test runner; exit code 0 means every check passed.</summary>
    public static class Program
    {
        static int _failed, _passed;

        static void Check(bool ok, string name)
        {
            if (ok) _passed++; else { _failed++; Console.WriteLine("FAIL: " + name); }
        }

        public static int Main(string[] args)
        {
            var root = FindRepoRoot();
            var bundled = Path.Combine(root, "src", "STGPhantomVPN", "Assets", "vpngate_fallback.csv");

            SanitizerRemovesDangerousDirectives();
            SanitizerKeepsNormalProfile();
            SanitizerBypassAttempts();
            BundledListParsesAndSanitizes(bundled);
            ResourceFilesMatch(root);
            Picker();
            NoPlainHttpInSources(root);
            TapDetection();
            TapRepairChoice();
            OpenVpnMsiProperties();
            OsGating();
            PrerequisiteChecks();
            ManifestDeclaresOldWindows(root);
            VersionIsConsistent(root);
            HelperPlacementRules();
            InstallerScript(root);
            WaitHandleProcessTracking();
            PollingProcessTracking();
            ServicePathAvoidsProcessHandles(root);
            ReleaseHasNoTestLabels(root);
            TapPromptRules();
            CountryFlagRules(root, bundled);
            WindowFitRules();
            PagesFitWithoutScrolling(root);
            ArchitectureRules(root);
            TrayRules(root);
            OpenVpnOnly(root);
            CompactWindow(root);
            BrandRules(root);
            DnsRules(root);
            TunnelRules(root);
            LogFileRules(root);
            Ipv6LeakRules(root);
            Ipv6FirewallRules(root);
            BuildDocsRules(root);
            PublicIpRules(root);
            LogCleanupRules(root);
            ResponsiveLayoutRules(root);
            FluentLookRules(root);
            TrayMenuRules(root);
            if (args.Contains("--live")) LiveVpnGate();
            var dump = Array.IndexOf(args, "--dump");
            if (dump >= 0 && dump + 1 < args.Length) DumpSanitized(bundled, args[dump + 1]);

            Console.WriteLine($"passed {_passed}, failed {_failed}");
            return _failed == 0 ? 0 : 1;
        }

        /// <summary>1.0.10: WinUI-style restyle in WPF (own Fluent theme, Mica on Windows 11, no Material Design).</summary>
        static void FluentLookRules(string root)
        {
            var src = Path.Combine(root, "src", "STGPhantomVPN");
            // Backdrop / chrome decisions by Windows build.
            Check(WindowLook.BackdropFor(7601) == Backdrop.Solid, "Windows 7 gets the solid background");
            Check(WindowLook.BackdropFor(9600) == Backdrop.Solid, "Windows 8.1 gets the solid background");
            Check(WindowLook.BackdropFor(19045) == Backdrop.Solid, "Windows 10 gets the solid background");
            Check(WindowLook.BackdropFor(22000) == Backdrop.MicaLegacy, "Windows 11 21H2 uses the legacy Mica attribute");
            Check(WindowLook.BackdropFor(22621) == Backdrop.Mica, "Windows 11 22H2 uses DWMWA_SYSTEMBACKDROP_TYPE");
            Check(WindowLook.BackdropFor(26100) == Backdrop.Mica, "Windows 11 24H2 uses DWMWA_SYSTEMBACKDROP_TYPE");
            Check(WindowLook.DarkModeAttribute(7601) == null && WindowLook.DarkModeAttribute(17134) == null, "no dark title attribute before 1809");
            Check(WindowLook.DarkModeAttribute(17763) == 19 && WindowLook.DarkModeAttribute(18985) == 20 && WindowLook.DarkModeAttribute(22621) == 20, "dark title attribute 19 / 20 by build");
            Check(!WindowLook.RoundedCorners(19045) && WindowLook.RoundedCorners(22000), "rounded corners from Windows 11 only");
            Check(WindowLook.DwmwaSystemBackdropType == 38 && WindowLook.DwmsbtMainWindow == 2 && WindowLook.DwmwaWindowCornerPreference == 33 && WindowLook.DwmwcpRound == 2 && WindowLook.DwmwaMicaEffect == 1029, "documented DWM attribute values");

            var backdrop = File.ReadAllText(Path.Combine(src, "Services", "WindowBackdrop.cs"));
            Check(backdrop.Contains("DwmSetWindowAttribute") && backdrop.Contains("DwmExtendFrameIntoClientArea"), "backdrop uses DWM");
            Check(backdrop.Contains("WindowLook.BackdropFor"), "backdrop chosen by Windows build");
            Check(backdrop.Contains("Fluent.WindowBg"), "solid fallback uses the theme window background");

            // No Material Design left anywhere in the shipped app.
            var csproj = File.ReadAllText(Path.Combine(src, "STGPhantomVPN.csproj"));
            Check(!csproj.Contains("MaterialDesign") && !csproj.Contains("Xaml.Behaviors"), "Material Design and Behaviors packages removed");
            var files = Directory.GetFiles(src, "*.*", SearchOption.AllDirectories)
                .Where(p => (p.EndsWith(".xaml") || p.EndsWith(".cs")) && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)).ToList();
            foreach (var f in files)
            {
                var t = File.ReadAllText(f);
                Check(!t.Contains("materialDesign") && !t.Contains("MaterialDesignThemes") && !Regex.IsMatch(t, @"\bmd:"), "no Material Design reference in " + Path.GetFileName(f));
            }
            var notices = File.ReadAllText(Path.Combine(root, "licenses", "THIRD-PARTY-NOTICES.txt"));
            Check(!notices.Contains("Material Design"), "notices no longer list Material Design");
            Check(!File.Exists(Path.Combine(root, "licenses", "MaterialDesignInXamlToolkit-LICENSE.txt")), "Material Design licence file removed");
            var iss = File.ReadAllText(Path.Combine(root, "installer", "STGPhantomVPN.iss"));
            foreach (var old in new[] { "MaterialDesignThemes.Wpf.dll", "MaterialDesignColors.dll", "Microsoft.Xaml.Behaviors.dll", "MaterialDesignInXamlToolkit-LICENSE.txt" })
                Check(Regex.IsMatch(iss, @"(?s)\[InstallDelete\].*" + Regex.Escape(old)), "upgrade removes " + old);

            // Theme dictionaries: same keys in light and dark, each brush with its .Color twin, no key used twice.
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            List<string> Keys(string file) => XDocument.Load(Path.Combine(src, "Themes", file)).Root.Elements().Select(e => (string)e.Attribute(x + "Key")).Where(k => k != null).ToList();
            var light = Keys("Colors.Light.xaml");
            var dark = Keys("Colors.Dark.xaml");
            Check(light.Count > 40, "light theme has the Fluent tokens");
            Check(light.OrderBy(k => k).SequenceEqual(dark.OrderBy(k => k)), "light and dark themes define the same keys");
            foreach (var k in light.Where(k => !k.EndsWith(".Color")))
                Check(light.Contains(k + ".Color"), "theme brush " + k + " has a Color twin");
            var fluent = Keys("Fluent.xaml");
            Check(fluent.Count == fluent.Distinct().Count(), "Fluent.xaml keys are unique");
            Check(!fluent.Intersect(light).Any(), "no style key reuses a colour key");
            foreach (var k in new[] { "Fluent.AccentButton", "Fluent.Button", "Fluent.ToggleSwitch", "Fluent.ComboBox", "Fluent.NavItem", "Fluent.CaptionButton", "Fluent.CloseCaptionButton", "Fluent.Title", "Fluent.Subtitle", "Fluent.Body", "Fluent.Caption" })
                Check(fluent.Contains(k), "Fluent style " + k + " exists");
            var fluentText = File.ReadAllText(Path.Combine(src, "Themes", "Fluent.xaml"));
            Check(fluentText.Contains("Segoe UI Variable Text, Segoe UI"), "Segoe UI Variable with Segoe UI fallback");
            var theme = File.ReadAllText(Path.Combine(src, "Services", "ThemeService.cs"));
            Check(theme.Contains("AppsUseLightTheme"), "System theme follows AppsUseLightTheme");

            // Pages use the Fluent building blocks.
            var main = File.ReadAllText(Path.Combine(src, "MainWindow.xaml"));
            var home = File.ReadAllText(Path.Combine(src, "Views", "HomeView.xaml"));
            var settings = File.ReadAllText(Path.Combine(src, "Views", "SettingsView.xaml"));
            var servers = File.ReadAllText(Path.Combine(src, "Views", "ServersView.xaml"));
            Check(main.Contains("MinimizeButton") && main.Contains("MaximizeButton") && main.Contains("CloseButton"), "own caption buttons");
            Check(main.Contains("Width=\"720\"") && main.Contains("Height=\"592\""), "window stays 720 x 592");
            Check(home.Contains("views:ProgressRing") && home.Contains("x:Name=\"ConnectRing\""), "Home shows a ProgressRing while connecting");
            Check(home.Contains("views:InfoBar") && Regex.IsMatch(home, "Severity=\"Error\""), "Home errors use an InfoBar");
            Check(Regex.Matches(home, "<views:SettingsExpander").Count >= 2, "Home stats and IP are SettingsExpanders");
            Check(home.Contains("Fluent.AccentButton"), "Home connect button is an accent button");
            Check(home.Contains("IpStatusText") && home.Contains("SmallFlag"), "Home keeps the leak verdict and IP flags");
            Check(Regex.Matches(settings, "<views:SettingsCard").Count >= 6, "Settings uses SettingsCards");
            Check(settings.Contains("BlockIpv6") && settings.Contains("Fluent.ComboBox") && settings.Contains("ShowLogsCommand"), "Settings keeps IPv6, DNS picker and logs");
            Check(servers.Contains("views:InfoBar") && servers.Contains("Fluent.TextBox"), "Servers uses InfoBar and the Fluent search box");
            foreach (var f in new[] { home, settings, servers })
                Check(!Regex.IsMatch(f, "Foreground=\"(Red|#FF0000|#[0-9A-Fa-f]{0,2}D32F2F)\""), "no raw red error text");
            var res = File.ReadAllText(Path.Combine(src, "Resources", "Strings.resx"));
            Check(!res.Contains("credit_mdix"), "Material Design credit removed from strings");
            foreach (var k in new[] { "caption_minimize", "caption_maximize", "caption_restore", "caption_close" })
                Check(res.Contains("name=\"" + k + "\""), "caption tooltip " + k);
        }

        /// <summary>1.0.10 tray menu: per-DPI icons, status header, Windows 11 corners.</summary>
        static void TrayMenuRules(string root)
        {
            Check(TrayText.IconPixels(96) == 16 && TrayText.IconPixels(120) == 20 && TrayText.IconPixels(144) == 24 && TrayText.IconPixels(192) == 32, "tray icons 16/20/24/32 px at 100/125/150/200 %");
            Check(TrayText.IconPixels(0) == 16, "unknown DPI falls back to 96");
            Check(TrayText.Header("Đã kết nối", "Japan", true) == "Đã kết nối · Japan", "header shows status and country");
            Check(TrayText.Header("Chưa kết nối", "Japan", false) == "Chưa kết nối", "header hides the country while disconnected");
            Check(TrayText.Dot(true, false, false) == TrayDot.Connected && TrayText.Dot(false, true, false) == TrayDot.Busy && TrayText.Dot(false, false, true) == TrayDot.Problem && TrayText.Dot(false, false, false) == TrayDot.Idle, "header dot by state");
            Check(TrayPalette.For(true) == TrayPalette.Dark && TrayPalette.For(false) == TrayPalette.Light, "menu palette by taskbar theme");
            Check(TrayPalette.Dark.Background != TrayPalette.Light.Background && TrayPalette.Dark.Text != TrayPalette.Light.Text, "light and dark menus differ");
            Check(!TrayText.RoundedCorners(19045) && TrayText.RoundedCorners(22000), "rounded tray menu from Windows 11");
            var tray = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Services", "TrayIcon.cs"));
            Check(tray.Contains("ToolStripRenderer") && tray.Contains("GetDpiForMonitor") && tray.Contains("SystemUsesLightTheme"), "tray menu has its own renderer, DPI and theme");
            Check(tray.Contains("ShowLogsCommand"), "tray menu opens the log viewer");
        }

        /// <summary>1.0.11: bridged (TAP) sessions block IPv6 with WFP filters instead of routes that need Neighbor Discovery.</summary>
        static void Ipv6FirewallRules(string root)
        {
            // When it is used.
            Check(Ipv6Firewall.NeededFor(TunnelLayer.Bridged, true), "bridged session with the toggle on uses the WFP block");
            Check(!Ipv6Firewall.NeededFor(TunnelLayer.Routed, true), "routed session keeps the route block (fe80::8 answered by the driver)");
            Check(!Ipv6Firewall.NeededFor(TunnelLayer.Bridged, false), "toggle off: no WFP block");
            var fw = TunnelPlan.Ipv6BlockOptions(true);
            Check(fw.SequenceEqual(new[] { "--block-ipv6" }), "with the WFP block only --block-ipv6 is passed (no routes to an unreachable gateway)");
            var routed = TunnelPlan.Ipv6BlockOptions(false);
            Check(routed.Contains("--block-ipv6") && routed.Any(x => x.StartsWith("--route-ipv6 2000::/4")), "without it the route block stays as before");

            // Filter plan.
            var plan = Ipv6Firewall.Plan(false);
            var layers = new[] { Ipv6Firewall.LayerAleAuthConnectV6, Ipv6Firewall.LayerAleAuthRecvAcceptV6, Ipv6Firewall.LayerOutboundTransportV6, Ipv6Firewall.LayerInboundTransportV6 };
            foreach (var layer in layers)
            {
                var onLayer = plan.Where(f => f.Layer == layer).ToList();
                Check(onLayer.Count(f => !f.Permit) == 1 && onLayer.Single(f => !f.Permit).Prefix == null && onLayer.Single(f => !f.Permit).Weight == 0, "one block-all filter at the lowest weight on " + layer);
                Check(onLayer.Where(f => f.Permit).All(f => f.Weight == 15 && f.Prefix != null), "permits outrank the block on " + layer);
                Check(onLayer.Where(f => f.Permit).Select(f => f.Prefix).OrderBy(x => x).SequenceEqual(new[] { "::1/128", "::ffff:0:0/96", "fe80::/10", "ff00::/8" }.OrderBy(x => x)), "permits: loopback, IPv4-mapped, link-local, multicast on " + layer);
            }
            Check(plan.Count == 4 * 5, "20 filters without LAN");
            Check(Ipv6Firewall.Plan(true).Count == 4 * 6 && Ipv6Firewall.Plan(true).Any(f => f.Prefix == "fc00::/7"), "allow-LAN adds unique local addresses");
            Check(plan.All(f => f.Layer != Guid.Empty) && layers.Distinct().Count() == 4, "four distinct IPv6 layers");
            Check(Ipv6Firewall.LayerAleAuthConnectV6 == new Guid("4a72393b-319f-44bc-84c3-ba54dcb3b6b4"), "FWPM_LAYER_ALE_AUTH_CONNECT_V6 GUID");
            Check(Ipv6Firewall.LayerAleAuthRecvAcceptV6 == new Guid("a3b42c97-9f04-4672-b87e-cee9c483257f"), "FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V6 GUID");
            Check(Ipv6Firewall.LayerOutboundTransportV6 == new Guid("e1735bde-013f-4655-b351-a49e15762df0"), "FWPM_LAYER_OUTBOUND_TRANSPORT_V6 GUID");
            Check(Ipv6Firewall.LayerInboundTransportV6 == new Guid("634a869f-fc23-4b90-b0c1-bf620a36ae6f"), "FWPM_LAYER_INBOUND_TRANSPORT_V6 GUID");
            Check(Ipv6Firewall.ConditionIpRemoteAddress == new Guid("b235ae9a-1d64-49b8-a44c-5ff3d9095045"), "FWPM_CONDITION_IP_REMOTE_ADDRESS GUID");
            Check(Ipv6Firewall.ActionBlock == 0x1001 && Ipv6Firewall.ActionPermit == 0x1002 && Ipv6Firewall.DataV6AddrMask == 0x101 && Ipv6Firewall.DataUint8 == 1 && Ipv6Firewall.SessionFlagDynamic == 1, "WFP constants");

            // What gets through.
            bool P(string a, bool lan = false) => Ipv6Firewall.IsPermitted(System.Net.IPAddress.Parse(a), lan);
            Check(!P("2402:800:6398:e613:7d1f:bfd4:9baa:9120"), "the reported leaking address is blocked");
            Check(!P("2606:4700:4700::1111") && !P("2001:4860:4860::8888"), "public IPv6 resolvers blocked");
            Check(!P("3fff::1") && !P("64:ff9b::808:808"), "other global and NAT64 addresses blocked");
            Check(P("fe80::1") && P("fe80::1c2b:3cff:fe4d:5e6f"), "link-local (router, LAN neighbours) allowed");
            Check(P("ff02::1") && P("ff02::1:2") && P("ff02::1:ff00:1"), "multicast (ND, RA, DHCPv6) allowed");
            Check(P("::1") && P("::ffff:192.168.1.1"), "loopback and IPv4-mapped allowed");
            Check(!P("fd12:3456::1") && P("fd12:3456::1", true), "unique local only with allow-LAN");
            Check(P("192.168.1.1"), "IPv4 is never touched");
            var (bytes, len) = Ipv6Firewall.ParsePrefix("fe80::/10");
            Check(bytes.Length == 16 && bytes[0] == 0xfe && bytes[1] == 0x80 && len == 10, "prefix parsing");
            var bad = false;
            try { Ipv6Firewall.ParsePrefix("10.0.0.0/8"); } catch (FormatException) { bad = true; }
            Check(bad, "IPv4 prefixes rejected");

            // Native layout, checked against the C compiler for x64 (sizeof/offsetof of the fwpmtypes.h structures).
            var x64 = new WfpLayout(8);
            Check(x64.FilterSize == 200 && x64.FilterDisplay == 16 && x64.FilterFlags == 32 && x64.FilterLayerKey == 64 && x64.FilterSubLayerKey == 80 &&
                  x64.FilterWeight == 96 && x64.FilterNumConditions == 112 && x64.FilterConditions == 120 && x64.FilterAction == 128, "FWPM_FILTER0 layout x64");
            Check(x64.SessionSize == 72 && x64.SessionFlags == 32, "FWPM_SESSION0 layout x64");
            Check(x64.SubLayerSize == 72 && x64.SubLayerDisplay == 16 && x64.SubLayerFlags == 32 && x64.SubLayerWeight == 64, "FWPM_SUBLAYER0 layout x64");
            Check(WfpLayout.ConditionSize == 40 && WfpLayout.ConditionValue == 24 && WfpLayout.ValueUnion == 8, "FWPM_FILTER_CONDITION0 layout");
            var x86 = new WfpLayout(4);
            Check(x86.FilterSize == 168 && x86.FilterLayerKey == 40 && x86.FilterWeight == 72 && x86.FilterNumConditions == 88 && x86.FilterConditions == 92 && x86.FilterAction == 96, "FWPM_FILTER0 layout x86");
            Check(x86.SessionSize == 48 && x86.SessionFlags == 24 && x86.SubLayerSize == 44 && x86.SubLayerWeight == 40, "FWPM_SESSION0 / FWPM_SUBLAYER0 layout x86");

            // Helper protocol.
            var token = new string('a', 32);
            Check(Ipv6Firewall.TryParseHelperArgs(new[] { "--elevated", "ipv6-guard", "1234", token }, out var pid, out var tok) && pid == 1234 && tok == token, "helper arguments parsed");
            foreach (var badArgs in new[] {
                new[] { "--elevated", "ipv6-guard", "0", token }, new[] { "--elevated", "ipv6-guard", "-1", token },
                new[] { "--elevated", "ipv6-guard", "12", "ABC" }, new[] { "--elevated", "ipv6-guard", "12", token + "0" },
                new[] { "--elevated", "ipv6-guard", "12", token, "x" }, new[] { "--elevated", "install-msi", "12", token },
                new[] { "--elevated", "ipv6-guard", "12", "..\\" + token.Substring(3) } })
                Check(!Ipv6Firewall.TryParseHelperArgs(badArgs, out _, out _), "helper rejects " + string.Join(" ", badArgs));
            Check(Ipv6Firewall.EventName(token, Ipv6Firewall.Signal.On) == @"Local\STGPhantomVPN-v6-" + token + "-on", "event names in the session namespace");
            Check(Enum.GetValues(typeof(Ipv6Firewall.Signal)).Cast<Ipv6Firewall.Signal>().Select(x => Ipv6Firewall.EventName(token, x)).Distinct().Count() == 6, "distinct event names");

            // Wiring.
            var src = Path.Combine(root, "src", "STGPhantomVPN");
            var helper = File.ReadAllText(Path.Combine(src, "Services", "ElevatedHelper.cs"));
            var guard = File.ReadAllText(Path.Combine(src, "Services", "Ipv6Guard.cs"));
            var ctl = File.ReadAllText(Path.Combine(src, "Services", "OpenVpnController.cs"));
            var app = File.ReadAllText(Path.Combine(src, "App.xaml.cs"));
            Check(helper.Contains("case Ipv6Firewall.HelperVerb: return RunIpv6Guard(args);"), "helper verb ipv6-guard exists");
            Check(helper.Contains("appExit") && helper.Contains("WaitAny"), "helper ends when the app process ends");
            Check(helper.Contains("\"STGPhantomVPN\""), "helper only serves the app process");
            Check(guard.Contains("Ipv6Firewall.SessionFlagDynamic") && guard.Contains("FwpmEngineClose0"), "filters live in a dynamic session closed on off/exit");
            Check(guard.Contains("FwpmTransactionBegin0") && guard.Contains("FwpmTransactionAbort0"), "filters are added in one transaction");
            Check(!guard.Contains("FwpmFilterDeleteById") && !guard.Contains("netsh"), "no persistent filters, no netsh");
            Check(ctl.Contains("Ipv6Firewall.NeededFor(layer, options.BlockIpv6)") && ctl.Contains("Ipv6Guard.Instance.Enable(options.AllowLocalLan)"), "bridged connects enable the block before OpenVPN starts");
            Check(ctl.IndexOf("Ipv6Guard.Instance.Enable", StringComparison.Ordinal) < ctl.IndexOf("BuildArguments(profilePath", StringComparison.Ordinal), "block enabled before the OpenVPN command line is built");
            var stop = ctl.Substring(ctl.IndexOf("void StopInternal()", StringComparison.Ordinal));
            stop = stop.Substring(0, stop.IndexOf("public static void CleanupOrphan", StringComparison.Ordinal));
            Check(stop.Contains("Ipv6Guard.Instance.Disable()"), "disconnect removes the block");
            var exited = ctl.Substring(ctl.IndexOf("void OnExited(", StringComparison.Ordinal));
            Check(exited.Substring(0, 800).Contains("Ipv6Guard.Instance.Disable()"), "a dropped session removes the block");
            Check(app.Contains("Ipv6Guard.Instance.Dispose()"), "app exit ends the helper");
            Check(new[] { "2000::", "2fff:ffff::1", "3000::1", "3fff:ffff:ffff::1" }.All(a => !P(a, true)), "no rule permits global unicast (2000::/3), even with allow-LAN");
        }

        /// <summary>1.0.11: the source builds on Windows and Linux; English docs by default with Vietnamese copies.</summary>
        static void BuildDocsRules(string root)
        {
            string R(params string[] p) => File.ReadAllText(Path.Combine(new[] { root }.Concat(p).ToArray()));
            var sh = R("build", "build.sh");
            var ps = R("build", "build.ps1");
            var inst = R("build", "build-installer.sh");
            Check(sh.Contains("OUT_DIR=\"${OUT_DIR:-$ROOT/out}\"") && inst.Contains("${OUT_DIR:-$ROOT/out}"), "Linux build writes to <repo>/out by default");
            Check(!sh.Contains("/workspace") && !inst.Contains("/workspace") && !ps.Contains("/workspace"), "no machine-specific output path in the build scripts");
            foreach (var p in new[] { "[switch]$Installer", "[switch]$BundleOpenVpn", "[string]$OutDir", "[switch]$SkipTests" })
                Check(ps.Contains(p), "build.ps1 option " + p);
            Check(ps.Contains("Join-Path $Root 'out'"), "build.ps1 writes to <repo>/out by default");
            Check(ps.Contains("$csproj.Project.PropertyGroup") && ps.Contains("/DAppVersion=$Version"), "build.ps1 takes the version from the csproj and passes it to ISCC");
            Check(ps.Contains("--list-sdks") && ps.Contains("-lt 8"), "build.ps1 checks for .NET SDK 8+");
            Check(ps.Contains("STGPhantomVPN.Tests") && ps.Contains("'run', '-c', 'Release'"), "build.ps1 runs the tests");
            Check(ps.Contains("Inno Setup 6_is1") && ps.Contains("ISCC.exe"), "build.ps1 finds Inno Setup 6");
            Check(ps.Contains("installer/STGPhantomVPN.iss") && ps.Contains("/DAppDir=$Pkg"), "build.ps1 compiles the same .iss");
            Check(!Regex.IsMatch(ps, @"\?\?|\?\s*\$\w+\s*:"), "build.ps1 avoids PowerShell 7-only operators");
            Check(Regex.Matches(sh, @"\[(amd64|arm64|x86)\]=[0-9a-f]{64}").Count == 3, "pinned MSI hashes stay in build.sh (build.ps1 reads them)");
            var iss = R("installer", "STGPhantomVPN.iss");
            Check(!Regex.IsMatch(iss, @"(?i)wine|Z:\\|/home/|/workspace"), "installer script has no Wine or Linux paths");
            Check(iss.Contains(@"SetupIconFile=..\src\STGPhantomVPN\Assets\app.ico"), "installer paths are relative to the .iss");
            foreach (var pair in new[] { ("README.md", "README.vi.md"), ("BUILDING.md", "BUILDING.vi.md") })
            {
                var en = R(pair.Item1);
                var vi = R(pair.Item2);
                Check(en.StartsWith("**English** | [Tiếng Việt](" + pair.Item2 + ")"), pair.Item1 + " starts with the language switch");
                Check(vi.StartsWith("[English](" + pair.Item1 + ") | **Tiếng Việt**"), pair.Item2 + " starts with the language switch");
            }
            Check(R("README.md").Contains("[BUILDING.md](BUILDING.md)") && R("README.vi.md").Contains("[BUILDING.vi.md](BUILDING.vi.md)"), "READMEs link the build guide");
            Check(R("README.md").Contains("## System requirements") && R("README.vi.md").Contains("## Yêu cầu hệ thống"), "README.md is English, README.vi.md Vietnamese");
            var b = R("BUILDING.md");
            foreach (var k in new[] { ".NET SDK 8", "Inno Setup 6", @"build\build.ps1", "Visual Studio 2022", "build/build.sh", "Wine", "`out/`", "<Version>", "SHA-256" })
                Check(b.Contains(k), "BUILDING.md covers " + k);
            Check(sh.Contains("README.vi.md") && ps.Contains("README.vi.md"), "both READMEs go into the package");
        }

        /// <summary>Old author profile handle that must not appear in release files (kept split so this file does not contain it).</summary>
        static readonly string BannedProfile = string.Concat("thanh", "tai", "2009") + "|rufus";

        static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "src", "STGPhantomVPN"))) dir = Path.GetDirectoryName(dir);
            return dir ?? throw new InvalidOperationException("repo root not found");
        }

        const string Ca = "<ca>\n-----BEGIN CERTIFICATE-----\nMIIBAAAA\n-----END CERTIFICATE-----\n</ca>\n";

        static void SanitizerRemovesDangerousDirectives()
        {
            var evil = string.Join("\n", new[]
            {
                "client", "dev tun", "proto tcp", "remote 1.2.3.4 443", "cipher AES-128-CBC",
                "script-security 2", "up \"C:\\\\Windows\\\\System32\\\\cmd.exe /c calc\"", "down evil.bat", "route-up x.bat",
                "route-pre-down y", "ipchange z", "client-connect a", "client-disconnect b", "learn-address c",
                "auth-user-pass-verify d via-env", "tls-verify e", "plugin C:\\evil.dll", "setenv FOO bar",
                "management 0.0.0.0 7505", "log C:\\Windows\\evil.log", "log-append ..\\..\\x", "config other.ovpn",
                "cd C:\\", "writepid C:\\x.pid", "status C:\\x", "--up calc.exe", "  UP calc.exe", "dev-node {GUID}",
                "auth-user-pass C:\\secret.txt", "tls-auth C:\\ta.key 1", "ca C:\\ca.crt",
                "<connection>", "remote 6.6.6.6 1194", "up calc", "</connection>",
                "<ca>", "-----BEGIN CERTIFICATE-----", "up calc.exe", "-----END CERTIFICATE-----", "</ca>",
            });
            var r = OvpnSanitizer.Sanitize(evil);
            var lines = r.Config.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            var names = lines.Where(l => !l.StartsWith("<") && !l.StartsWith("-----")).Select(l => l.Split(' ')[0].ToLowerInvariant()).ToList();
            foreach (var bad in new[] { "up", "down", "route-up", "route-pre-down", "ipchange", "client-connect", "client-disconnect",
                                        "learn-address", "auth-user-pass-verify", "tls-verify", "plugin", "script-security", "setenv",
                                        "management", "log", "log-append", "config", "cd", "writepid", "status", "dev-node", "tls-auth", "ca" })
                Check(!names.Contains(bad), "dangerous directive removed: " + bad);
            Check(!r.Config.Contains("6.6.6.6"), "<connection> block dropped");
            Check(!r.Config.Contains("<ca>"), "<ca> block with directive inside dropped");
            Check(!r.Config.Contains("secret.txt") && r.NeedsAuthUserPass, "auth-user-pass path stripped, flag set");
            Check(r.HasRemote && r.Cipher == "AES-128-CBC", "remote and cipher kept");
            Check(names.Contains("client") && names.Contains("dev") && names.Contains("proto"), "basic client directives kept");
            Check(!r.Config.ToLowerInvariant().Contains("calc"), "no payload text survives");
        }

        static void SanitizerKeepsNormalProfile()
        {
            var good = "dev tun\nproto udp\nremote 219.100.37.12 1194\ncipher AES-128-CBC\ndata-ciphers AES-128-CBC\nauth SHA1\n" +
                       "resolv-retry infinite\nnobind\npersist-key\npersist-tun\nclient\nverb 9\n" + Ca +
                       "<cert>\n-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----\n</cert>\n" +
                       "<key>\n-----BEGIN PRIVATE KEY-----\nBBBB\n-----END PRIVATE KEY-----\n</key>\n";
            var r = OvpnSanitizer.Sanitize(good);
            Check(r.IsUsable, "normal profile usable");
            Check(r.RemovedDirectives.Count == 0, "normal profile has nothing removed (" + string.Join(",", r.RemovedDirectives) + ")");
            Check(r.Config.Contains("<ca>") && r.Config.Contains("<cert>") && r.Config.Contains("<key>"), "inline key blocks kept");
            Check(r.Config.Contains("verb 3\n"), "verbosity capped");
        }

        static void BundledListParsesAndSanitizes(string path)
        {
            var list = VpnServer.ParseCsv(File.ReadAllText(path, Encoding.UTF8));
            Check(list.Count > 50, "bundled list parsed: " + list.Count);
            SanitizeAll(list, "bundled");
        }

        static void SanitizeAll(List<VpnServer> list, string label)
        {
            int usable = 0, removed = 0;
            foreach (var s in list)
            {
                var raw = s.DecodeOpenVpnConfig();
                var r = OvpnSanitizer.Sanitize(raw);
                if (r.IsUsable) usable++;
                removed += r.RemovedDirectives.Count;
                foreach (var d in r.Config.Split('\n'))
                {
                    var name = d.Trim().Split(' ')[0];
                    if (OvpnSanitizer.Dangerous.Contains(name)) Check(false, label + " " + s.HostName + " still has " + name);
                }
            }
            Check(usable == list.Count, $"{label}: all {list.Count} profiles usable after sanitizing (usable {usable}, removed {removed})");
            Console.WriteLine($"{label}: {list.Count} servers, {usable} usable, {removed} directives removed in total");
        }

        static void Picker()
        {
            var a = new VpnServer { HostName = "a", Ip = "1", Score = 10, Ping = 50, CountryLong = "Japan", CountryShort = "JP" };
            var b = new VpnServer { HostName = "b", Ip = "2", Score = 99, Ping = 0, CountryLong = "Korea", CountryShort = "KR" };
            var v = new VpnServer { HostName = "v", Ip = "3", Score = 1, Ping = 90, CountryLong = "Viet Nam", CountryShort = "VN" };
            var sorted = ServerPicker.FilterAndSort(new[] { a, b, v }, "", ServerSort.Score);
            Check(sorted[0] == v && sorted[1] == b, "Vietnam first, then score order");
            var byPing = ServerPicker.FilterAndSort(new[] { a, b }, "", ServerSort.Ping);
            Check(byPing[0] == a, "ping sort puts unknown ping last");
            Check(ServerPicker.FilterAndSort(new[] { a, b, v }, "kor", ServerSort.Score).Single() == b, "search by country");
            var next = ServerPicker.PickNext(new List<VpnServer> { a, b }, new HashSet<string> { a.Key });
            Check(next == b, "timed-out server excluded from next pick");
            Check(ServerPicker.PickNext(new List<VpnServer> { a }, new HashSet<string> { a.Key }) == null, "no other server");
            Check(Formatters.FormatDuration(TimeSpan.FromSeconds(3725)) == "01:02:05", "duration format");
            Check(Formatters.FormatBytes(1536) == "1.5 KB", "bytes format");
        }

        static void NoPlainHttpInSources(string root)
        {
            Check(VpnGateClient.ApiUrl.StartsWith("https://"), "VPNGate API is HTTPS");
            foreach (var f in Directory.GetFiles(Path.Combine(root, "src", "STGPhantomVPN"), "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                var text = File.ReadAllText(f);
                Check(!text.Contains("\"http://"), "no plain-HTTP URL in " + Path.GetFileName(f));
                Check(!text.Contains("GetParent") && !text.Contains("\"..\""), "no parent-folder search in " + Path.GetFileName(f));
            }
        }

        /// <summary>Writes sanitized live profiles (picked like the app does) for an external openvpn smoke test.</summary>
        static void DumpSanitized(string bundled, string dir)
        {
            Directory.CreateDirectory(dir);
            var client = new VpnGateClient(Path.Combine(dir, "cache.csv"), bundled);
            var list = client.FetchAsync(CancellationToken.None).GetAwaiter().GetResult();
            var ordered = ServerPicker.FilterAndSort(list, "", ServerSort.Score).Take(10).ToList();
            foreach (var s in ordered)
                File.WriteAllText(Path.Combine(dir, s.HostName + ".ovpn"), OvpnSanitizer.Sanitize(s.DecodeOpenVpnConfig()).Config);
            Console.WriteLine("dumped " + ordered.Count + " profiles to " + dir);
        }

        static void LiveVpnGate()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "stg-vpngate-test.csv");
            var client = new VpnGateClient(tmp, "/nonexistent");
            var list = client.FetchAsync(CancellationToken.None).GetAwaiter().GetResult();
            Check(list.Count > 0, "live HTTPS fetch returned " + list.Count + " servers");
            Check(File.Exists(tmp), "cache written");
            SanitizeAll(list, "live");
        }

        static List<string> DirectiveNames(SanitizedOvpn r)
        {
            var names = new List<string>();
            var inBlock = false;
            foreach (var raw in r.Config.Split('\n'))
            {
                var l = raw.Trim();
                if (l.Length == 0) continue;
                if (Regex.IsMatch(l, "^<[a-z0-9-]+>$")) { inBlock = true; continue; }
                if (Regex.IsMatch(l, "^</[a-z0-9-]+>$")) { inBlock = false; continue; }
                if (!inBlock) names.Add(l.Split(' ')[0]);
            }
            return names;
        }

        static readonly HashSet<string> ExpectedSafe = new HashSet<string>
        {
            "client", "dev", "proto", "remote", "cipher", "auth", "nobind", "persist-key", "persist-tun", "verb", "data-ciphers",
            "resolv-retry", "keepalive", "key-direction", "remote-cert-tls",
        };

        static void SanitizerBypassAttempts()
        {
            var basic = "client\ndev tun\nremote 1.2.3.4 443\n";
            var attempts = new Dictionary<string, string>
            {
                ["crlf"] = "up calc.exe\r\nscript-security 2\r\n",
                ["lone cr"] = "client\rup calc.exe\r",
                ["upper case"] = "SCRIPT-SECURITY 2\nUp calc.exe\nPLUGIN x.dll\n",
                ["leading whitespace"] = " \t up calc.exe\n\t\tplugin x.dll\n",
                ["double dash"] = "--up calc.exe\n--plugin x.dll\n--management 0.0.0.0 1\n",
                ["triple dash"] = "---up calc.exe\n",
                ["quoted name"] = "\"up\" calc.exe\n'up' calc.exe\n",
                ["escaped name"] = "u\\p calc.exe\n\\up calc.exe\n",
                ["bom"] = "\uFEFFup calc.exe\n",
                ["nbsp separator"] = "up\u00A0calc.exe\n",
                ["vertical tab"] = "up\vcalc.exe\n",
                ["nul byte"] = "client\0up calc.exe\n",
                ["unicode look-alike"] = "\u0075\u0070\u200B calc.exe\nup\u0131 x\n",
                ["kelvin sign"] = "\u212Aeepalive 10 60\nup calc.exe\n",
                ["windows-driver"] = "windows-driver wintun\ndev-node evil\n",
                ["setenv-safe"] = "setenv-safe FORWARD_COMPATIBLE 1\nsetenv opt up calc.exe\nignore-unknown-option up\n",
                ["status/log paths"] = "status C:/x\nlog-append //host/share/x\nwritepid C:/x\nlog C:/x\n",
                ["daemon/chroot/cd"] = "daemon\nchroot C:/\ncd C:/\nconfig C:/evil.ovpn\n",
                ["comment trick"] = "# x\n; y\nremote 5.5.5.5 443 # up calc.exe\n",
                ["quoted path arg"] = "remote \"C:\\evil\" 443\nproto \"tcp up calc\"\n",
                ["unterminated quote"] = "remote \"1.2.3.4 443 up calc.exe\n",
                ["connection block"] = "<connection>\nremote 6.6.6.6\nup calc.exe\n</connection>\n",
                ["unknown block"] = "<plugin>\nx.dll\n</plugin>\n<auth-user-pass>\nvpn\nvpn\n</auth-user-pass>\n",
                ["nested close"] = "<ca>\n-----BEGIN CERTIFICATE-----\n</key>\nup calc.exe\n</ca>\n",
                ["directive in block"] = "<tls-auth>\nscript-security 2\n</tls-auth>\n",
                ["long comment in block"] = "<ca>\n#" + new string('A', 254) + "</ca>\nplugin x.dll\n-----BEGIN CERTIFICATE-----\nAAAA\n</ca>\n",
                ["long armor in block"] = "<ca>\n-----BEGIN " + new string('A', 300) + "</ca>up-----\n</ca>\n",
                ["long line split"] = "remote " + new string('a', 245) + " up calc.exe\n",
                ["cipher none"] = "cipher none\ndata-ciphers AES-128-GCM:none\nauth none\n",
                ["compression"] = "allow-compression yes\n",
                ["auth file"] = "auth-user-pass C:/Users/Public/creds.txt\n",
                ["redirect"] = "route-up \"cmd /c calc\"\nroute-pre-down x\nipchange x\nlearn-address x\ntls-verify x\nclient-connect x\nclient-disconnect x\nauth-user-pass-verify x via-file\n",
            };
            foreach (var kv in attempts)
            {
                var r = OvpnSanitizer.Sanitize(basic + kv.Value);
                var names = DirectiveNames(r);
                var unexpected = names.Where(n => !ExpectedSafe.Contains(n)).ToList();
                Check(unexpected.Count == 0, "bypass '" + kv.Key + "' kept: " + string.Join(",", unexpected));
                var lower = r.Config.ToLowerInvariant();
                Check(!lower.Contains("calc") && !lower.Contains("x.dll") && !lower.Contains("6.6.6.6") && !lower.Contains("c:/") && !lower.Contains("none"),
                      "bypass '" + kv.Key + "' payload text removed");
                Check(r.Config.Split('\n').All(l => l.Length <= 250), "bypass '" + kv.Key + "' lines fit OpenVPN's 256-byte buffer");
                Check(r.Config.All(c => c == '\n' || c >= 0x20 && c < 0x7F), "bypass '" + kv.Key + "' output is printable ASCII");
                Check(r.IsUsable, "bypass '" + kv.Key + "' still usable");
            }

            var bom = OvpnSanitizer.Sanitize("\uFEFFclient\r\ndev tun\r\nremote 1.2.3.4 1194\r\n");
            Check(DirectiveNames(bom).Contains("client"), "BOM does not hide the first directive");
            var kelvin = OvpnSanitizer.Sanitize(basic + "\u212Aeepalive 10 60\n");
            Check(!kelvin.Config.Contains("\u212A"), "non-ASCII directive name never echoed");
            var key = OvpnSanitizer.Sanitize(basic + "<tls-auth>\n# 2048 bit OpenVPN static key\n-----BEGIN OpenVPN Static key V1-----\n0123abcd\n-----END OpenVPN Static key V1-----\n</tls-auth>\nkey-direction 1\n");
            Check(key.Config.Contains("<tls-auth>\n-----BEGIN OpenVPN Static key V1-----\n0123abcd\n") && !key.Config.Contains("2048 bit"), "static key kept, comment dropped");
            var tcp = OvpnSanitizer.Sanitize("client\ndev tun\nremote 1.2.3.4 443 tcp\n--proto tcp\n");
            Check(tcp.Config.Contains("remote 1.2.3.4 443 tcp\n") && tcp.Config.Contains("proto tcp\n"), "'--' prefix normalized for allowed directives");
        }

        sealed class FakeTapSource : ITapSystemSource
        {
            public List<NetAdapterEntry> Adapters = new List<NetAdapterEntry>();
            public List<string> Interfaces = new List<string>();
            public bool Driver;
            public int DriverQueries;
            public IEnumerable<NetAdapterEntry> RegisteredAdapters() => Adapters;
            public IEnumerable<string> EnumeratedInterfaceIds() => Interfaces;
            public bool DriverPackagePresent() { DriverQueries++; return Driver; }
        }

        const string TapGuid = "{A1B2C3D4-0000-1111-2222-333344445555}";
        const string WintunGuid = "{0F0E0D0C-AAAA-BBBB-CCCC-DDDDEEEEFFFF}";

        static void TapDetection()
        {
            Check(TapDetector.IsTapComponent("tap0901"), "tap0901 is TAP");
            Check(TapDetector.IsTapComponent("ROOT\\TAP0901"), "root\\tap0901 is TAP (case-insensitive)");
            Check(TapDetector.IsTapComponent(" tap0901 "), "ComponentId is trimmed");
            foreach (var id in new[] { null, "", "wintun", "ovpn-dco", "tap0801", "tap0901x", "pci\\ven_8086", "root\\tap0901\\0000" })
                Check(!TapDetector.IsTapComponent(id), "not TAP: " + (id ?? "null"));

            Check(TapDetector.NormalizeGuid("a1b2c3d4-0000-1111-2222-333344445555") == TapGuid, "GUID without braces normalized");
            Check(TapDetector.NormalizeGuid(TapGuid.ToLowerInvariant()) == TapGuid, "GUID case normalized");
            Check(TapDetector.NormalizeGuid("not-a-guid") == null && TapDetector.NormalizeGuid(null) == null, "invalid GUID rejected");

            var empty = new FakeTapSource();
            var r = TapDetector.Evaluate(empty);
            Check(r.Status == TapStatus.Missing && r.BlocksConnect && !r.IsReady, "nothing installed: Missing");

            var ready = new FakeTapSource
            {
                Adapters = { new NetAdapterEntry { ComponentId = "wintun", NetCfgInstanceId = WintunGuid }, new NetAdapterEntry { ComponentId = "tap0901", NetCfgInstanceId = TapGuid } },
                Interfaces = { WintunGuid, TapGuid.ToLowerInvariant() },
            };
            r = TapDetector.Evaluate(ready);
            Check(r.Status == TapStatus.Ready && r.IsReady && !r.BlocksConnect, "enumerated TAP adapter: Ready");
            Check(r.RegisteredAdapters == 1 && r.EnumeratedAdapters == 1, "only TAP adapters counted");
            Check(ready.DriverQueries == 0, "driver store not queried when an adapter is live");

            var onlyWintun = new FakeTapSource { Adapters = { new NetAdapterEntry { ComponentId = "wintun", NetCfgInstanceId = WintunGuid } }, Interfaces = { WintunGuid } };
            Check(TapDetector.Evaluate(onlyWintun).Status == TapStatus.Missing, "Wintun/DCO adapters do not count as TAP");

            var disabled = new FakeTapSource { Adapters = { new NetAdapterEntry { ComponentId = "tap0901", NetCfgInstanceId = TapGuid } }, Driver = true };
            r = TapDetector.Evaluate(disabled);
            Check(r.Status == TapStatus.AdapterInactive && !r.BlocksConnect && r.DriverPackage, "registered but not enumerated: AdapterInactive, connect not blocked");

            var noGuid = new FakeTapSource { Adapters = { new NetAdapterEntry { ComponentId = "tap0901", NetCfgInstanceId = null } }, Interfaces = { TapGuid } };
            Check(TapDetector.Evaluate(noGuid).Status == TapStatus.AdapterInactive, "TAP entry without NetCfgInstanceId is not treated as live");

            var driverOnly = new FakeTapSource { Driver = true, Interfaces = { WintunGuid } };
            r = TapDetector.Evaluate(driverOnly);
            Check(r.Status == TapStatus.DriverOnly && r.BlocksConnect, "staged driver without adapter: DriverOnly, connect blocked");

            var nulls = new FakeTapSource { Adapters = { null, new NetAdapterEntry() } };
            Check(TapDetector.Evaluate(nulls).Status == TapStatus.Missing, "null and empty entries ignored");

            Check(TapDetector.IsTapDriverStoreFolder("oemvista.inf_amd64_3f2a1b0c9d8e7f6a"), "OemVista.inf driver store folder recognised");
            Check(!TapDetector.IsTapDriverStoreFolder("wintun.inf_amd64_1234") && !TapDetector.IsTapDriverStoreFolder(null), "other driver store folders ignored");
        }

        static void TapRepairChoice()
        {
            TapCheckResult R(TapStatus s, bool driver) => new TapCheckResult { Status = s, DriverPackage = driver };
            Check(TapDetector.ChooseRepair(R(TapStatus.Ready, true), true) == TapRepair.None, "ready: nothing to repair");
            Check(TapDetector.ChooseRepair(null, true) == TapRepair.None, "no check: nothing to repair");
            Check(TapDetector.ChooseRepair(R(TapStatus.DriverOnly, true), true) == TapRepair.CreateAdapter, "driver staged + tapctl: create adapter");
            Check(TapDetector.ChooseRepair(R(TapStatus.AdapterInactive, true), true) == TapRepair.CreateAdapter, "inactive adapter + tapctl: create adapter");
            Check(TapDetector.ChooseRepair(R(TapStatus.DriverOnly, true), false) == TapRepair.InstallMsi, "no verified tapctl: MSI");
            Check(TapDetector.ChooseRepair(R(TapStatus.AdapterInactive, false), true) == TapRepair.InstallMsi, "adapter without staged driver: MSI");
            Check(TapDetector.ChooseRepair(R(TapStatus.Missing, false), true) == TapRepair.InstallMsi, "missing: MSI");
            Check(TapDetector.ChooseRepair(R(TapStatus.Missing, true), true) == TapRepair.InstallMsi, "missing never uses tapctl");
        }

        static void OpenVpnMsiProperties()
        {
            Check(OpenVpnMsiPlan.BuildProperties(null, false) == "ADDLOCAL=OpenVPN,OpenVPN.Service,Drivers,Drivers.TAPWindows6,Drivers.OvpnDco",
                "fresh install keeps the 1.0.0 feature set");
            var withGui = OpenVpnMsiPlan.BuildProperties(new[] { "OpenVPN", "OpenVPN.GUI", "OpenVPN.GUI.OnLogon", "Drivers", "Drivers.Wintun", "Drivers.OvpnDco" }, false);
            Check(withGui == "ADDLOCAL=OpenVPN,OpenVPN.GUI,OpenVPN.GUI.OnLogon,OpenVPN.Service,Drivers,Drivers.TAPWindows6,Drivers.OvpnDco,Drivers.Wintun",
                "installed features carried over and TAP added: " + withGui);
            Check(!withGui.Contains("REINSTALL"), "no REINSTALL when TAP feature is absent");
            var unknown = OpenVpnMsiPlan.BuildProperties(new[] { "Evil,Feature", "OpenVPN.GUI REMOVE=ALL" }, false);
            Check(unknown == "ADDLOCAL=OpenVPN,OpenVPN.Service,Drivers,Drivers.TAPWindows6,Drivers.OvpnDco", "unknown feature names never reach msiexec");
            var repair = OpenVpnMsiPlan.BuildProperties(new[] { "OpenVPN", "OpenVPN.Service", "Drivers", "Drivers.TAPWindows6" }, true);
            Check(repair == "ADDLOCAL=OpenVPN,OpenVPN.Service,Drivers,Drivers.OvpnDco REINSTALL=Drivers.TAPWindows6 REINSTALLMODE=emus",
                "TAP feature installed on pinned product: reinstall it: " + repair);
            var otherVersion = OpenVpnMsiPlan.BuildProperties(new[] { "Drivers.TAPWindows6" }, false);
            Check(!otherVersion.Contains("REINSTALL") && otherVersion.Contains("Drivers.TAPWindows6"), "upgrade from another version: plain ADDLOCAL");

            Check(OpenVpnMsiPlan.KnownFeatures.Length == OpenVpnMsiPlan.KnownFeatures.Distinct().Count(), "feature list has no duplicates");
            Check(OpenVpnMsiPlan.RequiredFeatures.All(OpenVpnMsiPlan.KnownFeatures.Contains), "required features are known");

            var pin = PinnedDownloads.OpenVpnProductVersion;
            Check(OpenVpnMsiPlan.IsNewerThanPinned("2.6.2401", pin), "2.6.24 is newer than pinned");
            Check(OpenVpnMsiPlan.IsNewerThanPinned("2.7.0", pin), "2.7.0 is newer than pinned");
            Check(!OpenVpnMsiPlan.IsNewerThanPinned(pin, pin), "pinned is not newer than itself");
            Check(!OpenVpnMsiPlan.IsNewerThanPinned("2.6.1500", pin), "2.6.15 is older");
            Check(!OpenVpnMsiPlan.IsNewerThanPinned(null, pin) && !OpenVpnMsiPlan.IsNewerThanPinned("garbage", pin), "unparsable version is not newer");

            foreach (var arch in new[] { "amd64", "arm64", "x86" })
                Check(Guid.TryParse(PinnedDownloads.OpenVpnUpgradeCode(arch), out _), "UpgradeCode is a GUID for " + arch);
            Check(PinnedDownloads.OpenVpnUpgradeCode("amd64") != PinnedDownloads.OpenVpnUpgradeCode("x86"), "UpgradeCode differs per architecture");
            Check(Guid.TryParse(PinnedDownloads.OpenVpnProductCode, out _), "ProductCode is a GUID");
        }

        static readonly WindowsVersion Win7Sp1 = new WindowsVersion(6, 1, 7601, 1);
        static readonly WindowsVersion Win8 = new WindowsVersion(6, 2, 9200);
        static readonly WindowsVersion Win81 = new WindowsVersion(6, 3, 9600);
        static readonly WindowsVersion Win10Rtm = new WindowsVersion(10, 0, 10240);
        static readonly WindowsVersion Win10_1909 = new WindowsVersion(10, 0, 18363);
        static readonly WindowsVersion Win10_2004 = new WindowsVersion(10, 0, 19041);
        static readonly WindowsVersion Win11 = new WindowsVersion(10, 0, 26100);

        static void OsGating()
        {
            Check(OsSupport.Family(Win7Sp1) == WindowsFamily.Windows7, "7601 is Windows 7");
            Check(OsSupport.Family(Win8) == WindowsFamily.Windows8, "9200 is Windows 8");
            Check(OsSupport.Family(Win81) == WindowsFamily.Windows81, "9600 is Windows 8.1");
            Check(OsSupport.Family(Win10Rtm) == WindowsFamily.Windows10OrLater && OsSupport.Family(Win11) == WindowsFamily.Windows10OrLater, "10240+ is Windows 10 or later");
            Check(OsSupport.Family(new WindowsVersion(6, 0, 6002)) == WindowsFamily.Older, "Vista is older");
            Check(OsSupport.Family(default(WindowsVersion)) == WindowsFamily.Unknown, "zero version is unknown");

            Check(!OsSupport.IsWindows10OrLater(new WindowsVersion(6, 3, 10239)), "build 10239 is not Windows 10");
            Check(OsSupport.IsWindows10OrLater(Win10Rtm), "build 10240 is Windows 10");


            Check(!OsSupport.SupportsOvpnDco(Win7Sp1) && !OsSupport.SupportsOvpnDco(Win81), "no ovpn-dco on Windows 7/8.1");
            Check(!OsSupport.SupportsOvpnDco(Win10Rtm) && !OsSupport.SupportsOvpnDco(Win10_1909), "no ovpn-dco before Windows 10 2004");
            Check(OsSupport.SupportsOvpnDco(Win10_2004) && OsSupport.SupportsOvpnDco(Win11), "ovpn-dco from Windows 10 2004");

            Check(OsSupport.NeedsExplicitTls12(Win7Sp1) && OsSupport.NeedsExplicitTls12(Win81), "TLS 1.2 forced on Windows 7/8.1");
            Check(!OsSupport.NeedsExplicitTls12(Win10Rtm) && !OsSupport.NeedsExplicitTls12(Win11), "SystemDefault kept on Windows 10/11");

            Check(OsSupport.DisplayName(Win7Sp1) == "Windows 7 SP1 (build 7601)", "Windows 7 display name");
            Check(OsSupport.DisplayName(Win11).StartsWith("Windows 11"), "Windows 11 display name");
            Check(OsSupport.DisplayName(Win10_2004).StartsWith("Windows 10"), "Windows 10 display name");

            var dcoOff = OpenVpnMsiPlan.BuildProperties(null, false, includeOvpnDco: false);
            Check(dcoOff == "ADDLOCAL=OpenVPN,OpenVPN.Service,Drivers,Drivers.TAPWindows6", "ADDLOCAL without ovpn-dco on older Windows: " + dcoOff);
            var dcoOffInstalled = OpenVpnMsiPlan.BuildProperties(new[] { "Drivers.OvpnDco", "OpenVPN.GUI" }, false, includeOvpnDco: false);
            Check(!dcoOffInstalled.Contains("OvpnDco") && dcoOffInstalled.Contains("OpenVPN.GUI"), "installed ovpn-dco is not re-requested on older Windows");
            var dcoOffRepair = OpenVpnMsiPlan.BuildProperties(new[] { "Drivers.TAPWindows6" }, true, includeOvpnDco: false);
            Check(dcoOffRepair == "ADDLOCAL=OpenVPN,OpenVPN.Service,Drivers REINSTALL=Drivers.TAPWindows6 REINSTALLMODE=emus", "TAP repair without ovpn-dco: " + dcoOffRepair);
            Check(OpenVpnMsiPlan.BuildProperties(null, false, includeOvpnDco: true) == OpenVpnMsiPlan.BuildProperties(null, false), "default keeps ovpn-dco");
        }

        sealed class FakeProbe : ISystemProbe
        {
            public HashSet<string> Files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ucrtbase.dll", "d3dcompiler_47.dll" };
            public Dictionary<string, Version> Versions = new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Hotfixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "KB4474419" };
            public HashSet<string> Roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Prerequisites.DigiCertTrustedRootG4.Sha1Thumbprint, Prerequisites.IsrgRootX1.Sha1Thumbprint,
            };
            public bool AutoUpdateOff;
            public int Calls;

            public bool SystemFileExists(string f) { Calls++; return Files.Contains(f); }
            public Version SystemFileVersion(string f) { Calls++; return Versions.TryGetValue(f, out var v) ? v : null; }
            public bool IsHotfixInstalled(string kb) { Calls++; return Hotfixes.Contains(kb); }
            public bool IsRootCertificatePresent(string t) { Calls++; return Roots.Contains(t); }
            public bool RootAutoUpdateDisabled => AutoUpdateOff;
        }

        static string Kbs(PrerequisiteReport r) => string.Join(",", r.MissingUpdates.Select(u => u.Kb));

        static void PrerequisiteChecks()
        {
            var clean = new FakeProbe();
            var r = Prerequisites.Evaluate(Win7Sp1, clean);
            Check(r.IsClean && !r.BlocksOpenVpn, "fully patched Windows 7 is clean");

            var bare = new FakeProbe();
            bare.Files.Clear(); bare.Hotfixes.Clear(); bare.Roots.Clear();
            r = Prerequisites.Evaluate(Win7Sp1, bare);
            Check(Kbs(r) == "KB2999226,KB4474419,KB4019990", "bare Windows 7 lists all three KBs in order: " + Kbs(r));
            Check(r.BlocksOpenVpn, "missing KBs block OpenVPN");
            Check(r.MissingRoots.Count == 2 && r.HasRootWarning, "both roots reported missing");

            var noUcrt = new FakeProbe();
            noUcrt.Files.Remove("ucrtbase.dll");
            Check(Kbs(Prerequisites.Evaluate(Win7Sp1, noUcrt)) == "KB2999226", "missing ucrtbase.dll means KB2999226");

            var noD3d = new FakeProbe();
            noD3d.Files.Remove("d3dcompiler_47.dll");
            Check(Kbs(Prerequisites.Evaluate(Win7Sp1, noD3d)) == "KB4019990", "missing d3dcompiler_47.dll means KB4019990");

            var noSha2 = new FakeProbe();
            noSha2.Hotfixes.Clear();
            Check(Kbs(Prerequisites.Evaluate(Win7Sp1, noSha2)) == "KB4474419", "no SHA-2 evidence means KB4474419");

            noSha2.Versions["ntoskrnl.exe"] = new Version(6, 1, 7601, 24499);
            Check(Kbs(Prerequisites.Evaluate(Win7Sp1, noSha2)) == "KB4474419", "July 2019 kernel is not SHA-2 evidence");
            noSha2.Versions["ntoskrnl.exe"] = new Version(6, 1, 7601, 24511);
            Check(Prerequisites.Evaluate(Win7Sp1, noSha2).IsClean, "August 2019 (SHA-2-only) kernel proves KB4474419 even when the KB is hidden");
            noSha2.Versions["ntoskrnl.exe"] = new Version(6, 1, 7601, 26321);
            Check(Prerequisites.Evaluate(Win7Sp1, noSha2).IsClean, "ESU-era kernel proves KB4474419");
            noSha2.Versions["ntoskrnl.exe"] = new Version(10, 0, 19041, 1);
            Check(!Prerequisites.HasSha2CodeSigning(noSha2), "a non-Windows-7 kernel version is not used as evidence");

            var noRoots = new FakeProbe();
            noRoots.Roots.Remove(Prerequisites.IsrgRootX1.Sha1Thumbprint);
            r = Prerequisites.Evaluate(Win7Sp1, noRoots);
            Check(!r.BlocksOpenVpn && r.HasRootWarning && !r.IsClean, "missing root warns but does not block");
            Check(r.MissingRoots.Single().Name == "ISRG Root X1", "missing root is named");
            Check(!r.RootAutoUpdateDisabled, "auto-update flag only when policy disables it");
            noRoots.AutoUpdateOff = true;
            Check(Prerequisites.Evaluate(Win7Sp1, noRoots).RootAutoUpdateDisabled, "policy-disabled root auto-update is reported");
            noRoots.Roots.Add(Prerequisites.IsrgRootX1.Sha1Thumbprint);
            Check(!Prerequisites.Evaluate(Win7Sp1, noRoots).RootAutoUpdateDisabled, "policy flag ignored when no root is missing");

            var w81 = new FakeProbe();
            w81.Files.Clear(); w81.Hotfixes.Clear();
            r = Prerequisites.Evaluate(Win81, w81);
            Check(Kbs(r) == "KB2999226", "Windows 8.1 only needs the UCRT update: " + Kbs(r));
            w81.Files.Add("ucrtbase.dll");
            Check(Prerequisites.Evaluate(Win81, w81).IsClean, "Windows 8.1 with UCRT and roots is clean");
            w81.Roots.Clear();
            r = Prerequisites.Evaluate(Win81, w81);
            Check(!r.BlocksOpenVpn && r.MissingRoots.Count == 2, "Windows 8.1 root check warns only");
            Check(Kbs(Prerequisites.Evaluate(Win8, new FakeProbe())) == "", "Windows 8 with UCRT needs no KB");

            foreach (var v in new[] { Win10Rtm, Win10_2004, Win11 })
            {
                var untouched = new FakeProbe();
                untouched.Files.Clear(); untouched.Hotfixes.Clear(); untouched.Roots.Clear();
                Check(Prerequisites.Evaluate(v, untouched).IsClean && untouched.Calls == 0, "Windows " + v + " skips every prerequisite probe");
            }

            foreach (var u in new[] { Prerequisites.UniversalCrt, Prerequisites.Sha2CodeSigning, Prerequisites.D3DCompiler })
            {
                Check(Prerequisites.IsTrustedUpdateLink(u.Url) && u.Url.EndsWith("q=" + u.Kb), u.Kb + " links to the Microsoft Update Catalog");
                Check(u.DescriptionKey == "prereq_" + u.Kb.ToLowerInvariant(), u.Kb + " has a matching resource key");
            }
            Check(!Prerequisites.IsTrustedUpdateLink("http://www.catalog.update.microsoft.com/Search.aspx?q=KB1"), "plain HTTP catalog link rejected");
            Check(!Prerequisites.IsTrustedUpdateLink("https://www.catalog.update.microsoft.com.evil.example/x"), "look-alike host rejected");
            Check(!Prerequisites.IsTrustedUpdateLink("file:///C:/Windows/System32/calc.exe") && !Prerequisites.IsTrustedUpdateLink(null), "non-catalog targets rejected");
            Check(Prerequisites.DigiCertTrustedRootG4.Sha1Thumbprint.Length == 40 && Prerequisites.IsrgRootX1.Sha1Thumbprint.Length == 40, "root thumbprints are SHA-1 hex");
        }

        /// <summary>Without the Windows 7/8/8.1 GUIDs, Windows applies compatibility shims to the process.</summary>
        static void ManifestDeclaresOldWindows(string root)
        {
            var manifest = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "app.manifest"));
            foreach (var guid in new[] { "35138b9a-5d96-4fbd-8e2d-a2440225f93a", "4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38", "1f676c76-80e1-4239-95bb-83d0f6d0da78", "8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a" })
                Check(manifest.Contains("{" + guid + "}"), "manifest supportedOS " + guid);
            var neutral = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Resources", "Strings.resx"));
            foreach (var key in new[] { "prereq_kb2999226", "prereq_kb4474419", "prereq_kb4019990" })
                Check(neutral.Contains("name=\"" + key + "\""), "resource " + key + " exists");
        }

        /// <summary>The version must be the same in the csproj, manifest, About screen constant, build script and README.</summary>
        static void WaitHandleProcessTracking()
        {
            var exit = new ManualResetEvent(false);
            var terminateCalls = 0;
            var p = new WaitHandleOpenVpnProcess(4242, exit, () => { terminateCalls++; exit.Set(); return true; });
            var raised = 0;
            var raisedEvent = new ManualResetEventSlim(false);
            p.Exited += () => { Interlocked.Increment(ref raised); raisedEvent.Set(); };
            Check(p.Id == 4242, "wait-handle process keeps the PID");
            Check(!p.HasExited, "wait-handle process running while the handle is unsignaled");
            Check(!p.WaitForExit(50), "WaitForExit times out while running");
            p.StartWatching();
            p.StartWatching();
            Check(!raisedEvent.Wait(100), "Exited not raised while running");
            Check(p.TryKill(2000), "TryKill succeeds when termination is allowed");
            Check(terminateCalls == 1, "TryKill calls terminate once");
            Check(raisedEvent.Wait(2000), "Exited raised after exit");
            Thread.Sleep(100);
            Check(raised == 1, "Exited raised exactly once");
            Check(p.HasExited && p.WaitForExit(0), "HasExited and WaitForExit after exit");
            Check(p.TryKill(0) && terminateCalls == 1, "TryKill on an exited process does not terminate again");
            p.Dispose();
            p.Dispose();
            Check(p.HasExited, "disposed wait-handle process reports exited without throwing");

            // Already exited before watching starts: Exited is raised from StartWatching.
            var done = new ManualResetEvent(true);
            var q = new WaitHandleOpenVpnProcess(1, done, null);
            var qRaised = 0;
            q.Exited += () => Interlocked.Increment(ref qRaised);
            q.StartWatching();
            Thread.Sleep(100);
            Check(qRaised == 1, "Exited raised once for a process that exited before watching");
            q.Dispose();

            // Termination denied (as for a process the user may not end): TryKill reports failure, never throws.
            var stuck = new ManualResetEvent(false);
            var errors = 0;
            var r = new WaitHandleOpenVpnProcess(2, stuck, () => false) { Error = (m, e) => errors++ };
            Check(!r.TryKill(50), "TryKill false when termination is denied");
            var t = new WaitHandleOpenVpnProcess(3, new ManualResetEvent(false), () => throw new UnauthorizedAccessException("denied")) { Error = (m, e) => errors++ };
            var threw = false;
            try { Check(!t.TryKill(50), "TryKill false when terminate throws"); }
            catch (Exception) { threw = true; }
            Check(!threw && errors == 1, "terminate exception is reported, not thrown");

            // A throwing Exited handler must not escape to the thread pool.
            var h = new ManualResetEvent(false);
            var handlerErrors = new ManualResetEventSlim(false);
            var u = new WaitHandleOpenVpnProcess(5, h, null) { Error = (m, e) => handlerErrors.Set() };
            u.Exited += () => throw new InvalidOperationException("handler");
            u.StartWatching();
            h.Set();
            Check(handlerErrors.Wait(2000), "Exited handler exception is reported");

            // Disposing before exit stops the notification.
            var v = new ManualResetEvent(false);
            var vp = new WaitHandleOpenVpnProcess(6, v, null);
            var vRaised = 0;
            vp.Exited += () => Interlocked.Increment(ref vRaised);
            vp.StartWatching();
            vp.Dispose();
            try { v.Set(); } catch (ObjectDisposedException) { }
            Thread.Sleep(100);
            Check(vRaised == 0, "no Exited after Dispose");
            r.Dispose();
            t.Dispose();
            u.Dispose();
        }

        static void PollingProcessTracking()
        {
            var running = true;
            var killed = 0;
            var p = new PollingOpenVpnProcess(77, () => running, () => { killed++; running = false; return true; }, 20);
            var raised = new ManualResetEventSlim(false);
            var count = 0;
            p.Exited += () => { Interlocked.Increment(ref count); raised.Set(); };
            p.StartWatching();
            Check(!p.HasExited && !p.WaitForExit(60), "polling process running");
            running = false;
            Check(raised.Wait(2000), "polling raises Exited when the PID disappears");
            Thread.Sleep(100);
            Check(count == 1 && p.HasExited, "polling raises Exited once");
            p.Dispose();

            var alive = true;
            var q = new PollingOpenVpnProcess(78, () => alive, () => { killed++; alive = false; return true; }, 20);
            Check(q.TryKill(1000) && killed == 1, "polling TryKill terminates and waits");
            q.Dispose();

            var errors = 0;
            var r = new PollingOpenVpnProcess(79, () => throw new InvalidOperationException("list"), null, 20) { Error = (m, e) => errors++ };
            var threw = false;
            try { Check(!r.HasExited && !r.TryKill(30), "polling check failure is treated as still running"); }
            catch (Exception) { threw = true; }
            Check(!threw && errors > 0, "polling check failure is reported, not thrown");
            r.Dispose();
        }

        /// <summary>Regression for 1.0.3: openvpn.exe started by the Interactive Service denies PROCESS_ALL_ACCESS to the user.</summary>
        static void ServicePathAvoidsProcessHandles(string root)
        {
            var services = Path.Combine(root, "src", "STGPhantomVPN", "Services");
            var ctl = File.ReadAllText(Path.Combine(services, "OpenVpnController.cs"));
            Check(!ctl.Contains("GetProcessById"), "controller does not open openvpn.exe through Process.GetProcessById");
            Check(!ctl.Contains("EnableRaisingEvents"), "controller does not use Process exit events");
            Check(ctl.Contains("OpenVpnProcesses.Attach(pid)"), "service path tracks openvpn.exe by PID");
            var connect = ctl.Substring(ctl.IndexOf("void Connect(", StringComparison.Ordinal));
            connect = connect.Substring(0, connect.IndexOf("static string BuildArguments", StringComparison.Ordinal));
            var mgmt = connect.IndexOf("ManagementLoop(", StringComparison.Ordinal);
            var watch = connect.IndexOf("StartWatching()", StringComparison.Ordinal);
            Check(mgmt > 0 && watch > mgmt, "management loop starts before exit monitoring");
            Check(connect.Contains("monitoring it failed"), "post-start failures are caught in Connect");
            var native = File.ReadAllText(Path.Combine(services, "OpenVpnProcesses.cs"));
            Check(native.Contains("SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION"), "PID tracking asks only for SYNCHRONIZE and limited query rights");
            Check(!native.Contains("PROCESS_ALL_ACCESS"), "no PROCESS_ALL_ACCESS in process tracking");
        }

        static void VersionIsConsistent(string root)
        {
            var v = AppVersion.Current;
            var src = Path.Combine(root, "src", "STGPhantomVPN");
            var csproj = File.ReadAllText(Path.Combine(src, "STGPhantomVPN.csproj"));
            Check(csproj.Contains("<Version>" + v + "</Version>"), "csproj Version is " + v);
            Check(csproj.Contains("<FileVersion>" + v + ".0</FileVersion>"), "csproj FileVersion is " + v + ".0");
            Check(csproj.Contains("<InformationalVersion>" + v + "</InformationalVersion>"), "csproj InformationalVersion is " + v);
            Check(File.ReadAllText(Path.Combine(src, "app.manifest")).Contains("version=\"" + v + ".0\""), "manifest version is " + v + ".0");
            Check(File.ReadAllText(Path.Combine(root, "build", "build.sh")).Contains("<Version>") && !Regex.IsMatch(File.ReadAllText(Path.Combine(root, "build", "build.sh")), "(?m)^VERSION=\"[0-9]"), "build.sh reads the version from the csproj");
            foreach (var readme in new[] { "README.md", "README.vi.md" })
            {
                var title = File.ReadAllLines(Path.Combine(root, readme)).FirstOrDefault(l => l.StartsWith("# "));
                Check(title != null && title.EndsWith(" " + v), readme + " title shows " + v);
                Check(File.ReadAllText(Path.Combine(root, readme)).Contains("STGPhantomVPN-Setup-" + v + ".exe"), readme + " names the " + v + " installer");
            }
            Check(File.ReadAllLines(Path.Combine(root, "licenses", "THIRD-PARTY-NOTICES.txt"))[0].Contains(" " + v + " "), "THIRD-PARTY-NOTICES title shows " + v);
            Check(File.ReadAllText(Path.Combine(root, "installer", "STGPhantomVPN.iss")).Contains("#define AppVersion \"" + v + "\""), "installer default AppVersion is " + v);
            Check(File.ReadAllText(Path.Combine(root, "README.md")).Contains("STGPhantomVPN-Setup-" + v + ".exe"), "README names the " + v + " installer");
            Check(File.ReadAllText(Path.Combine(root, "README.md")).Contains("STGPhantomVPN-Windows-" + v + ".zip"), "README names the " + v + " zip");
        }

        /// <summary>Portable copies must not hand UAC a user-writable exe when a protected install of the same version exists.</summary>
        static void HelperPlacementRules()
        {
            const string users = "S-1-5-32-545", authUsers = "S-1-5-11", someone = "S-1-5-21-1-2-3-1001";
            const int readExecute = 0x1200A9, modify = 0x1301BF, full = 0x1F01FF, appendData = 0x4, deleteChild = 0x40;
            Check(!AclRules.GrantsUnsafeRights(users, readExecute, false, AclRules.TargetMask), "Users read/execute is safe");
            Check(AclRules.GrantsUnsafeRights(users, modify, false, AclRules.TargetMask), "Users modify on app folder is unsafe");
            Check(AclRules.GrantsUnsafeRights(someone, full, false, AclRules.TargetMask), "user full control is unsafe");
            Check(AclRules.GrantsUnsafeRights(authUsers, unchecked((int)0x40000000), false, AclRules.TargetMask), "GENERIC_WRITE is unsafe");
            Check(!AclRules.GrantsUnsafeRights(authUsers, modify, true, AclRules.TargetMask), "inherit-only ACE does not apply to the object");
            Check(!AclRules.GrantsUnsafeRights("S-1-3-0", full, false, AclRules.TargetMask), "CREATOR OWNER placeholder ignored");
            Check(!AclRules.GrantsUnsafeRights(AclRules.Administrators, full, false, AclRules.TargetMask), "Administrators trusted");
            Check(!AclRules.GrantsUnsafeRights(AclRules.TrustedInstaller, full, false, AclRules.TargetMask), "TrustedInstaller trusted");
            Check(!AclRules.GrantsUnsafeRights(authUsers, appendData, false, AclRules.AncestorMask), "creating folders in C:\\ is fine for a parent");
            Check(AclRules.GrantsUnsafeRights(authUsers, appendData, false, AclRules.TargetMask), "creating files in the app folder is unsafe");
            Check(AclRules.GrantsUnsafeRights(users, deleteChild, false, AclRules.AncestorMask), "delete-child on a parent is unsafe (rename)");
            Check(AclRules.IsTrustedOwner(AclRules.LocalSystem) && !AclRules.IsTrustedOwner(someone), "owner trust");

            const string portable = @"C:\Users\a\Downloads\STG\STGPhantomVPN.exe", installed = @"C:\Program Files\STG Phantom VPN\STGPhantomVPN.exe";
            var good = new HelperCandidate { ExePath = installed, FileVersion = "1.0.4.0", IsProtected = true };
            var r = HelperPlacement.Choose(installed, "1.0.4.0", true, new[] { good });
            Check(r.Source == HelperSource.Self && r.ExePath == installed, "protected self is used");
            r = HelperPlacement.Choose(portable, "1.0.4.0", false, new[] { good });
            Check(r.Source == HelperSource.Installed && r.ExePath == installed, "portable copy elevates the installed copy");
            r = HelperPlacement.Choose(portable, "1.0.4.0", false, new[] { new HelperCandidate { ExePath = installed, FileVersion = "1.0.2.0", IsProtected = true } });
            Check(r.Source == HelperSource.UnprotectedSelf && r.ExePath == portable, "other installed version not used");
            r = HelperPlacement.Choose(portable, "1.0.4.0", false, new[] { new HelperCandidate { ExePath = installed, FileVersion = "1.0.4.0", IsProtected = false } });
            Check(r.Source == HelperSource.UnprotectedSelf, "unprotected install not used");
            r = HelperPlacement.Choose(portable, null, false, new[] { good });
            Check(r.Source == HelperSource.UnprotectedSelf, "unknown own version falls back to self");
            Check(HelperPlacement.Choose(portable, "1.0.4.0", false, null).Source == HelperSource.UnprotectedSelf, "no install: portable keeps working");
        }

        /// <summary>installer/STGPhantomVPN.iss must stay in step with the app and the build scripts.</summary>
        static void InstallerScript(string root)
        {
            var iss = File.ReadAllText(Path.Combine(root, "installer", "STGPhantomVPN.iss"));
            var appXaml = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "App.xaml.cs"));
            var helper = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Services", "ElevatedHelper.cs"));
            var build = File.ReadAllText(Path.Combine(root, "build", "build.sh"));
            var buildInstaller = File.ReadAllText(Path.Combine(root, "build", "build-installer.sh"));
            Check(iss.Contains("AppId={" + InstallInfo.AppId + "\r\n"), "installer AppId matches InstallInfo.AppId");
            Check(iss.Contains("AppMutex=" + InstallInfo.SingleInstanceMutex + "\r\n") && appXaml.Contains("InstallInfo.SingleInstanceMutex"), "installer AppMutex is the app's mutex");
            Check(iss.Contains("DefaultDirName={autopf}\\{#AppName}") && iss.Contains("#define AppName \"" + InstallInfo.DefaultFolderName + "\""), "installs to Program Files\\" + InstallInfo.DefaultFolderName);
            Check(iss.Contains("PrivilegesRequired=admin\r\n") && !iss.Contains("PrivilegesRequiredOverridesAllowed"), "admin-only install");
            Check(iss.Contains("MinVersion=6.1sp1\r\n"), "MinVersion 6.1sp1");
            Check(iss.Contains("528040"), ".NET Framework 4.8 release key checked");
            Check(iss.Contains("runasoriginaluser"), "post-install launch is unelevated");
            Check(!Regex.IsMatch(iss, @"(?im)^Source:.*\.msi"), "installer bundles no MSI");
            var verbs = Regex.Matches(iss, @"RunHelper\('([a-z-]+)'\)").Select(m => m.Groups[1].Value).ToList();
            Check(verbs.SequenceEqual(new[] { "remove-data" }), "uninstaller only runs the data clean-up verb");
            foreach (var verb in verbs)
                Check(helper.Contains("case \"" + verb + "\""), "helper verb " + verb + " exists");
            Check(build.Contains("build-installer.sh") && buildInstaller.Contains("STGPhantomVPN.iss"), "build.sh can call build-installer.sh");

            var languages = Regex.Matches(iss, @"(?m)^Name: ""(\w+)""; MessagesFile: ""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
            Check(languages.SequenceEqual(new[] { "english", "vietnamese", "chinesesimplified" }), "installer languages: " + string.Join(",", languages));
            foreach (Match m in Regex.Matches(iss, @"(?m)^Name: ""\w+""; MessagesFile: ""(Languages\\[^""]+)"""))
            {
                var file = Path.Combine(root, "installer", m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar));
                Check(File.Exists(file), "language file " + m.Groups[1].Value);
                if (!File.Exists(file)) continue;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    var hash = string.Concat(sha.ComputeHash(File.ReadAllBytes(file)).Select(b => b.ToString("x2")));
                    Check(buildInstaller.Contains("[" + Path.GetFileName(file) + "]=" + hash), Path.GetFileName(file) + " SHA-256 pinned in build-installer.sh");
                }
            }
            var keys = Regex.Matches(iss, @"(?m)^(\w+)\.(\w+)=").Select(m => (Lang: m.Groups[1].Value, Key: m.Groups[2].Value)).ToList();
            foreach (var key in keys.Select(k => k.Key).Distinct())
                foreach (var lang in languages)
                    Check(keys.Contains((lang, key)), "custom message " + key + " in " + lang);
            foreach (Match m in Regex.Matches(iss, @"CustomMessage\('(\w+)'\)"))
                Check(keys.Any(k => k.Key == m.Groups[1].Value), "custom message " + m.Groups[1].Value + " defined");
        }

        /// <summary>Every Strings.*.resx must have exactly the neutral file's keys and the same {n} placeholders.</summary>
        static void ResourceFilesMatch(string root)
        {
            var dir = Path.Combine(root, "src", "STGPhantomVPN", "Resources");
            Dictionary<string, string> Load(string f) => XDocument.Load(f).Root.Elements("data")
                .ToDictionary(e => (string)e.Attribute("name"), e => (string)e.Element("value") ?? "");
            var neutral = Load(Path.Combine(dir, "Strings.resx"));
            var files = Directory.GetFiles(dir, "Strings.*.resx");
            Check(files.Length >= 3, "translations present: " + files.Length);
            var csproj = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "STGPhantomVPN.csproj"));
            var loc = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Services", "Loc.cs"));
            var settings = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Views", "SettingsView.xaml"));
            var build = File.ReadAllText(Path.Combine(root, "build", "build.sh"));
            string Ph(string v) => string.Join(",", Regex.Matches(v, @"\{\d+(:[^}]*)?\}").Select(m => m.Value).OrderBy(x => x));
            foreach (var f in files)
            {
                var culture = Path.GetFileName(f).Split('.')[1];
                var t = Load(f);
                var missing = neutral.Keys.Except(t.Keys).ToList();
                var extra = t.Keys.Except(neutral.Keys).ToList();
                Check(missing.Count == 0 && extra.Count == 0, culture + ": same keys (missing " + string.Join(",", missing) + "; extra " + string.Join(",", extra) + ")");
                foreach (var k in neutral.Keys.Intersect(t.Keys))
                {
                    Check(Ph(neutral[k]) == Ph(t[k]), culture + ": placeholders match for " + k);
                    Check(t[k].Trim().Length > 0, culture + ": non-empty " + k);
                }
                Check(csproj.Contains("Resources\\Strings." + culture + ".resx\" LogicalName=\"STGPhantomVPN.Resources.Strings." + culture + ".resources\""), culture + ": csproj LogicalName entry");
                Check(Regex.IsMatch(csproj, "<SatelliteResourceLanguages>[^<]*\\b" + Regex.Escape(culture) + "\\b"), culture + ": listed in SatelliteResourceLanguages");
                Check(loc.Contains("\"" + culture + "\""), culture + ": in Loc.Supported");
                Check(settings.Contains("ConverterParameter=" + culture + "}"), culture + ": language chip in SettingsView");
                Check(build.Contains(culture), culture + ": satellite folder packaged by build.sh");
            }
        }

        /// <summary>1.0.5 is a release: no test-build wording in the app, installer, build scripts or file names.</summary>
        static void ReleaseHasNoTestLabels(string root)
        {
            var files = Directory.GetFiles(Path.Combine(root, "src", "STGPhantomVPN"), "*", SearchOption.AllDirectories)
                .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .Where(p => new[] { ".cs", ".xaml", ".resx", ".csproj", ".manifest" }.Contains(Path.GetExtension(p)))
                .Concat(new[] { Path.Combine(root, "installer", "STGPhantomVPN.iss"), Path.Combine(root, "build", "build.sh"), Path.Combine(root, "build", "build-installer.sh"),
                                Path.Combine(root, "README.md"), Path.Combine(root, "README.vi.md"), Path.Combine(root, "BUILDING.md"), Path.Combine(root, "BUILDING.vi.md"),
                                Path.Combine(root, "build", "build.ps1"), Path.Combine(root, "licenses", "THIRD-PARTY-NOTICES.txt") });
            var banned = new Regex(@"ban-thu|bản thử|BAN-THU|test build|test_build|测试版|កំណែសាកល្បង|\bChannel\b", RegexOptions.IgnoreCase);
            foreach (var file in files)
            {
                var hit = banned.Match(File.ReadAllText(file));
                Check(!hit.Success, "no test-build label in " + Path.GetFileName(file) + (hit.Success ? " (" + hit.Value + ")" : ""));
            }
            var iss = File.ReadAllText(Path.Combine(root, "installer", "STGPhantomVPN.iss"));
            Check(iss.Contains("OutputBaseFilename=STGPhantomVPN-Setup-{#AppVersion}\r\n"), "installer file name is STGPhantomVPN-Setup-<version>.exe");
            Check(File.ReadAllText(Path.Combine(root, "build", "build.sh")).Contains("NAME=\"STGPhantomVPN-Windows-${VERSION}${PKG_SUFFIX:-}\""), "zip name is STGPhantomVPN-Windows-<version>.zip");
            Check(File.ReadAllText(Path.Combine(root, "build", "build-installer.sh")).Contains("NAME=\"STGPhantomVPN-Setup-${VERSION}\""), "build-installer.sh expects STGPhantomVPN-Setup-<version>.exe");
        }

        static void TapPromptRules()
        {
            TapCheckResult R(TapStatus s) => new TapCheckResult { Status = s };
            var missing = R(TapStatus.Missing);
            var driverOnly = R(TapStatus.DriverOnly);
            var inactive = R(TapStatus.AdapterInactive);
            var ready = R(TapStatus.Ready);
            foreach (TapPromptTrigger t in Enum.GetValues(typeof(TapPromptTrigger)))
            {
                Check(!TapPromptPolicy.ShouldPrompt(ready, t, false) && !TapPromptPolicy.ShouldPrompt(ready, t, true), "ready adapter: never asks (" + t + ")");
                Check(!TapPromptPolicy.ShouldPrompt(null, t, false), "no check result: never asks (" + t + ")");
            }
            Check(TapPromptPolicy.ShouldPrompt(missing, TapPromptTrigger.Startup, false), "startup, missing, not declined: asks");
            Check(TapPromptPolicy.ShouldPrompt(driverOnly, TapPromptTrigger.Startup, false), "startup, driver without adapter: asks");
            Check(TapPromptPolicy.ShouldPrompt(inactive, TapPromptTrigger.Startup, false), "startup, inactive adapter: asks");
            Check(!TapPromptPolicy.ShouldPrompt(missing, TapPromptTrigger.Startup, true), "startup after Not now: does not ask");
            Check(!TapPromptPolicy.ShouldPrompt(inactive, TapPromptTrigger.Startup, true), "startup after Not now (inactive): does not ask");
            Check(TapPromptPolicy.ShouldPrompt(missing, TapPromptTrigger.Connect, true), "connect after Not now: asks again");
            Check(TapPromptPolicy.ShouldPrompt(driverOnly, TapPromptTrigger.Connect, true), "connect, driver without adapter: asks");
            Check(!TapPromptPolicy.ShouldPrompt(inactive, TapPromptTrigger.Connect, false), "connect with a registered adapter: left to OpenVPN");
            Check(TapPromptPolicy.ShouldPrompt(inactive, TapPromptTrigger.DriverError, true), "OpenVPN driver error: asks even after Not now");
            Check(TapPromptPolicy.ShouldPrompt(missing, TapPromptTrigger.AfterOpenVpnInstall, true), "right after installing OpenVPN: asks");
            Check(!TapPromptPolicy.DeclinedAfterCheck(ready, true), "Not now is cleared once TAP is ready");
            Check(TapPromptPolicy.DeclinedAfterCheck(missing, true) && TapPromptPolicy.DeclinedAfterCheck(null, true), "Not now kept while TAP is missing");
            Check(!TapPromptPolicy.DeclinedAfterCheck(missing, false), "nothing declined stays not declined");
        }

        static void CountryFlagRules(string root, string bundled)
        {
            var dir = Path.Combine(root, "src", "STGPhantomVPN", "Assets", "Flags");
            var files = Directory.GetFiles(dir, "*.png").Select(p => Path.GetFileNameWithoutExtension(p)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Check(files.Count >= 240, "flag set is complete: " + files.Count);
            Check(files.SequenceEqual(CountryFlags.Available.OrderBy(x => x, StringComparer.Ordinal)), "CountryFlags.Available matches Assets/Flags");
            foreach (var f in Directory.GetFiles(dir))
            {
                var bytes = File.ReadAllBytes(f);
                Check(Path.GetExtension(f) == ".png" && bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G', "flag is a PNG: " + Path.GetFileName(f));
            }
            Check(CountryFlags.ResourcePath("JP") == "Assets/Flags/jp.png", "JP flag");
            Check(CountryFlags.ResourcePath(" vn ") == "Assets/Flags/vn.png", "code trimmed and lower-cased");
            Check(CountryFlags.ResourcePath("UK") == "Assets/Flags/gb.png", "UK maps to the GB flag");
            foreach (var bad in new[] { null, "", "  ", "??", "—", "X", "USA", "1A", "zz", "XK1" })
                Check(CountryFlags.ResourcePath(bad) == null, "no flag (globe) for " + (bad ?? "null"));
            var codes = VpnServer.ParseCsv(File.ReadAllText(bundled)).Select(s => s.CountryCode).Distinct().ToList();
            foreach (var c in codes) Check(CountryFlags.ResourcePath(c) != null, "bundled server country has a flag: " + c);
            foreach (var c in new[] { "JP", "KR", "US", "TH", "RU", "VN", "GB", "DE", "FR", "CN", "TW", "HK", "IN", "ID", "PH", "SG", "BR", "CA", "AU", "MX" })
                Check(CountryFlags.ResourcePath(c) != null, "common VPNGate country has a flag: " + c);

            var csproj = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "STGPhantomVPN.csproj"));
            Check(csproj.Contains("<Resource Include=\"Assets\\Flags\\*.png\" />"), "flags embedded as WPF resources");
            var controls = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Views", "Controls.cs"));
            Check(controls.Contains("CountryFlags.ResourcePath") && controls.Contains("GlyphKind.Globe"), "CountryBadge shows the flag or a globe");
            var servers = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Views", "ServersView.xaml"));
            Check(servers.Contains("<views:CountryBadge DockPanel.Dock=\"Left\" Code=\"{Binding CountryCode}\" />"), "server cards show the country badge");
            Check(File.Exists(Path.Combine(root, "licenses", "flag-icons-LICENSE.txt")), "flag-icons licence shipped");
            Check(File.ReadAllText(Path.Combine(root, "licenses", "THIRD-PARTY-NOTICES.txt")).Contains("flag-icons"), "flag-icons in third-party notices");
        }

        static void WindowFitRules()
        {
            // Work areas in DIPs (screen / scale minus a 40-48 px taskbar) for common laptop and desktop screens.
            var areas = new[]
            {
                (Name: "1366x768 @100%", W: 1366.0, H: 728.0), (Name: "1366x768 @125%", W: 1092.8, H: 582.4), (Name: "1366x768 @150%", W: 910.7, H: 480.0),
                (Name: "1920x1080 @100%", W: 1920.0, H: 1032.0), (Name: "1920x1080 @125%", W: 1536.0, H: 825.6), (Name: "1920x1080 @150%", W: 1280.0, H: 688.0),
                (Name: "1280x720 @100%", W: 1280.0, H: 680.0), (Name: "1600x900 @125%", W: 1280.0, H: 680.0),
            };
            foreach (var a in areas)
            {
                var b = WindowFit.Compute(0, 0, a.W, a.H);
                Check(b.Width <= a.W && b.Height <= a.H, a.Name + ": window inside the work area");
                Check(b.Left >= 0 && b.Top >= 0 && b.Left + b.Width <= a.W && b.Top + b.Height <= a.H, a.Name + ": window placed inside the work area");
                Check(b.MinWidth <= b.Width && b.MinHeight <= b.Height, a.Name + ": minimum size does not exceed the window");
                Check(b.Height >= Math.Min(WindowFit.PreferredHeight, a.H - 2 * WindowFit.Gap) - 1, a.Name + ": window uses the available height");
                Check(WindowFit.DialogMaxHeight(b.Height - 31) <= b.Height - 31, a.Name + ": dialogs fit the client area");
            }
            var big = WindowFit.Compute(0, 0, 2560, 1400);
            Check(big.Width == WindowFit.PreferredWidth && big.Height == WindowFit.PreferredHeight, "large screen: preferred size");
            var offset = WindowFit.Compute(-1920, 40, 1920, 1040);
            Check(offset.Left >= -1920 && offset.Top >= 40, "work area offset honoured");
            var bad = WindowFit.Compute(0, 0, double.NaN, 0);
            Check(double.IsNaN(bad.Left) && bad.Width == WindowFit.PreferredWidth, "unknown work area: defaults");
        }

        /// <summary>Home must never need page scrolling; long pages scroll inside their own list or page.</summary>
        static void PagesFitWithoutScrolling(string root)
        {
            var views = Path.Combine(root, "src", "STGPhantomVPN", "Views");
            var home = File.ReadAllText(Path.Combine(views, "HomeView.xaml"));
            Check(!home.Contains("<ScrollViewer"), "Home has no page ScrollViewer");
            Check(home.Contains("<Viewbox x:Name=\"Fit\" Stretch=\"Uniform\""), "Home scales to fit instead of scrolling");
            Check(home.Contains("ConnectToggleCommand"), "Home keeps the connect button");
            var servers = File.ReadAllText(Path.Combine(views, "ServersView.xaml"));
            Check(!servers.Contains("<ScrollViewer VerticalScrollBarVisibility"), "Servers page scrolls only inside its list");
            Check(Regex.IsMatch(servers, "<RowDefinition Height=\"\\*\" />") && servers.Contains("VirtualizingPanel.IsVirtualizing=\"True\""), "server list fills the remaining height");
            var main = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "MainWindow.xaml"));
            Check(Regex.Matches(main, "MaxHeight=\"\\{DynamicResource DialogMaxHeight\\}\"").Count >= 2 && !main.Contains("MaxHeight=\"560\""), "dialogs are bounded by the window height");
            Check(File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "MainWindow.xaml.cs")).Contains("SystemParameters.WorkArea"), "window sized from the work area");
        }

        static void ArchitectureRules(string root)
        {
            Check(MachineArch.FromImageFileMachine(MachineArch.ImageFileMachineAmd64) == "amd64", "AMD64 machine: amd64 MSI");
            Check(MachineArch.FromImageFileMachine(MachineArch.ImageFileMachineArm64) == "arm64", "ARM64 machine: arm64 MSI");
            Check(MachineArch.FromImageFileMachine(MachineArch.ImageFileMachineI386) == "x86", "I386 machine: x86 MSI");
            Check(MachineArch.FromImageFileMachine(0) == null && MachineArch.FromImageFileMachine(0x01C4) == null, "unknown machine: fall back");
            foreach (var arch in new[] { "amd64", "arm64", "x86" })
            {
                Check(PinnedDownloads.OpenVpn(arch).FileName.Contains("-" + arch + ".msi"), "OpenVPN MSI for " + arch);
            }
            var csproj = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "STGPhantomVPN.csproj"));
            Check(csproj.Contains("<PlatformTarget>AnyCPU</PlatformTarget>") && csproj.Contains("<Prefer32Bit>false</Prefer32Bit>"), "exe is AnyCPU without Prefer32Bit");
            var iss = File.ReadAllText(Path.Combine(root, "installer", "STGPhantomVPN.iss"));
            Check(iss.Contains("ArchitecturesInstallIn64BitMode=x64compatible or arm64\r\n") && !iss.Contains("ArchitecturesAllowed="), "installer: all architectures, 64-bit mode on x64 and ARM64");
            var locator = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Services", "ToolLocator.cs"));
            Check(locator.Contains("IsWow64Process2") && locator.Contains("EntryPointNotFoundException"), "native architecture read with IsWow64Process2, with a Windows 7/8.1 fallback");
        }

        static void TrayRules(string root)
        {
            Check(TrayText.Tooltip("STG Phantom VPN", "Đã kết nối", "Japan") == "STG Phantom VPN\nĐã kết nối\nJapan", "tooltip: app, status, country");
            Check(TrayText.Tooltip("STG Phantom VPN", "Chưa kết nối", null) == "STG Phantom VPN\nChưa kết nối", "tooltip without a server");
            var longTip = TrayText.Tooltip("STG Phantom VPN", new string('x', 80), "Korea Republic of");
            Check(longTip.Length <= TrayText.MaxTooltipLength && longTip.EndsWith("…"), "tooltip kept within the NotifyIcon limit");
            Check(TrayText.Tooltip(null, null, null) == "", "tooltip with nothing known");
            Check(TrayText.ConnectAction(false, false) == TrayConnectAction.Connect, "disconnected: Connect");
            Check(TrayText.ConnectAction(true, false) == TrayConnectAction.Disconnect, "connected: Disconnect");
            Check(TrayText.ConnectAction(false, true) == TrayConnectAction.Busy && TrayText.ConnectAction(true, true) == TrayConnectAction.Busy, "connecting: item disabled");
            Check(TrayText.CloseHidesToTray(true, false, false), "close hides to the tray by default");
            Check(!TrayText.CloseHidesToTray(false, false, false), "close quits when the setting is off");
            Check(!TrayText.CloseHidesToTray(true, true, false), "tray Exit really closes");
            Check(!TrayText.CloseHidesToTray(true, false, true), "sign-out / shutdown really closes");
            Check(TrayText.ExitNeedsConfirmation(true, false) && TrayText.ExitNeedsConfirmation(false, true), "exit asks while connected or connecting");
            Check(!TrayText.ExitNeedsConfirmation(false, false), "exit does not ask while disconnected");

            var src = Path.Combine(root, "src", "STGPhantomVPN");
            var window = File.ReadAllText(Path.Combine(src, "MainWindow.xaml.cs"));
            var app = File.ReadAllText(Path.Combine(src, "App.xaml.cs"));
            var tray = File.ReadAllText(Path.Combine(src, "Services", "TrayIcon.cs"));
            var store = File.ReadAllText(Path.Combine(src, "Services", "SettingsStore.cs"));
            var vm = File.ReadAllText(Path.Combine(src, "ViewModels", "MainViewModel.cs"));
            var settings = File.ReadAllText(Path.Combine(src, "Views", "SettingsView.xaml"));
            var csproj = File.ReadAllText(Path.Combine(src, "STGPhantomVPN.csproj"));
            Check(window.Contains("TrayText.CloseHidesToTray(") && window.Contains("e.Cancel = true;") && window.Contains("Hide();"), "closing the window hides it");
            Check(window.Contains("_vm.TakeTrayHint()") && window.Contains("_tray.ShowHint()"), "one-time tray hint");
            Check(window.Contains("StopOpenVpnIfActiveAsync") && window.Contains("exit_confirm_body"), "tray Exit confirms and disconnects first");
            Check(window.Contains("_tray.Dispose()") && tray.Contains("_icon.Visible = false;"), "tray icon removed on exit");
            Check(app.Contains("SessionEnding += ") && window.Contains("public void OnSessionEnding()"), "sign-out / shutdown handled");
            Check(app.Contains("EventWaitHandle.OpenExisting(InstallInfo.ActivateEvent)") && app.Contains("new EventWaitHandle(false, EventResetMode.AutoReset, InstallInfo.ActivateEvent)"), "second launch activates the running window");
            Check(app.IndexOf("\"--elevated\"", StringComparison.Ordinal) < app.IndexOf("SingleInstanceMutex", StringComparison.Ordinal), "elevated helper runs before the single-instance check");
            Check(InstallInfo.ActivateEvent.StartsWith("Local\\"), "activation event is per session");
            Check(store.Contains("[DataMember] public bool CloseQuitsApp") && vm.Contains("get => !_settings.CloseQuitsApp;"), "minimize to tray defaults to on, also for older settings files");
            Check(store.Contains("[DataMember] public bool TrayHintShown"), "tray hint remembered");
            Check(settings.Contains("IsChecked=\"{Binding MinimizeToTray}\"") && settings.Contains("[minimize_to_tray]"), "Settings toggle for minimize to tray");
            Check(csproj.Contains("<Resource Include=\"Assets\\tray-connected.ico\" />") && File.Exists(Path.Combine(src, "Assets", "tray-connected.ico")), "connected tray icon embedded");
            Check(tray.Contains("\"Assets/tray-connected.ico\"") && tray.Contains("\"Assets/tray-idle.ico\""), "tray icon changes with the connection");

            var vi = XDocument.Load(Path.Combine(src, "Resources", "Strings.vi.resx")).Root.Elements("data").ToDictionary(e => (string)e.Attribute("name"), e => (string)e.Element("value"));
            Check(vi["tray_open"] == "Mở STG Phantom VPN" && vi["tray_connect"] == "Kết nối" && vi["tray_disconnect"] == "Ngắt kết nối" && vi["tray_exit"] == "Thoát", "Vietnamese tray menu");
            Check(vi["minimize_to_tray"] == "Thu nhỏ xuống khay hệ thống khi đóng", "Vietnamese tray setting");
        }

        /// <summary>1.0.6 is OpenVPN + VPNGate only: no WireGuard code, UI, strings or notices, and no author profile blurb.</summary>
        static void OpenVpnOnly(string root)
        {
            var src = Path.Combine(root, "src", "STGPhantomVPN");
            var files = Directory.GetFiles(src, "*", SearchOption.AllDirectories)
                .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .Where(p => new[] { ".cs", ".xaml", ".resx", ".csproj" }.Contains(Path.GetExtension(p)))
                .ToList();
            foreach (var file in files)
            {
                var text = File.ReadAllText(file);
                var wireGuardAllowed = file.EndsWith("ElevatedHelper.cs", StringComparison.Ordinal);
                Check(wireGuardAllowed || !Regex.IsMatch(text, @"wireguard|\bwg_|""wg-", RegexOptions.IgnoreCase), "no WireGuard in " + Path.GetFileName(file));
                Check(!Regex.IsMatch(text, BannedProfile, RegexOptions.IgnoreCase), "no author profile blurb in " + Path.GetFileName(file));
            }
            var helper = File.ReadAllText(Path.Combine(src, "Services", "ElevatedHelper.cs"));
            Check(!helper.Contains("installtunnelservice") && !helper.Contains("uninstalltunnelservice"), "helper never touches WireGuard tunnels");
            Check(helper.Contains("LegacyWireGuardFolder"), "data clean-up keeps tunnel configs from older versions");
            Check(!File.Exists(Path.Combine(src, "Views", "WireGuardView.xaml")) && !File.Exists(Path.Combine(src, "Core", "WireGuardConfig.cs")), "WireGuard sources removed");
            Check(!File.Exists(Path.Combine(root, "licenses", "WireGuard-for-Windows-COPYING.txt")), "WireGuard licence file removed");
            Check(!File.ReadAllText(Path.Combine(root, "licenses", "THIRD-PARTY-NOTICES.txt")).Contains("WireGuard"), "no WireGuard in third-party notices");
            var iss = File.ReadAllText(Path.Combine(root, "installer", "STGPhantomVPN.iss"));
            Check(!iss.Contains("wg-remove-all") && !iss.Contains("UninstallRemoveTunnels"), "uninstaller does not remove WireGuard tunnels");
            Check(Regex.IsMatch(iss, "(?m)^vietnamese.UninstallRemoveData=.*OpenVPN và WireGuard \\(nếu có\\) không bị gỡ"), "uninstaller says OpenVPN and WireGuard stay installed");
            foreach (var (name, text) in new[] { "README.md", "README.vi.md", "BUILDING.md", "BUILDING.vi.md" }.Select(n => (n, File.ReadAllText(Path.Combine(root, n)))).Append(("STGPhantomVPN.iss", iss)))
                Check(!Regex.IsMatch(text, BannedProfile, RegexOptions.IgnoreCase), "no author profile blurb in " + name);
            var main = File.ReadAllText(Path.Combine(src, "MainWindow.xaml"));
            Check(Regex.Matches(main, "Style=\"\\{StaticResource Fluent.NavItem\\}\"").Count == 3, "three nav items: Home, Servers, Settings");
            foreach (var t in new[] { "0", "1", "2" })
                Check(main.Contains("Converter={StaticResource TabIs}, ConverterParameter=" + t + "}"), "nav item and page " + t + " bound to SelectedTab");
        }

        static void CompactWindow(string root)
        {
            Check(WindowFit.PreferredWidth == 720 && WindowFit.PreferredHeight == 592, "compact default: 720 x 592 DIP (about 706 x 585 visible)");
            var fhd = WindowFit.Compute(0, 0, 1920, 1032);
            Check(fhd.Width == 720 && fhd.Height == 592 && fhd.Left == 600 && fhd.Top == 220, "1080p: compact window centred");
            var small = WindowFit.Compute(0, 0, 1024, 560);
            Check(small.Height == 544 && small.MinHeight <= small.Height && small.Top == 8, "short work area: window shrinks, stays above the taskbar");
            var tiny = WindowFit.Compute(0, 0, 640, 440);
            Check(tiny.Width == 624 && tiny.MinWidth == 624 && tiny.MinHeight == 424, "very small work area: minimum follows the window");
            var xaml = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "MainWindow.xaml"));
            Check(xaml.Contains("Width=\"720\" Height=\"592\""), "XAML default matches WindowFit");
            var home = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Views", "HomeView.xaml"));
            var homeCode = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Views", "HomeView.xaml.cs"));
            Check(homeCode.Contains("HomeLayout.MaxScale") && homeCode.Contains("HomeLayout.UseTwoColumns"), "Home scales down without limit and up to a cap");
        }

        static void BrandRules(string root)
        {
            var src = Path.Combine(root, "src", "STGPhantomVPN");
            foreach (GlyphKind k in Enum.GetValues(typeof(GlyphKind)))
            {
                Check(GlyphData.Parts.TryGetValue(k, out var parts) && parts.Length > 0, "glyph " + k + " has path data");
                if (parts == null) continue;
                foreach (var p in parts)
                {
                    Check(p.Length > 3 && "sfk".IndexOf(p[0]) >= 0 && p[1] == ' ' && p[2] == 'M', "glyph " + k + " part format");
                    Check(Regex.IsMatch(p.Substring(2), @"^[MLHVCAZ0-9 .\-]+$"), "glyph " + k + " uses absolute M/L/H/V/C/A/Z only");
                    var nums = Regex.Matches(p.Substring(2), @"-?\d+(\.\d+)?").Cast<Match>().Select(m => double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture));
                    Check(nums.All(n => n >= 0 && n <= GlyphData.Grid), "glyph " + k + " stays on the 24 grid");
                }
            }
            var xamls = Directory.GetFiles(src, "*.xaml", SearchOption.AllDirectories).Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));
            foreach (var x in xamls)
            {
                var text = File.ReadAllText(x);
                Check(!text.Contains("md:PackIcon"), "own glyphs instead of PackIcon in " + Path.GetFileName(x));
                foreach (Match m in Regex.Matches(text, "(?:views:Glyph[^>]*Kind|Property=\"Kind\" Value)=\"(\\w+)\""))
                    Check(Enum.TryParse<GlyphKind>(m.Groups[1].Value, out _), "glyph kind " + m.Groups[1].Value + " exists (" + Path.GetFileName(x) + ")");
            }
            Check(!File.ReadAllText(Path.Combine(src, "Views", "Controls.cs")).Contains("PackIcon"), "globe fallback is a Glyph");

            static int[] IcoSizes(string path)
            {
                var b = File.ReadAllBytes(path);
                int n = BitConverter.ToUInt16(b, 4);
                return Enumerable.Range(0, n).Select(i => b[6 + i * 16] == 0 ? 256 : b[6 + i * 16]).ToArray();
            }
            static bool IcoBmpBelow256(string path)
            {
                var b = File.ReadAllBytes(path);
                int n = BitConverter.ToUInt16(b, 4);
                for (int i = 0; i < n; i++)
                {
                    var size = b[6 + i * 16] == 0 ? 256 : b[6 + i * 16];
                    var off = BitConverter.ToInt32(b, 6 + i * 16 + 12);
                    var png = b[off] == 0x89 && b[off + 1] == (byte)'P';
                    if (png != (size == 256)) return false;
                }
                return true;
            }
            var assets = Path.Combine(src, "Assets");
            Check(IcoSizes(Path.Combine(assets, "app.ico")).SequenceEqual(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }), "app.ico: 16-256");
            Check(IcoBmpBelow256(Path.Combine(assets, "app.ico")), "app.ico: BMP entries, PNG only at 256");
            foreach (var t in new[] { "tray-idle.ico", "tray-connected.ico" })
            {
                Check(IcoSizes(Path.Combine(assets, t)).SequenceEqual(new[] { 16, 20, 24, 32 }), t + ": tray sizes");
                Check(IcoBmpBelow256(Path.Combine(assets, t)), t + ": BMP entries for System.Drawing.Icon");
            }
            Check(!File.ReadAllBytes(Path.Combine(assets, "tray-idle.ico")).SequenceEqual(File.ReadAllBytes(Path.Combine(assets, "tray-connected.ico"))), "tray icons differ by state");
            var csproj = File.ReadAllText(Path.Combine(src, "STGPhantomVPN.csproj"));
            foreach (var r in new[] { "Assets\\tray-idle.ico", "Assets\\brand-mark.png", "Assets\\app-icon.png", "Assets\\Dns\\*.png" })
                Check(csproj.Contains("<Resource Include=\"" + r + "\" />"), r + " embedded");
            Check(File.ReadAllText(Path.Combine(src, "MainWindow.xaml")).Contains("/Assets/app-icon.png"), "title bar shows the app icon");
            Check(File.ReadAllText(Path.Combine(src, "Views", "SettingsView.xaml")).Contains("/Assets/app-icon.png"), "About shows the app icon");
            var iss = File.ReadAllText(Path.Combine(root, "installer", "STGPhantomVPN.iss"));
            foreach (var m in Regex.Matches(iss, @"(?m)^Wizard(?:Small)?ImageFile=(.+)$").Cast<Match>().SelectMany(m => m.Groups[1].Value.Trim().Split(',')))
                Check(File.Exists(Path.Combine(root, "installer", m.Trim())), "installer image " + m.Trim() + " exists");
            Check(Regex.Matches(iss, @"(?m)^Wizard(?:Small)?ImageFile=").Count == 2, "installer uses the brand wizard images");
            var big = File.ReadAllBytes(Path.Combine(root, "installer", "wizard-image-100.bmp"));
            Check(big[0] == 'B' && big[1] == 'M' && BitConverter.ToInt32(big, 18) == 164 && BitConverter.ToInt32(big, 22) == 314, "wizard image 164 x 314 BMP");
            var small = File.ReadAllBytes(Path.Combine(root, "installer", "wizard-small-100.bmp"));
            Check(BitConverter.ToInt32(small, 18) == 55 && BitConverter.ToInt32(small, 22) == 55, "wizard small image 55 x 55");
            var generated = File.ReadAllText(Path.Combine(src, "Core", "Glyphs.cs"));
            Check(generated.StartsWith("// Generated by assets/brand/build_brand.py"), "glyph data generated from assets/brand");
            Check(File.Exists(Path.Combine(root, "assets", "brand", "svg", "logo.svg")), "brand source SVG kept");
        }

        static void Ipv6LeakRules(string root)
        {
            var o = TunnelPlan.Ipv6BlockOptions();
            Check(o.Contains("--block-ipv6"), "IPv6 packets are answered locally with no-route");
            Check(o.Contains("--ifconfig-ipv6 fd53:5447:5056::2/64 fd53:5447:5056::1"), "the adapter gets a unique local IPv6 address");
            foreach (var r in new[] { "::/3", "2000::/4", "3000::/4", "fc00::/7" })
                Check(o.Contains("--route-ipv6 " + r), "public IPv6 range routed into the tunnel: " + r);
            Check(!o.Any(x => x.Contains("redirect-gateway")), "the IPv6 block leaves IPv4 routing alone");
            Check(!o.Any(x => x.Contains("fe80") || x.Contains("ff00") || x.Contains("::/0")), "link-local and multicast keep their own routes");
            var ctl = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Services", "OpenVpnController.cs"));
            var build = ctl.Substring(ctl.IndexOf("static string BuildArguments", StringComparison.Ordinal));
            Check(build.Contains("if (options.BlockIpv6)") && build.Contains("TunnelPlan.Ipv6BlockOptions(ipv6Firewall)"), "both layers add the IPv6 block when enabled");
            Check(build.IndexOf("TunnelPlan.Ipv6BlockOptions(ipv6Firewall)", StringComparison.Ordinal) > build.IndexOf("TunnelPlan.Options(layer", StringComparison.Ordinal), "IPv6 block added for routed and bridged sessions alike");
            var settings = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Services", "SettingsStore.cs"));
            Check(settings.Contains("public bool Ipv6BlockOff"), "IPv6 block stored inverted so it defaults to on");
            var vm = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "ViewModels", "MainViewModel.cs"));
            Check(vm.Contains("BlockIpv6 = !_settings.Ipv6BlockOff"), "connect options carry the IPv6 block");
            Check(File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Views", "SettingsView.xaml")).Contains("IsChecked=\"{Binding BlockIpv6}\""), "Settings has the IPv6 leak toggle");
        }

        static void PublicIpRules(string root)
        {
            foreach (var u in PublicIp.V4Endpoints.Concat(PublicIp.V6Endpoints).Concat(PublicIp.LocationEndpoints("1.2.3.4")))
                Check(u.StartsWith("https://"), "IP check over HTTPS: " + u);
            Check(PublicIp.V6Endpoints[0].Contains("api6.ipify.org"), "IPv6 check starts with an IPv6-only service");
            Check(PublicIp.RequestTimeout <= TimeSpan.FromSeconds(6), "short IP check timeout");

            var ipify = PublicIp.Parse("{\"ip\":\"138.64.64.245\"}", IpFamily.V4);
            Check(ipify != null && ipify.Ip == "138.64.64.245" && !ipify.HasLocation, "ipify answer");
            var ipinfo = PublicIp.Parse("{\n  \"ip\": \"138.64.64.245\",\n  \"city\": \"Tokyo\",\n  \"region\": \"Tokyo\",\n  \"country\": \"JP\",\n  \"org\": \"AS4685 Asahi Net\",\n  \"timezone\": \"Asia/Tokyo\"\n}", IpFamily.V4);
            Check(ipinfo != null && ipinfo.CountryCode == "JP" && ipinfo.City == "Tokyo" && ipinfo.Isp == "Asahi Net" && ipinfo.Country == null, "ipinfo answer: code, city, ISP without AS number");
            var ifco = PublicIp.Parse("{\"ip\":\"2402:800:6398:1::5\",\"ip_decimal\":1,\"country\":\"Vietnam\",\"country_iso\":\"VN\",\"city\":\"Hanoi\",\"asn\":\"AS7552\",\"asn_org\":\"Viettel Group\"}", IpFamily.V6);
            Check(ifco != null && ifco.CountryCode == "VN" && ifco.Country == "Vietnam" && ifco.Isp == "Viettel Group" && ifco.Ip == "2402:800:6398:1::5", "ifconfig.co answer (IPv6)");
            Check(ifco.Details == "Hanoi, Vietnam · Viettel Group", "details line: " + ifco.Details);
            Check(PublicIp.Parse("{\"ip\":\"2402:800:6398:1::5\"}", IpFamily.V4) == null, "IPv6 answer rejected when IPv4 was asked");
            Check(PublicIp.Parse("{\"ip\":\"1.2.3.4\"}", IpFamily.V6) == null, "IPv4 answer rejected when IPv6 was asked");
            Check(PublicIp.Parse("{\"ip\":\"::ffff:1.2.3.4\"}", IpFamily.V6) == null, "IPv4-mapped address is not a real IPv6");
            Check(PublicIp.Parse("<html>blocked</html>", IpFamily.V4) == null && PublicIp.Parse(null, IpFamily.V4) == null && PublicIp.Parse("{\"ip\":\"1.2.3\"}", IpFamily.V4) == null, "garbage answers ignored");
            Check(PublicIp.Field("{\"org\":\"A \\\"B\\\" \\u0043\"}", "org") == "A \"B\" C", "JSON string escapes");
            Check(PublicIp.Normalize(" 2402:0800:6398:0001:0000:0000:0000:0005 ", IpFamily.V6) == "2402:800:6398:1::5", "IPv6 normalised");
            var merged = PublicIp.Merge(PublicIp.Parse("{\"ip\":\"1.2.3.4\"}", IpFamily.V4), PublicIp.Parse("{\"ip\":\"1.2.3.4\",\"country\":\"JP\"}", IpFamily.V4));
            Check(merged.CountryCode == "JP", "location merged from a second lookup");
            var other = PublicIp.Merge(PublicIp.Parse("{\"ip\":\"1.2.3.4\"}", IpFamily.V4), PublicIp.Parse("{\"ip\":\"5.6.7.8\",\"country\":\"JP\"}", IpFamily.V4));
            Check(other.CountryCode == null, "location of another address is not merged");

            var real4 = PublicIp.Parse("{\"ip\":\"113.161.1.1\",\"country\":\"VN\"}", IpFamily.V4);
            var vpn4 = PublicIp.Parse("{\"ip\":\"138.64.64.245\",\"country\":\"JP\"}", IpFamily.V4);
            var v6 = PublicIp.Parse("{\"ip\":\"2402:800:6398::1\"}", IpFamily.V6);
            var ok = PublicIp.Assess(real4, vpn4, null, "138.64.64.245");
            Check(!ok.HasProblem && ok.MatchesServer, "tunneled, matches the server");
            var leak = PublicIp.Assess(real4, vpn4, v6, "1.1.1.1");
            Check(leak.V6Leak && !leak.V4NotTunneled && !leak.MatchesServer && leak.HasProblem, "any IPv6 while connected is a leak");
            var notTunneled = PublicIp.Assess(real4, PublicIp.Parse("{\"ip\":\"113.161.1.1\"}", IpFamily.V4), null, "138.64.64.245");
            Check(notTunneled.V4NotTunneled && notTunneled.HasProblem, "VPN IPv4 equal to the real one is flagged");
            Check(!PublicIp.Assess(null, vpn4, null, null).HasProblem, "unknown real IP: no false alarm");

            var ipVm = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "ViewModels", "MainViewModel.Ip.cs"));
            Check(ipVm.Contains("CanMeasureReal") && ipVm.Contains("run != _realRun || !CanMeasureReal"), "real IP measured only without a tunnel, stale answers dropped");
            Check(ipVm.Contains("VpnIpDelay") && ipVm.Contains("_vpn.ConnectedSinceUtc != since"), "VPN IP measured after connecting, per connection");
            Check(ipVm.Contains("Task.Run(() => PublicIp.FetchAsync"), "IP checks run off the UI thread");
            var home = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Views", "HomeView.xaml"));
            foreach (var b in new[] { "RealIpText", "VpnIpText", "VpnIpv6Text", "IpStatusText", "CheckIpCommand", "CopyRealIpCommand", "CopyVpnIpCommand", "[ip_real]", "[ip_vpn]", "[ip_check]" })
                Check(home.Contains(b), "Home shows " + b);
            Check(home.IndexOf("CheckIpCommand", StringComparison.Ordinal) > home.IndexOf("x:Name=\"StatsCard\"", StringComparison.Ordinal), "IP rows folded into the statistics card");
        }

        static void LogCleanupRules(string root)
        {
            Check(LogCleanup.IsClearable("app.log") && LogCleanup.IsClearable("app.log.1") && LogCleanup.IsClearable("openvpn-20261105-090703-045.log") && LogCleanup.IsClearable("openvpn.log"), "app and OpenVPN logs are clearable");
            foreach (var n in new[] { "msiexec-openvpn.log", "settings.json", "app.log.2", "..\\app.log", "../app.log", "sub\\openvpn-1.log", "", null })
                Check(!LogCleanup.IsClearable(n), "never cleared: " + (n ?? "null"));
            var plan = LogCleanup.Plan(new[] { "app.log", "app.log.1", "openvpn-20261105-090703-045.log", "openvpn-20261105-100000-000.log", "msiexec-openvpn.log", "notes.txt" }, "openvpn-20261105-100000-000.log");
            Check(plan.Delete.Count == 3 && !plan.Delete.Contains("openvpn-20261105-100000-000.log") && !plan.Delete.Contains("msiexec-openvpn.log"), "cleanup deletes only log files, not the active one");
            Check(plan.Truncate.SequenceEqual(new[] { "openvpn-20261105-100000-000.log" }), "the active OpenVPN log is emptied instead");
            Check(LogCleanup.Plan(new[] { "app.log" }, null).Truncate.Count == 0, "nothing emptied when idle");
            var cleaner = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Services", "LogCleaner.cs"));
            Check(cleaner.Contains("FileAttributes.ReparsePoint") && cleaner.Contains("Path.GetDirectoryName(path), dir"), "cleaner skips links and paths outside the log folder");
            Check(cleaner.Contains("lock (Log.WriteGate)"), "cleaner does not race app.log writes");
            var viewer = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Views", "LogViewerWindow.xaml.cs"));
            Check(viewer.Contains("MessageBoxButton.YesNo") && viewer.Contains("MessageBoxResult.No)") && viewer.Contains("LoadFiles();"), "clearing asks first and refreshes the viewer");
            Check(File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Views", "LogViewerWindow.xaml")).Contains("[log_clear]"), "viewer has the clear button");
            Check(File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Views", "SettingsView.xaml")).Contains("ClearLogsCommand"), "Settings has the clear button");

            var dir = Path.Combine(Path.GetTempPath(), "stg-logclean-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                foreach (var n in new[] { "app.log", "openvpn-20261105-090703-045.log", "openvpn-20261105-100000-000.log", "msiexec-openvpn.log", "keep.txt" })
                    File.WriteAllText(Path.Combine(dir, n), "x");
                var names = Directory.GetFiles(dir).Select(Path.GetFileName);
                var p = LogCleanup.Plan(names, "openvpn-20261105-100000-000.log");
                foreach (var n in p.Delete) File.Delete(Path.Combine(dir, n));
                foreach (var n in p.Truncate) File.WriteAllText(Path.Combine(dir, n), "");
                var left = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(n => n).ToArray();
                Check(left.SequenceEqual(new[] { "keep.txt", "msiexec-openvpn.log", "openvpn-20261105-100000-000.log" }), "cleanup on disk keeps foreign files: " + string.Join(",", left));
                Check(new FileInfo(Path.Combine(dir, "openvpn-20261105-100000-000.log")).Length == 0, "active log emptied on disk");
            }
            finally { Directory.Delete(dir, true); }
        }

        static void ResponsiveLayoutRules(string root)
        {
            Check(!HomeLayout.UseTwoColumns(640, 520), "compact 720 x 592 window: one column");
            Check(HomeLayout.UseTwoColumns(950, 500), "maximized on a 1024 x 600 laptop: two columns");
            Check(HomeLayout.UseTwoColumns(1290, 680) && HomeLayout.UseTwoColumns(1850, 980), "maximized at 1366 x 768 and 1920 x 1080: two columns");
            Check(!HomeLayout.UseTwoColumns(1000, 1000), "tall page stays one column");
            Check(Math.Abs(HomeLayout.Scale(640, 520, 588, 640) - 520.0 / 640) < 1e-9, "compact: scaled down to fit the height");
            Check(HomeLayout.Scale(3000, 2000, 950, 400) == HomeLayout.MaxScale, "huge page: scale capped");
            Check(HomeLayout.Scale(0, 0, 100, 100) == 1 && HomeLayout.Scale(100, 100, 0, 0) == 1, "unknown sizes: no scaling");
            var list = Enumerable.Range(1, 7).Select(i => new VpnServer { HostName = "h" + i, Ip = "1.1.1." + i }).ToList();
            var rows = ServerPicker.Rows(list, 3);
            Check(rows.Count == 3 && rows[0].Count == 3 && rows[2].Count == 1 && rows[2][0].HostName == "h7", "server cards split into rows in order");
            Check(ServerPicker.Rows(list, 0).Count == 7 && ServerPicker.Rows(null, 2).Count == 0, "row split guards");
            Check(ServerPicker.ColumnsFor(600) == 1 && ServerPicker.ColumnsFor(1200) == 2 && ServerPicker.ColumnsFor(1800) == 3, "server columns follow the width");
            var servers = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Views", "ServersView.xaml"));
            Check(servers.Contains("Converter=\"{StaticResource ServerRows}\"") && servers.Contains("<UniformGrid Rows=\"1\" Columns=\"{Binding Columns, ElementName=Root}\""), "server list shows rows of cards");
        }

        static void TunnelRules(string root)
        {
            var udp = OvpnSanitizer.Sanitize("client\ndev tun\nproto udp\nremote 219.100.37.12 1195\n" + Ca);
            Check(udp.Proto == "udp" && udp.IsUdp && udp.RemoteHost == "219.100.37.12", "sanitizer reads proto and remote host (udp)");
            var tcp = OvpnSanitizer.Sanitize("client\ndev tun\nproto tcp\nremote 1.2.3.4 443\n" + Ca);
            Check(tcp.Proto == "tcp" && !tcp.IsUdp, "sanitizer reads proto (tcp)");
            var remoteOnly = OvpnSanitizer.Sanitize("client\ndev tun\nremote vpn.example.net 443 tcp-client\n" + Ca);
            Check(remoteOnly.Proto == "tcp-client" && !remoteOnly.IsUdp && remoteOnly.RemoteHost == "vpn.example.net", "proto falls back to the remote line");
            var none = OvpnSanitizer.Sanitize("client\ndev tun\nremote 1.2.3.4\n" + Ca);
            Check(none.Proto == null && none.IsUdp, "OpenVPN default protocol is UDP");

            var routedTcp = TunnelPlan.Options(TunnelLayer.Routed, false, "1.2.3.4");
            Check(routedTcp.Count == 0, "routed TCP session adds no options");
            var routedUdp = TunnelPlan.Options(TunnelLayer.Routed, true, "1.2.3.4");
            Check(routedUdp.SequenceEqual(new[] { "--explicit-exit-notify 1" }), "routed UDP session announces its exit");
            var bridged = TunnelPlan.Options(TunnelLayer.Bridged, false, "1.2.3.4");
            Check(bridged.Contains("--dev tap"), "bridged session uses the TAP layer");
            Check(bridged.Any(o => o.StartsWith("--route-delay 0 ")), "bridged session waits for the DHCP lease before routes");
            Check(bridged.Contains("--route 1.2.3.4 255.255.255.255 net_gateway"), "bridged session keeps the server route on the physical gateway");
            Check(bridged.Contains("--block-outside-dns"), "bridged session lowers the adapter metric");
            Check(!bridged.Contains("--explicit-exit-notify 1"), "explicit-exit-notify only for UDP");
            Check(!TunnelPlan.Options(TunnelLayer.Bridged, true, null).Any(o => o.StartsWith("--route ")), "no server route without an IPv4 address");
            Check(TunnelPlan.SupportsDnsOverride(TunnelLayer.Routed) && !TunnelPlan.SupportsDnsOverride(TunnelLayer.Bridged), "DNS override only on routed sessions");

            Check(TunnelPlan.ServerAddress("219.100.37.12", "9.9.9.9") == "219.100.37.12", "server address from the profile remote");
            Check(TunnelPlan.ServerAddress("vpn.example.net", "9.9.9.9") == "9.9.9.9", "server address from the list for a host name");
            Check(TunnelPlan.ServerAddress(null, "") == null, "no server address");
            foreach (var bad in new[] { "01.2.3.4", "1.2.3", "1.2.3.4.5", "256.1.1.1", "::1", "1.2.3.4 ", "a.b.c.d" })
                Check(!TunnelPlan.IsIPv4(bad) || bad.Trim() != bad && TunnelPlan.IsIPv4(bad.Trim()), "IPv4 literal check: " + bad);

            Check(TunnelPlan.StageOf(null) == ConnectStage.NotStarted, "stage: nothing yet");
            Check(TunnelPlan.StageOf("TCP_CONNECT") == ConnectStage.Network && TunnelPlan.StageOf("WAIT") == ConnectStage.Network &&
                  TunnelPlan.StageOf("RECONNECTING") == ConnectStage.Network, "stage: network");
            Check(TunnelPlan.StageOf("AUTH") == ConnectStage.Handshake && TunnelPlan.StageOf("GET_CONFIG") == ConnectStage.Handshake, "stage: handshake");
            Check(TunnelPlan.StageOf("ASSIGN_IP") == ConnectStage.Adapter && TunnelPlan.StageOf("ADD_ROUTES") == ConnectStage.Adapter, "stage: adapter");
            Check(TunnelPlan.IsServerRefusal("AUTH: Received control message: AUTH_FAILED") &&
                  TunnelPlan.IsServerRefusal("AUTH: Received control message: AUTH_FAILED,session full"), "AUTH_FAILED is a server refusal");
            Check(!TunnelPlan.IsServerRefusal("Initialization Sequence Completed") && !TunnelPlan.IsServerRefusal(null), "other lines are not refusals");

            var ctl = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "Services", "OpenVpnController.cs"));
            Check(ctl.Contains("TunnelPlan.Options(layer, sanitized.IsUdp, serverIp)"), "controller adds the layer options");
            Check(ctl.Contains("TunnelLayer.Bridged, session"), "a refused routed session is retried bridged in place");
            Check(ctl.Contains("retryOf != _session || _stopRequested"), "a disconnect cancels the bridged retry");
            Check(ctl.Contains("TunnelPlan.IsServerRefusal(message)") && ctl.Contains("VpnErrors.ServerRefused"), "AUTH_FAILED maps to the server refusal error");
            var vm = File.ReadAllText(Path.Combine(root, "src", "STGPhantomVPN", "ViewModels", "MainViewModel.cs"));
            Check(vm.Contains("void OnServerRefused()") && vm.Contains("if (_automaticAttempt || refused == null) return;"), "next server is tried once after a refusal");
            foreach (var key in new[] { "connect_timeout_message_network", "connect_timeout_message_handshake", "connect_timeout_message_adapter" })
                Check(vm.Contains("\"" + key + "\""), "timeout message per stage: " + key);
        }

        static void LogFileRules(string root)
        {
            var name = OpenVpnLogFiles.NewName(new DateTime(2026, 11, 5, 9, 7, 3, 45));
            Check(name == "openvpn-20261105-090703-045.log", "per-attempt log name: " + name);
            Check(OpenVpnLogFiles.IsOpenVpnLog(name) && OpenVpnLogFiles.IsOpenVpnLog("openvpn.log") && !OpenVpnLogFiles.IsOpenVpnLog("app.log") &&
                  !OpenVpnLogFiles.IsOpenVpnLog("msiexec-openvpn.log") && !OpenVpnLogFiles.IsOpenVpnLog("openvpn-x.txt"), "OpenVPN log names recognised");
            var names = new List<string> { "app.log", "app.log.1", "openvpn.log", "msiexec-openvpn.log" };
            for (var i = 1; i <= 7; i++) names.Add(OpenVpnLogFiles.NewName(new DateTime(2026, 11, i, 10, 0, 0)));
            var ordered = OpenVpnLogFiles.NewestFirst(names);
            Check(ordered.Count == 8 && ordered[0] == "openvpn-20261107-100000-000.log" && ordered.Last() == "openvpn.log", "newest log first, legacy last");
            var prune = OpenVpnLogFiles.ToPruneBeforeNew(names);
            Check(prune.Count == 4 && prune.Contains("openvpn.log") && prune.Contains("openvpn-20261101-100000-000.log") &&
                  !prune.Contains("openvpn-20261104-100000-000.log") && !prune.Contains("app.log"), "pruning keeps the newest four before a new attempt");
            Check(OpenVpnLogFiles.ToPruneBeforeNew(new[] { "openvpn-20261101-100000-000.log" }).Count == 0, "nothing pruned below the limit");
            Check(OpenVpnLogFiles.Keep == 5, "five OpenVPN logs are kept");

            var ui = Path.Combine(root, "src", "STGPhantomVPN");
            Check(File.Exists(Path.Combine(ui, "Views", "LogViewerWindow.xaml")), "log viewer window exists");
            var viewer = File.ReadAllText(Path.Combine(ui, "Views", "LogViewerWindow.xaml"));
            Check(viewer.Contains("[log_copy]") && viewer.Contains("[log_open_folder]") && viewer.Contains("IsReadOnly=\"True\""), "log viewer has copy and open-folder actions");
            var viewerCode = File.ReadAllText(Path.Combine(ui, "Views", "LogViewerWindow.xaml.cs"));
            Check(viewerCode.Contains("FileShare.ReadWrite"), "log viewer reads files that OpenVPN still writes");
            Check(File.ReadAllText(Path.Combine(ui, "Views", "SettingsView.xaml")).Contains("ShowLogsCommand"), "Settings opens the log viewer");
            Check(File.ReadAllText(Path.Combine(ui, "Views", "HomeView.xaml")).Contains("ShowLogsCommand"), "the Home error line links to the log viewer");
        }

        static void DnsRules(string root)
        {
            string n;
            foreach (var ok in new[] { "1.1.1.1", "8.8.4.4", "149.112.112.112", " 94.140.14.14 ", "223.255.255.1", "100.63.0.1", "172.32.0.1", "11.0.0.1" })
                Check(PublicDns.TryParseResolver(ok, out n) && n == ok.Trim(), "public resolver accepted: " + ok);
            foreach (var bad in new[] { null, "", "1.1.1", "1.1.1.1.1", "01.1.1.1", "1.1.1.256", "1.1.1.-1", "1.1.1.1a", "0x1.1.1.1", "1..1.1", "1.1.1.1/32",
                                        "0.0.0.0", "127.0.0.1", "10.0.0.1", "172.16.0.1", "172.31.255.255", "192.168.1.1", "169.254.1.1", "100.64.0.1",
                                        "224.0.0.1", "255.255.255.255", "240.0.0.1", "192.0.2.1", "198.51.100.1", "203.0.113.9", "198.18.0.1", "١.١.١.١", "1.1.1.1 8.8.8.8" })
                Check(!PublicDns.TryParseResolver(bad, out n), "refused as resolver: " + (bad ?? "null"));

            Check(PublicDns.Resolvers(PublicDns.ServerId, "1.1.1.1", null) == null && PublicDns.Resolvers(null, null, null) == null, "default: server DNS");
            Check(PublicDns.Resolvers("cloudflare", null, null).SequenceEqual(new[] { "1.1.1.1", "1.0.0.1" }), "Cloudflare");
            Check(PublicDns.Resolvers("google", null, null).SequenceEqual(new[] { "8.8.8.8", "8.8.4.4" }), "Google");
            Check(PublicDns.Resolvers("quad9", null, null).SequenceEqual(new[] { "9.9.9.9", "149.112.112.112" }), "Quad9");
            Check(PublicDns.Resolvers("opendns", null, null).SequenceEqual(new[] { "208.67.222.222", "208.67.220.220" }), "OpenDNS");
            Check(PublicDns.Resolvers("adguard", null, null).SequenceEqual(new[] { "94.140.14.14", "94.140.15.15" }), "AdGuard");
            Check(PublicDns.Resolvers("cloudflare-family", null, null).SequenceEqual(new[] { "1.1.1.3", "1.0.0.3" }), "Cloudflare for Families");
            Check(PublicDns.Resolvers("GOOGLE", null, null) != null, "provider id case-insensitive");
            Check(PublicDns.Resolvers("unknown", null, null) == null, "unknown provider: server DNS");
            Check(PublicDns.Resolvers("custom", "9.9.9.9", "").SequenceEqual(new[] { "9.9.9.9" }), "custom: primary only");
            Check(PublicDns.Resolvers("custom", "9.9.9.9", "1.1.1.1").SequenceEqual(new[] { "9.9.9.9", "1.1.1.1" }), "custom: primary and secondary");
            Check(PublicDns.Resolvers("custom", "9.9.9.9", "9.9.9.9").SequenceEqual(new[] { "9.9.9.9" }), "custom: duplicate dropped");
            Check(PublicDns.Resolvers("custom", "9.9.9.9", "nope").SequenceEqual(new[] { "9.9.9.9" }), "custom: bad secondary ignored");
            Check(PublicDns.Resolvers("custom", "192.168.1.1", "1.1.1.1") == null && PublicDns.Resolvers("custom", "", null) == null, "custom: bad primary falls back to server DNS");

            Check(PublicDns.OpenVpnOptions(null).Count == 0 && PublicDns.OpenVpnOptions(new string[0]).Count == 0, "server DNS: no extra options");
            var o = PublicDns.OpenVpnOptions(new[] { "1.1.1.1", "1.0.0.1" });
            Check(o.SequenceEqual(new[]
            {
                "--pull-filter ignore \"dhcp-option DNS\"", "--pull-filter ignore \"dns server\"",
                "--dhcp-option DNS 1.1.1.1", "--dhcp-option DNS 1.0.0.1",
                "--route 1.1.1.1 255.255.255.255 vpn_gateway", "--route 1.0.0.1 255.255.255.255 vpn_gateway",
                "--block-outside-dns",
            }), "public DNS: drop pushed DNS, set ours, route through the tunnel, block outside DNS");
            Check(PublicDns.Providers.Select(p => p.Id).Distinct().Count() == PublicDns.Providers.Count && PublicDns.Providers.All(p => p.Id != PublicDns.CustomId && p.Id != ""), "provider ids unique");
            foreach (var p in PublicDns.Providers)
            {
                Check(PublicDns.TryParseResolver(p.Primary, out _) && PublicDns.TryParseResolver(p.Secondary, out _), p.Name + " addresses valid");
                Check(File.Exists(Path.Combine(root, "src", "STGPhantomVPN", p.Logo.Replace('/', Path.DirectorySeparatorChar))), p.Name + " logo bundled");
            }

            var src = Path.Combine(root, "src", "STGPhantomVPN");
            var ctl = File.ReadAllText(Path.Combine(src, "Services", "OpenVpnController.cs"));
            Check(ctl.Contains("PublicDns.OpenVpnOptions(options.DnsServers)") && ctl.Contains("options.BlockOutsideDns && dns.Count == 0"), "DNS options on the OpenVPN command line, block-outside-dns once");
            var vm = File.ReadAllText(Path.Combine(src, "ViewModels", "MainViewModel.cs"));
            Check(vm.Contains("PublicDns.Resolvers(_settings.DnsProvider, _settings.CustomDns1, _settings.CustomDns2)"), "connect uses the chosen DNS");
            Check(vm.Contains("public bool ConnectionSettingsPending") && vm.Contains("ReconnectCommand = new AsyncCommand(ReconnectAsync"), "changed while connected: next connect, or reconnect now");
            var settings = File.ReadAllText(Path.Combine(src, "Views", "SettingsView.xaml"));
            Check(settings.Contains("ItemsSource=\"{Binding DnsChoices}\"") && settings.Contains("{Binding CustomDns1") && settings.Contains("{Binding ReconnectCommand}"), "Settings > Connection: DNS picker");
            var vi = XDocument.Load(Path.Combine(src, "Resources", "Strings.vi.resx")).Root.Elements("data").ToDictionary(e => (string)e.Attribute("name"), e => (string)e.Element("value"));
            Check(vi["dns_server_default"] == "DNS của máy chủ VPN (mặc định)" && vi["dns_custom"] == "Tùy chỉnh", "Vietnamese DNS picker labels");
            var notices = File.ReadAllText(Path.Combine(root, "licenses", "THIRD-PARTY-NOTICES.txt"));
            Check(notices.Contains("DNS provider logos") && notices.Contains("trademarks of their respective owners"), "DNS logos noted as owners' trademarks");
        }
    }
}
