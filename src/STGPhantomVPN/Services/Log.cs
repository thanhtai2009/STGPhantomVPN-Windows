using System;
using System.IO;
using System.Text;

namespace STGPhantomVPN.Services
{
    /// <summary>Small append-only diagnostic log in %LocalAppData%\STGPhantomVPN\logs.</summary>
    public static class Log
    {
        static readonly object Gate = new object();
        /// <summary>Held while writing or rotating; the log cleaner takes it so it never races a write.</summary>
        internal static object WriteGate => Gate;
        public const string FileName = "app.log";
        const long MaxBytes = 2 * 1024 * 1024;

        /// <summary>
        /// The elevated helper logs to the Administrators-only folder: rotating or appending to a file in a
        /// user-writable folder with admin rights can be redirected through planted links.
        /// </summary>
        public static string FilePath => Path.Combine(Elevation.IsElevated ? SecureDir.LogDir : AppPaths.LogDir, "app.log");

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message, Exception ex = null) =>
            Write("ERROR", ex == null ? message : message + ": " + ex.GetType().Name + ": " + ex.Message);

        static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    var path = FilePath;
                    if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    {
                        var old = path + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidOperationException) { }
            catch (System.Security.SecurityException) { }
        }
    }
}
