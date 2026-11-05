using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace STGPhantomVPN.Services
{
    /// <summary>Checks that an executable carries a valid Authenticode signature from an expected publisher.</summary>
    public static class Authenticode
    {
        /// <summary>True when the file has a valid Authenticode signature whose subject O= equals <paramref name="organization"/>.</summary>
        public static bool IsSignedBy(string path, string organization)
        {
            if (!File.Exists(path)) return false;
            using (var fs = OpenLocked(path))
                return IsSignedBy(fs, path, organization);
        }

        /// <summary>
        /// Opens a file for reading while denying writers and rename/delete, so the bytes that were
        /// verified are the bytes that get executed while the stream stays open.
        /// </summary>
        public static FileStream OpenLocked(string path) =>
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        /// <summary>Verifies through an already open, write-locked handle (no path re-resolution between checks).</summary>
        public static bool IsSignedBy(FileStream locked, string path, string organization)
        {
            if (!VerifyTrust(path, locked.SafeFileHandle)) return false;
            try
            {
                using (var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                {
                    // Exact RDN match; a substring test would accept e.g. "CN=O=OpenVPN Inc., O=Someone Else".
                    var org = NameAttribute(cert, OidOrganization);
                    if (string.Equals(org, organization, StringComparison.Ordinal)) return true;
                    Log.Warn("Unexpected signer for " + path + ": " + cert.Subject);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Signer lookup failed for " + path, ex);
                return false;
            }
        }

        static bool VerifyTrust(string path, SafeFileHandle handle)
        {
            // Revocation is checked online first; when no CRL/OCSP answer can be obtained (offline PC)
            // the check is repeated without revocation, but an explicit "revoked" result is final.
            var rc = WinVerifyTrustFile(path, handle, WTD_REVOKE_WHOLECHAIN, WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT | WTD_DISABLE_MD2_MD4);
            if (rc == CERT_E_REVOCATION_FAILURE || rc == CRYPT_E_REVOCATION_OFFLINE || rc == CRYPT_E_NO_REVOCATION_CHECK)
            {
                Log.Warn("Revocation status unavailable for " + path + " (0x" + rc.ToString("X8") + "); verifying without it");
                rc = WinVerifyTrustFile(path, handle, WTD_REVOKE_NONE, WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_DISABLE_MD2_MD4);
            }
            if (rc != 0) Log.Warn("WinVerifyTrust " + path + " => 0x" + rc.ToString("X8"));
            return rc == 0;
        }

        static int WinVerifyTrustFile(string path, SafeFileHandle handle, uint revocation, uint provFlags)
        {
            var addRef = false;
            handle.DangerousAddRef(ref addRef);
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
                pcwszFilePath = path,
                hFile = handle.DangerousGetHandle(),
            };
            var pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            try
            {
                Marshal.StructureToPtr(fileInfo, pFile, false);
                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = revocation,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = pFile,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    dwProvFlags = provFlags,
                };
                var action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
                var invalid = new IntPtr(-1); // INVALID_HANDLE_VALUE: no UI parent, never interactive
                var rc = WinVerifyTrust(invalid, ref action, ref data);
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(invalid, ref action, ref data);
                return rc;
            }
            finally
            {
                Marshal.DestroyStructure(pFile, typeof(WINTRUST_FILE_INFO));
                Marshal.FreeHGlobal(pFile);
                if (addRef) handle.DangerousRelease();
            }
        }

        static string NameAttribute(X509Certificate2 cert, string oid)
        {
            var oidPtr = Marshal.StringToHGlobalAnsi(oid);
            try
            {
                var sb = new StringBuilder(512);
                var n = CertGetNameString(cert.Handle, CERT_NAME_ATTR_TYPE, 0, oidPtr, sb, sb.Capacity);
                return n > 1 ? sb.ToString() : null;
            }
            finally { Marshal.FreeHGlobal(oidPtr); }
        }

        const string OidOrganization = "2.5.4.10";
        const uint CERT_NAME_ATTR_TYPE = 3;

        const uint WTD_UI_NONE = 2;
        const uint WTD_REVOKE_NONE = 0;
        const uint WTD_REVOKE_WHOLECHAIN = 1;
        const uint WTD_CHOICE_FILE = 1;
        const uint WTD_STATEACTION_VERIFY = 1;
        const uint WTD_STATEACTION_CLOSE = 2;
        const uint WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT = 0x80;
        const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;
        const uint WTD_DISABLE_MD2_MD4 = 0x2000;
        const int CERT_E_REVOCATION_FAILURE = unchecked((int)0x800B010E);
        const int CRYPT_E_REVOCATION_OFFLINE = unchecked((int)0x80092013);
        const int CRYPT_E_NO_REVOCATION_CHECK = unchecked((int)0x80092012);
        static Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, EntryPoint = "CertGetNameStringW")]
        static extern int CertGetNameString(IntPtr pCertContext, uint dwType, uint dwFlags, IntPtr pvTypePara, StringBuilder pszNameString, int cchNameString);
    }
}
