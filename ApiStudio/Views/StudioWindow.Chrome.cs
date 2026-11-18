using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;

namespace ApiStudio
{
    public partial class StudioWindow
    {
        private WindowState? styledWindowState;

        private void UpdateWindowChrome()
        {
            // Keep the last presented frame while minimized. Replacing it with
            // the normal-window frame forces a second layout when restored.
            if (WindowState == WindowState.Minimized || styledWindowState == WindowState) return;
            styledWindowState = WindowState;
            bool maximized = WindowState == WindowState.Maximized;
            WindowFrame.BorderThickness = new Thickness(maximized ? 0 : 1);
            WindowFrame.CornerRadius = new CornerRadius(maximized ? 0 : 10);
            TitleBar.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(10, 10, 0, 0);
            MaximizeIcon.Data = Geometry.Parse(maximized
                ? "M3.5,0.5 L10.5,0.5 L10.5,7.5 M0.5,3.5 L7.5,3.5 L7.5,10.5 L0.5,10.5 Z"
                : "M0.5,0.5 L9.5,0.5 L9.5,9.5 L0.5,9.5 Z");
            MaximizeButton.ToolTip = maximized ? "还原" : "最大化";
            AutomationProperties.SetName(MaximizeButton, maximized ? "还原窗口" : "最大化窗口");
        }
        private void UpdateCompositionBackground()
        {
            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source != null && !source.IsDisposed && source.CompositionTarget != null)
                // This is the clear color before WPF presents the retained scene.
                source.CompositionTarget.BackgroundColor = Theme.Canvas;
        }
        private void MinimizeClick(object sender, RoutedEventArgs e) { SystemCommands.MinimizeWindow(this); }
        private void MaximizeClick(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        }
        private void CloseWindowClick(object sender, RoutedEventArgs e) { Close(); }
    }
}
