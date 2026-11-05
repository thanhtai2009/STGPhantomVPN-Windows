using System;
using System.Collections.Generic;

namespace STGPhantomVPN.Core
{
    /// <summary>Read-only view of the machine used by <see cref="Prerequisites"/>; replaced by a fake in tests.</summary>
    public interface ISystemProbe
    {
        /// <summary>True when the file exists in the native %SystemRoot%\System32 folder.</summary>
        bool SystemFileExists(string fileName);

        /// <summary>File version of a System32 file, or null when it is missing or unreadable.</summary>
        Version SystemFileVersion(string fileName);

        /// <summary>True when Windows reports the update as installed (servicing package or hotfix list).</summary>
        bool IsHotfixInstalled(string kb);

        /// <summary>True when the root is in the LocalMachine Trusted Root or Third-Party Root store.</summary>
        bool IsRootCertificatePresent(string sha1Thumbprint);

        /// <summary>True when policy turns off automatic root certificate download.</summary>
        bool RootAutoUpdateDisabled { get; }
    }

    /// <summary>A Windows update that OpenVPN 2.6 or this app needs on Windows 7 / 8.x.</summary>
    public sealed class RequiredUpdate
    {
        public string Kb { get; }
        /// <summary>Resource key of the one-line description shown in the dialog.</summary>
        public string DescriptionKey { get; }
        public string Url { get; }

        public RequiredUpdate(string kb, string descriptionKey, string url)
        {
            Kb = kb;
            DescriptionKey = descriptionKey;
            Url = url;
        }
    }

    public sealed class RootCertificate
    {
        public string Name { get; }
        public string Sha1Thumbprint { get; }

        public RootCertificate(string name, string sha1)
        {
            Name = name;
            Sha1Thumbprint = sha1;
        }
    }

    public sealed class PrerequisiteReport
    {
        public static readonly PrerequisiteReport None = new PrerequisiteReport(new List<RequiredUpdate>(), new List<RootCertificate>(), false);

        public IReadOnlyList<RequiredUpdate> MissingUpdates { get; }
        public IReadOnlyList<RootCertificate> MissingRoots { get; }
        public bool RootAutoUpdateDisabled { get; }

        public PrerequisiteReport(IReadOnlyList<RequiredUpdate> missingUpdates, IReadOnlyList<RootCertificate> missingRoots, bool rootAutoUpdateDisabled)
        {
            MissingUpdates = missingUpdates;
            MissingRoots = missingRoots;
            RootAutoUpdateDisabled = rootAutoUpdateDisabled;
        }

        /// <summary>Missing updates stop OpenVPN from installing or running; missing roots only produce a warning.</summary>
        public bool BlocksOpenVpn => MissingUpdates.Count > 0;
        public bool HasRootWarning => MissingRoots.Count > 0;
        public bool IsClean => !BlocksOpenVpn && !HasRootWarning;
    }

    /// <summary>
    /// Prerequisites for running the official OpenVPN 2.6 MSI on Windows 7 SP1 and 8.x. Windows 10+ needs
    /// none of them.
    /// </summary>
    public static class Prerequisites
    {
        /// <summary>Universal C Runtime; openvpn.exe and OpenSSL link against ucrtbase.dll.</summary>
        public static readonly RequiredUpdate UniversalCrt = new RequiredUpdate("KB2999226", "prereq_kb2999226",
            "https://www.catalog.update.microsoft.com/Search.aspx?q=KB2999226");

        /// <summary>SHA-2 code signing support; without it Windows 7 cannot load the SHA-2-only signed TAP-Windows6 driver.</summary>
        public static readonly RequiredUpdate Sha2CodeSigning = new RequiredUpdate("KB4474419", "prereq_kb4474419",
            "https://www.catalog.update.microsoft.com/Search.aspx?q=KB4474419");

        /// <summary>D3DCompiler_47.dll, required by WPF on .NET Framework 4.8 for Windows 7.</summary>
        public static readonly RequiredUpdate D3DCompiler = new RequiredUpdate("KB4019990", "prereq_kb4019990",
            "https://www.catalog.update.microsoft.com/Search.aspx?q=KB4019990");

        /// <summary>Root of the OpenVPN Inc. code-signing chain.</summary>
        public static readonly RootCertificate DigiCertTrustedRootG4 = new RootCertificate("DigiCert Trusted Root G4", "DDFB16CD4931C973A2037D3FC83A4D7D775D05E4");

        /// <summary>Root of the Let's Encrypt certificates used by the download and VPN Gate hosts.</summary>
        public static readonly RootCertificate IsrgRootX1 = new RootCertificate("ISRG Root X1", "CABD2A79A1076A31F21D253635CB039D4329A5E8");

        public const string UcrtFile = "ucrtbase.dll";
        public const string D3DCompilerFile = "d3dcompiler_47.dll";
        public const string KernelFile = "ntoskrnl.exe";

        /// <summary>
        /// Windows 7 kernel shipped by the first update released after Microsoft made Windows 7 updates
        /// SHA-2-only (KB4517297, August 2019; July 2019 builds were 6.1.7601.24498/24499). A kernel at or above
        /// this version can only have been installed on a system that already had SHA-2 code signing support,
        /// so it proves KB4474419 even when a later rollup or cleanup hides the KB from the update history.
        /// </summary>
        public static readonly Version Sha2EraKernel = new Version(6, 1, 7601, 24511);

        const string CatalogPrefix = "https://www.catalog.update.microsoft.com/";

        /// <summary>Only Microsoft Update Catalog links are ever handed to the shell.</summary>
        public static bool IsTrustedUpdateLink(string url) =>
            url != null && url.StartsWith(CatalogPrefix, StringComparison.Ordinal) && Uri.TryCreate(url, UriKind.Absolute, out var u) &&
            u.Scheme == Uri.UriSchemeHttps && u.Host == "www.catalog.update.microsoft.com";

        public static bool HasSha2CodeSigning(ISystemProbe probe)
        {
            if (probe.IsHotfixInstalled(Sha2CodeSigning.Kb)) return true;
            var kernel = probe.SystemFileVersion(KernelFile);
            return kernel != null && kernel.Major == 6 && kernel.Minor == 1 && kernel >= Sha2EraKernel;
        }

        public static PrerequisiteReport Evaluate(WindowsVersion os, ISystemProbe probe)
        {
            if (OsSupport.IsWindows10OrLater(os)) return PrerequisiteReport.None;

            var updates = new List<RequiredUpdate>();
            if (!probe.SystemFileExists(UcrtFile)) updates.Add(UniversalCrt);
            if (OsSupport.Family(os) == WindowsFamily.Windows7 || OsSupport.Family(os) == WindowsFamily.Older)
            {
                if (!HasSha2CodeSigning(probe)) updates.Add(Sha2CodeSigning);
                if (!probe.SystemFileExists(D3DCompilerFile)) updates.Add(D3DCompiler);
            }

            var roots = new List<RootCertificate>();
            foreach (var root in new[] { DigiCertTrustedRootG4, IsrgRootX1 })
                if (!probe.IsRootCertificatePresent(root.Sha1Thumbprint)) roots.Add(root);

            return new PrerequisiteReport(updates, roots, roots.Count > 0 && probe.RootAutoUpdateDisabled);
        }
    }
}
