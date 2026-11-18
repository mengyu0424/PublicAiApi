using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;

namespace ApiStudio
{
    internal static class StudioChrome
    {
        internal static void Attach(Window window, double captionHeight, bool resizable)
        {
            window.WindowStyle = WindowStyle.None;
            WindowChrome.SetWindowChrome(window, new WindowChrome
            {
                CaptionHeight = captionHeight, GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(resizable ? 6 : 0),
                CornerRadius = new CornerRadius(10), UseAeroCaptionButtons = false
            });
            if (resizable)
                window.SourceInitialized += delegate
                {
                    HwndSource.FromHwnd(new WindowInteropHelper(window).Handle).AddHook(
                        delegate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
                        { return ConstrainMaximizedBounds(window, hwnd, message, lParam, ref handled); });
                };
        }

        // Use the work area of the window's monitor, including taskbars on any edge.
        private static IntPtr ConstrainMaximizedBounds(Window window, IntPtr hwnd, int message, IntPtr lParam, ref bool handled)
        {
            if (message != 0x24) return IntPtr.Zero; // WM_GETMINMAXINFO
            var monitor = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor))
            {
                var bounds = (MinMaxInfo)Marshal.PtrToStructure(lParam, typeof(MinMaxInfo));
                bounds.MaxPosition.X = monitor.Work.Left - monitor.Monitor.Left;
                bounds.MaxPosition.Y = monitor.Work.Top - monitor.Monitor.Top;
                bounds.MaxSize.X = monitor.Work.Right - monitor.Work.Left;
                bounds.MaxSize.Y = monitor.Work.Bottom - monitor.Work.Top;
                var scale = HwndSource.FromHwnd(hwnd).CompositionTarget.TransformToDevice;
                bounds.MinTrackSize.X = Math.Max(bounds.MinTrackSize.X, (int)Math.Ceiling(window.MinWidth * scale.M11));
                bounds.MinTrackSize.Y = Math.Max(bounds.MinTrackSize.Y, (int)Math.Ceiling(window.MinHeight * scale.M22));
                Marshal.StructureToPtr(bounds, lParam, false);
                handled = true;
            }
            return IntPtr.Zero;
        }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public int Flags; }
        [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo
        {
            public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
        }
    }
}
