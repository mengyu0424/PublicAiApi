using System;
using System.IO;
using System.Text;

namespace ApiStudio
{
    public enum AppThemeMode { System, Light, Dark }
    public sealed class ThemePreferenceStore
    {
        private sealed class Preference { public Preference() { } public string Theme { get; set; } }
        public string FilePath { get; private set; }
        public ThemePreferenceStore(string path) { FilePath = path; }
        public AppThemeMode Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return AppThemeMode.System;
                var value = JsonText.Deserialize<Preference>(File.ReadAllText(FilePath, Encoding.UTF8));
                AppThemeMode mode;
                return value != null && Enum.TryParse(value.Theme, out mode) && Enum.IsDefined(typeof(AppThemeMode), mode) ? mode : AppThemeMode.System;
            }
            catch (Exception) { return AppThemeMode.System; }
        }
        public void Save(AppThemeMode mode)
        {
            if (!Enum.IsDefined(typeof(AppThemeMode), mode)) throw new ArgumentOutOfRangeException("mode");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath)));
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonText.Serialize(new Preference { Theme = mode.ToString() }), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak"); else File.Move(temp, FilePath);
        }
    }
}
