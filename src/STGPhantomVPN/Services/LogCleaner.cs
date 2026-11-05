using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using STGPhantomVPN.Core;

namespace STGPhantomVPN.Services
{
    /// <summary>Deletes the app and OpenVPN logs in the log folders; files elsewhere are never touched.</summary>
    public static class LogCleaner
    {
        public sealed class Result
        {
            public int Deleted { get; set; }
            public int Emptied { get; set; }
            public int Failed { get; set; }
        }

        /// <param name="activeLogPath">OpenVPN log of a running connection, emptied instead of deleted; null when idle.</param>
        public static Result Clear(IEnumerable<string> folders, string activeLogPath)
        {
            var result = new Result();
            foreach (var dir in folders.Where(d => !string.IsNullOrEmpty(d)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(dir)) continue;
                string[] names;
                try { names = Directory.GetFiles(dir).Select(Path.GetFileName).ToArray(); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { result.Failed++; continue; }

                var inUse = activeLogPath != null && string.Equals(Path.GetDirectoryName(Path.GetFullPath(activeLogPath)), dir, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(activeLogPath) : null;
                var plan = LogCleanup.Plan(names, inUse);
                lock (Log.WriteGate)
                {
                    foreach (var name in plan.Delete)
                    {
                        var path = Inside(dir, name);
                        if (path == null) continue;
                        try { File.Delete(path); result.Deleted++; }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                        {
                            if (Empty(path)) result.Emptied++; else result.Failed++;
                        }
                    }
                }
                foreach (var name in plan.Truncate)
                {
                    var path = Inside(dir, name);
                    if (path == null) continue;
                    if (Empty(path)) result.Emptied++; else result.Failed++;
                }
            }
            Log.Info("Logs cleared: " + result.Deleted + " deleted, " + result.Emptied + " emptied, " + result.Failed + " failed");
            return result;
        }

        /// <summary>Full path of a plain file directly in the folder; null for links or anything that resolves elsewhere.</summary>
        static string Inside(string dir, string name)
        {
            if (!LogCleanup.IsClearable(name)) return null;
            var path = Path.GetFullPath(Path.Combine(dir, name));
            if (!string.Equals(Path.GetDirectoryName(path), dir, StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return null; }
            return path;
        }

        static bool Empty(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                    fs.SetLength(0);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return false; }
        }
    }
}
