using System;
using System.IO;
using System.Net.Http;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Services
{
    /// <summary>
    /// Installs the official OpenVPN MSI. A copy shipped in the app's redist\ folder is used
    /// when present; otherwise the MSI is downloaded over HTTPS from the vendor. The file must match the
    /// pinned SHA-256 and carry a valid vendor signature; the elevated helper checks the hash again after
    /// copying it into an Administrators-only folder, then runs msiexec.
    /// </summary>
    public static class InstallerService
    {
        const string Publisher = "OpenVPN Inc.";

        /// <summary>The pinned OpenVPN MSI for the native architecture.</summary>
        public static PinnedDownload Pin() => PinnedDownloads.OpenVpn(ToolLocator.OsArch());

        /// <summary>Returns null on success, otherwise a resource key describing the failure.</summary>
        /// <remarks>Runs on the thread pool: hashing and signature/revocation checks must not block the UI thread.</remarks>
        public static Task<string> InstallAsync(IProgress<double> progress, CancellationToken ct) =>
            Task.Run(() => InstallCoreAsync(progress, ct), ct);

        static async Task<string> InstallCoreAsync(IProgress<double> progress, CancellationToken ct)
        {
            var d = Pin();
            var msi = await ObtainAsync(d, progress, ct).ConfigureAwait(false);
            if (msi == null) return "install_download_failed";
            if (!PinnedDownloads.Verify(msi, d) || !Authenticode.IsSignedBy(msi, Publisher))
            {
                Log.Error("Integrity check failed for " + msi);
                if (!msi.StartsWith(AppPaths.AppDir, StringComparison.OrdinalIgnoreCase)) SecureDir.DeleteQuietly(msi);
                return "install_hash_mismatch";
            }

            // The SID, not the name: with over-the-shoulder UAC the helper runs as a different (admin) account.
            string userSid;
            using (var id = WindowsIdentity.GetCurrent()) userSid = id.User?.Value ?? "";
            var code = await Elevation.RunHelperAsync("install-msi", msi, userSid).ConfigureAwait(false);
            switch (code)
            {
                case 0: return null;
                case -1: return "install_uac_declined";
                case ElevatedHelper.ExitHashMismatch: return "install_hash_mismatch";
                case ElevatedHelper.ExitNewerInstalled: return "install_openvpn_newer";
                default: return "install_msi_failed";
            }
        }

        static async Task<string> ObtainAsync(PinnedDownload d, IProgress<double> progress, CancellationToken ct)
        {
            var bundled = Path.Combine(AppPaths.AppDir, "redist", d.FileName);
            if (PinnedDownloads.Verify(bundled, d)) return bundled;

            var target = Path.Combine(AppPaths.DownloadDir, d.FileName);
            if (PinnedDownloads.Verify(target, d)) return target;

            var uri = new Uri(d.Url);
            if (uri.Scheme != Uri.UriSchemeHttps) return null;
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                using (var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode || resp.RequestMessage.RequestUri.Scheme != Uri.UriSchemeHttps) return null;
                    var total = resp.Content.Headers.ContentLength ?? 0;
                    using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var dst = File.Create(target))
                    {
                        var buf = new byte[81920];
                        long done = 0;
                        int n;
                        while ((n = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await dst.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
                            done += n;
                            if (total > 0) progress?.Report((double)done / total);
                        }
                    }
                }
                return target;
            }
            catch (Exception ex)
            {
                Log.Error("Download " + d.Url + " failed", ex);
                SecureDir.DeleteQuietly(target);
                return null;
            }
        }
    }
}
