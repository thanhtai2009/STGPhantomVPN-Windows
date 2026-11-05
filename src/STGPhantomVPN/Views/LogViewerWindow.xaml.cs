using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using STGPhantomVPN.Core;
using STGPhantomVPN.Services;
using STGPhantomVPN.ViewModels;

namespace STGPhantomVPN.Views
{
    /// <summary>Read-only view of the app log and the per-attempt OpenVPN logs.</summary>
    public partial class LogViewerWindow : Window
    {
        /// <summary>Only the end of large files is shown; the folder button opens the full file.</summary>
        const int MaxBytes = 512 * 1024;

        static LogViewerWindow _open;

        readonly string[] _folders;
        readonly Func<string> _activeLog;
        string _preferred;

        LogViewerWindow(IEnumerable<string> folders, string preferredFile, Func<string> activeLog)
        {
            InitializeComponent();
            SourceInitialized += (s, e) => Services.WindowBackdrop.ApplyFrameTheme(this);
            _activeLog = activeLog;
            _folders = folders.Where(f => !string.IsNullOrEmpty(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            _preferred = preferredFile;
            Loaded += (s, e) => LoadFiles();
            Closed += (s, e) => { if (_open == this) _open = null; };
        }

        /// <summary>Opens the viewer, or brings the open one to front and shows <paramref name="preferredFile"/>.</summary>
        /// <param name="activeLog">OpenVPN log of a running connection, or null; it is emptied instead of deleted.</param>
        public static void ShowSingle(IEnumerable<string> folders, string preferredFile, Func<string> activeLog)
        {
            if (_open != null)
            {
                _open._preferred = preferredFile;
                _open.LoadFiles();
                if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
                _open.Activate();
                return;
            }
            _open = new LogViewerWindow(folders, preferredFile, activeLog);
            var owner = Application.Current?.MainWindow;
            if (owner != null && owner.IsVisible) _open.Owner = owner;
            else _open.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _open.Show();
        }

        sealed class LogFile
        {
            public string Path { get; set; }
            public string Label { get; set; }
        }

        void LoadFiles()
        {
            var current = (Files.SelectedItem as LogFile)?.Path;
            var paths = new List<string>();
            foreach (var dir in _folders)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    var names = Directory.GetFiles(dir).Select(System.IO.Path.GetFileName).ToList();
                    paths.AddRange(OpenVpnLogFiles.NewestFirst(names).Select(n => System.IO.Path.Combine(dir, n)));
                    paths.AddRange(new[] { "app.log", "app.log.1" }.Where(names.Contains).Select(n => System.IO.Path.Combine(dir, n)));
                }
                catch (IOException ex) { Log.Warn("Listing logs in " + dir + " failed: " + ex.Message); }
                catch (UnauthorizedAccessException ex) { Log.Warn("Listing logs in " + dir + " failed: " + ex.Message); }
            }

            var items = paths.Distinct(StringComparer.OrdinalIgnoreCase).Select(p => new LogFile { Path = p, Label = LabelOf(p) }).ToList();
            Files.ItemsSource = items;
            if (items.Count == 0)
            {
                LogText.Text = "";
                Note.Text = Loc.T("log_empty");
                return;
            }
            var pick = items.FirstOrDefault(i => SamePath(i.Path, _preferred)) ?? items.FirstOrDefault(i => SamePath(i.Path, current)) ?? items[0];
            _preferred = null;
            if (Files.SelectedItem == pick) ShowFile(pick);
            else Files.SelectedItem = pick;
        }

        static bool SamePath(string a, string b) => a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        static string LabelOf(string path)
        {
            var name = System.IO.Path.GetFileName(path);
            var kind = OpenVpnLogFiles.IsOpenVpnLog(name) ? "OpenVPN" : Loc.T("log_kind_app");
            string time;
            try { time = File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }
            catch (Exception) { time = ""; }
            return kind + " · " + time + " · " + name;
        }

        void ShowFile(LogFile file)
        {
            if (file == null) return;
            try
            {
                using (var fs = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var truncated = fs.Length > MaxBytes;
                    if (truncated) fs.Seek(-MaxBytes, SeekOrigin.End);
                    using (var reader = new StreamReader(fs, new UTF8Encoding(false), true))
                    {
                        var text = reader.ReadToEnd();
                        if (truncated)
                        {
                            var firstBreak = text.IndexOf('\n');
                            if (firstBreak >= 0) text = text.Substring(firstBreak + 1);
                        }
                        LogText.Text = text;
                    }
                    Note.Text = truncated ? Loc.F("log_truncated", Formatters.FormatBytes(MaxBytes)) : file.Path;
                }
                LogText.ScrollToEnd();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                LogText.Text = "";
                Note.Text = Loc.F("log_read_failed", ex.Message);
            }
        }

        void OnFileChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => ShowFile(Files.SelectedItem as LogFile);

        void OnRefresh(object sender, RoutedEventArgs e) => LoadFiles();

        void OnCopy(object sender, RoutedEventArgs e)
        {
            var text = LogText.SelectionLength > 0 ? LogText.SelectedText : LogText.Text;
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                Clipboard.SetText(text);
                Note.Text = Loc.T("log_copied");
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException || ex is System.Runtime.InteropServices.ExternalException)
            {
                Note.Text = Loc.F("log_copy_failed", ex.Message);
            }
        }

        void OnClear(object sender, RoutedEventArgs e)
        {
            var message = ConfirmAndClear(this, _folders, _activeLog?.Invoke());
            if (message == null) return;
            LoadFiles();
            Note.Text = message;
        }

        /// <summary>Asks first, then clears the logs; returns the result line, or null when the user cancelled.</summary>
        public static string ConfirmAndClear(Window owner, IEnumerable<string> folders, string activeLog)
        {
            var text = Loc.T("log_clear_confirm") + (activeLog != null ? Environment.NewLine + Environment.NewLine + Loc.T("log_clear_active_note") : "");
            var answer = owner != null && owner.IsVisible
                ? MessageBox.Show(owner, text, Loc.T("log_clear_title"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                : MessageBox.Show(text, Loc.T("log_clear_title"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return null;
            var r = LogCleaner.Clear(folders, activeLog);
            var message = Loc.F("log_cleared", r.Deleted + r.Emptied);
            if (r.Failed > 0) message += " " + Loc.F("log_clear_failed", r.Failed);
            return message;
        }

        void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            var file = (Files.SelectedItem as LogFile)?.Path;
            if (file != null && File.Exists(file))
                MainViewModel.OpenShellArgs("explorer.exe", "/select," + CommandLine.Quote(file));
            else
                MainViewModel.OpenShell(_folders.FirstOrDefault(Directory.Exists) ?? AppPaths.LogDir);
        }

        void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
