using System;
using System.Windows.Media;
using Microsoft.Win32;

namespace ApiStudio
{
    internal static class Theme
    {
        private static Func<bool> systemThemeReader = ReadSystemDarkMode;
        public static AppThemeMode Mode { get; private set; }
        public static bool IsDark { get; private set; }
        public static void SetMode(AppThemeMode mode)
        {
            Mode = mode;
            IsDark = mode == AppThemeMode.Dark || mode == AppThemeMode.System && systemThemeReader();
        }
        public static void RefreshSystemTheme() { if (Mode == AppThemeMode.System) IsDark = systemThemeReader(); }
        private static bool ReadSystemDarkMode()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                { var value = key == null ? null : key.GetValue("AppsUseLightTheme"); return value is int && (int)value == 0; }
            }
            catch (Exception) { return false; }
        }
        private static Color Rgb(byte r, byte g, byte b) { return Color.FromRgb(r, g, b); }
        public static Color Ink { get { return IsDark ? Rgb(225, 232, 243) : Rgb(32, 44, 62); } }
        public static Color Muted { get { return IsDark ? Rgb(151, 166, 187) : Rgb(126, 138, 155); } }
        public static Color Error { get { return IsDark ? Rgb(244, 115, 124) : Rgb(213, 79, 81); } }
        public static Color Accent { get { return IsDark ? Rgb(78, 212, 189) : Rgb(0, 155, 137); } }
        public static Color Canvas { get { return IsDark ? Rgb(17, 23, 33) : Rgb(245, 247, 251); } }
        public static Color Surface { get { return IsDark ? Rgb(26, 34, 47) : Colors.White; } }
        public static Color Subtle { get { return IsDark ? Rgb(22, 30, 42) : Rgb(250, 251, 253); } }
        public static Color Border { get { return IsDark ? Rgb(48, 62, 80) : Rgb(227, 232, 239); } }
        public static Color Hover { get { return IsDark ? Rgb(29, 58, 61) : Rgb(232, 247, 243); } }
        public static Color Active { get { return IsDark ? Rgb(26, 65, 61) : Rgb(230, 246, 242); } }
        public static Color Grip { get { return IsDark ? Rgb(64, 81, 102) : Rgb(204, 214, 225); } }
        public static Color JsonKey { get { return IsDark ? Rgb(142, 181, 255) : Rgb(68, 104, 177); } }
        public static Color JsonString { get { return IsDark ? Rgb(116, 215, 189) : Rgb(0, 141, 111); } }
        public static Color JsonNumber { get { return IsDark ? Rgb(240, 184, 107) : Rgb(173, 105, 38); } }
        private static readonly Color[] LightBrackets = { Rgb(185,125,22), Rgb(133,72,193), Rgb(38,121,193), Rgb(207,76,117), Rgb(0,139,132), Rgb(112,117,38) };
        private static readonly Color[] DarkBrackets = { Rgb(239,202,107), Rgb(205,160,255), Rgb(124,198,255), Rgb(255,157,180), Rgb(101,220,203), Rgb(214,221,130) };
        public static Color BracketColor(int level) { var palette = IsDark ? DarkBrackets : LightBrackets; return palette[level % palette.Length]; }
        public static Color MethodColor(string method) { return method == "POST" ? Rgb(221,155,49) : method == "PUT" ? Rgb(98,153,240) : Rgb(30,183,148); }
        public static Color StatusColor(string status)
        {
            switch (ApiStatus.Normalize(status))
            {
                case "测试中": return Rgb(245,158,11);
                case "调试中": return Rgb(168,85,247);
                case "已完成": return Rgb(16,185,129);
                default: return Rgb(59,130,246);
            }
        }
    }
}
