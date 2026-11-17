using System;
using System.IO;
using System.Windows;

namespace ApiStudio
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                var app = new Application();
                app.Run(new StudioWindow());
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-error.log"), ex.ToString()); } catch { }
                System.Windows.MessageBox.Show("启动失败：\r\n" + ex.Message, "API Studio", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
