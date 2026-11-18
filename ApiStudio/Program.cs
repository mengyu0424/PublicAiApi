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
                StudioDialog.CreateMessage(null, "程序遇到异常，无法继续运行。\r\n\r\n" + ex.Message, "程序异常", false).ShowModal();
            }
        }
    }
}
