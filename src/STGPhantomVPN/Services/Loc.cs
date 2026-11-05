using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace STGPhantomVPN.Services
{
    /// <summary>
    /// UI strings from Resources\Strings.resx (English, neutral) and Strings.&lt;culture&gt;.resx.
    /// XAML binds to the indexer, e.g. {Binding [nav_home], Source={x:Static svc:Loc.Instance}},
    /// so switching language refreshes every bound string immediately.
    /// </summary>
    public sealed class Loc : INotifyPropertyChanged
    {
        public static Loc Instance { get; } = new Loc();

        /// <summary>Languages offered in Settings. Add a Strings.&lt;code&gt;.resx and an entry here to add one.</summary>
        public static readonly string[] Supported = { "vi", "en", "zh-CN", "km" };

        readonly ResourceManager _rm = new ResourceManager("STGPhantomVPN.Resources.Strings", typeof(Loc).Assembly);
        CultureInfo _culture = new CultureInfo("vi");

        public event PropertyChangedEventHandler PropertyChanged;

        public string Language => _culture.Name;

        public string this[string key]
        {
            get
            {
                if (string.IsNullOrEmpty(key)) return "";
                return _rm.GetString(key, _culture) ?? key;
            }
        }

        public static string T(string key) => Instance[key];

        public static string F(string key, params object[] args)
        {
            try { return string.Format(Instance._culture, Instance[key], args); }
            catch (FormatException) { return Instance[key]; }
        }

        public void SetLanguage(string code)
        {
            if (Array.IndexOf(Supported, code) < 0) code = "vi";
            try { _culture = new CultureInfo(string.IsNullOrEmpty(code) ? "vi" : code); }
            catch (CultureNotFoundException) { _culture = new CultureInfo("vi"); }
            CultureInfo.DefaultThreadCurrentUICulture = _culture;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        }
    }
}
