using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using ApiStudio;

internal static class WpfSmokeTests
{
    private static int passed;

    [STAThread]
    private static int Main()
    {
        string root = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        string directory = Path.Combine(root, "wpf-scratch-" + Guid.NewGuid().ToString("N"));
        try
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            Check(typeof(StudioWindow).Assembly.GetCustomAttribute<TargetFrameworkAttribute>().FrameworkName == ".NETFramework,Version=v4.8",
                "the application targets .NET Framework 4.8");
            Check(Array.TrueForAll(typeof(StudioWindow).Assembly.GetReferencedAssemblies(),
                reference => reference.Name != "System.Windows.Forms" && reference.Name != "WindowsFormsIntegration" && reference.Name != "System.Drawing"),
                "the executable has no WinForms, integration host or GDI drawing assembly dependencies");
            Check(typeof(StudioWindow).Assembly.GetType("ApiStudio.MainForm") == null,
                "the old WinForms main window is absent from the executable");
            Run(directory, root);
            application.Shutdown();
            Console.WriteLine("All " + passed + " WPF checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(directory)), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    private static StudioWindow Open(string directory, bool maximized)
    {
        var window = (StudioWindow)Activator.CreateInstance(typeof(StudioWindow), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { directory }, null);
        Check(window.WindowState == WindowState.Maximized, "WPF window defaults to maximized");
        if (!maximized) window.WindowState = WindowState.Normal;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000; window.Top = -30000;
        window.ShowActivated = maximized; window.ShowInTaskbar = false;
        window.Show(); Pump();
        return window;
    }

    private static void Run(string directory, string output)
    {
        var store = new WorkspaceStore(Path.Combine(directory, "workspace.json"));
        var data = new Workspace();
        var folder = new ApiItem { IsFolder = true, Name = "测试集合" };
        data.Items.Add(folder);
        var sample = new ApiItem { Name = "B002-住院医嘱开立（描述）", Method = "POST", Status = "调试中", Url = "http://localhost/api?name=test",
            Body = "{\"message\":\"中文内容\"}", Response = "{\"success\":true}", StatusCode = 200, StatusText = "OK" };
        folder.Children.Add(sample);
        folder.Children.Add(new ApiItem { Name = "B003-住院医嘱作废", Url = "https://example.invalid/cancel" });
        store.Save(data);
        var preferences = new ThemePreferenceStore(Path.Combine(directory, "settings.json"));
        preferences.Save(AppThemeMode.Dark);
        File.WriteAllText(Path.Combine(directory, "sidebar-width-normal.txt"), "354");
        File.WriteAllText(Path.Combine(directory, "sidebar-width-maximized.txt"), "405");
        File.WriteAllText(Path.Combine(directory, "request-height-normal.txt"), "261");
        File.WriteAllText(Path.Combine(directory, "request-height-maximized.txt"), "294");

        var window = Open(directory, false);
        double normalWidth, normalHeight;
        try
        {
            var tree = Element<TreeView>(window, "CollectionTree");
            var title = Element<TextBox>(window, "RequestTitle");
            var address = Element<TextBox>(window, "AddressBox");
            var search = Element<TextBox>(window, "SearchBox");
            var sidebar = Element<ColumnDefinition>(window, "SidebarColumn");
            var requestRow = Element<RowDefinition>(window, "RequestRow");
            var host = Element<ContentControl>(window, "RequestHost");
            var json = (JsonEditor)host.Content;
            var editor = json.TextBox;
            Check(title.Text == sample.Name && Element<TextBlock>(window, "Breadcrumb").Text == folder.Name &&
                editor.Text == sample.Body && Element<TextBlock>(window, "ResponseInfo").Text == "200 OK",
                "WPF loads existing requests, paths and responses with its WPF JSON editor");
            Check(Element<Grid>(window, "EditorPage").IsVisible && editor.IsVisible && editor is TextBox,
                "the request editor is a visible native WPF control");
            Check(sidebar.Width.Value == 354 && requestRow.Height.Value == 261, "WPF restores existing normal-window splitter files");
            ChromeUi(window);
            search.Text = "cancel"; Pump();
            var filtered = (TreeViewItem)tree.Items[0];
            Check(filtered.Items.Count == 1 && ((ApiItem)((TreeViewItem)filtered.Items[0]).Tag).Name.Contains("作废"),
                "WPF tree search matches addresses and retains parent folders");
            search.Clear(); Pump();
            title.Text = sample.Name + " · 已编辑";
            Check(((TreeViewItem)((TreeViewItem)tree.Items[0]).Items[0]).ToolTip.ToString().StartsWith(title.Text),
                "inline title edits update the corresponding tree entry");
            title.Text = sample.Name;
            Element<ComboBox>(window, "RequestStatus").SelectedItem = "已完成";
            Check(((TreeViewItem)((TreeViewItem)tree.Items[0]).Items[0]).ToolTip.ToString().EndsWith("已完成"), "status changes update the collection");

            var requestTabs = Element<TabControl>(window, "RequestTabs");
            requestTabs.SelectedIndex = 2; Pump();
            Check(!host.IsVisible, "switching to Query hides the body editor");
            Element<TextBox>(window, "QueryBox").Text = "name=中文 值";
            Invoke(window, "ApplyQueryClick", null, null);
            Check(address.Text.Contains("name=%E4%B8%AD%E6%96%87%20%E5%80%BC"), "Query edits apply URL encoding");
            requestTabs.SelectedIndex = 0; Pump();
            Check(host.IsVisible && editor.Text == sample.Body, "switching back restores the same editor content");

            var theme = Element<Button>(window, "ThemeButton");
            theme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(theme.ContextMenu != null && theme.ContextMenu.IsOpen && theme.ContextMenu.Items.Count == 3,
                "the WPF theme icon opens three selectable options");
            ((MenuItem)theme.ContextMenu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            theme.ContextMenu.IsOpen = false; Pump();
            Check(preferences.Load() == AppThemeMode.Light && ((SolidColorBrush)window.Background).Color.R > 200,
                "theme selection updates the WPF palette and saves to the existing settings file");
            theme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            ((MenuItem)theme.ContextMenu.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            theme.ContextMenu.IsOpen = false; Pump();
            DialogUi(window, output);

            Drag(Element<GridSplitter>(window, "SidebarDivider"), 37, 0);
            Drag(Element<GridSplitter>(window, "PayloadDivider"), 0, 24);
            normalWidth = sidebar.Width.Value; normalHeight = requestRow.Height.Value;
            Check(Math.Abs(normalWidth - 391) <= 1.5 && Math.Abs(normalHeight - 285) <= 1.5,
                "both WPF dividers can be dragged and update their grid sizes");

            EditorUi(window, json);
            SendThroughWorkBench(window, store, address, editor);
            WebServiceUi(window, store, address, json);
            SavePreview(window, output);
        }
        finally { window.Close(); Pump(); }

        var reopened = Open(directory, true);
        try
        {
            var sidebar = Element<ColumnDefinition>(reopened, "SidebarColumn");
            var requestRow = Element<RowDefinition>(reopened, "RequestRow");
            Check(sidebar.Width.Value == 405 && requestRow.Height.Value == 294, "startup restores the saved maximized dimensions");
            Check(Element<ComboBox>(reopened, "RequestType").SelectedIndex == 1 &&
                Element<TextBlock>(reopened, "SoapActionHint").IsVisible &&
                Element<TextBlock>(reopened, "SoapActionHint").Text.Contains("http://tempuri.org/Add") &&
                !Element<ComboBox>(reopened, "RequestMethod").IsEnabled &&
                Element<TextBox>(reopened, "WsMethodBox").Text == "Add" &&
                Element<TextBox>(reopened, "WsNamespaceBox").Text == "http://tempuri.org/" &&
                ((StackPanel)reopened.FindName("WsRows")).Children.Count == 2,
                "reopening restores the WebService table fields, automatic Action and POST-only mode");
            reopened.WindowState = WindowState.Normal; Pump();
            Check(sidebar.Width.Value == Math.Round(normalWidth) && requestRow.Height.Value == Math.Round(normalHeight),
                "dragged positions persist independently when a new WPF window returns to normal mode");
        }
        finally { reopened.Close(); Pump(); }
    }

    private static void EditorUi(StudioWindow window, JsonEditor json)
    {
        var editor = json.TextBox;
        ScrollBarUi(window, json);
        EditorDeletionUi(json);
        ContinuousRenderingUi(json);
        PairedSymbolsUi(json);
        AutoIndentUi(json);
        TabIndentUi(json);
        SymbolInputUi(window, json);
        const string compact = "{\"中文\":\"含\\\"引号与{}[]\",\"number\":-1.2e+3,\"items\":[true,false,null,{\"nested\":[]}]}";
        json.Content = compact;
        Pump();
        var select = editor.GetType().GetMethod("SelectTokenAt", BindingFlags.Instance | BindingFlags.NonPublic);
        select.Invoke(editor, new object[] { compact.IndexOf("引号") });
        Check(editor.SelectedText == "含\\\"引号与{}[]", "double-click selection keeps the entire escaped Chinese string without its enclosing quotes");
        foreach (string scalar in new[] { "-1.2e+3", "true", "false", "null" })
        {
            select.Invoke(editor, new object[] { compact.IndexOf(scalar) + 1 });
            Check(editor.SelectedText == scalar, "double-click selects the complete JSON value: " + scalar);
        }
        editor.Select(editor.Text.Length, 0);
        editor.SelectedText = " ";
        string edited = editor.Text;
        int events = 0;
        TextChangedEventHandler changed = delegate { events++; };
        editor.TextChanged += changed;
        editor.Select(3, 2);
        json.Highlight(); Pump();
        Check(events == 0 && editor.SelectionStart == 3 && editor.SelectionLength == 2 && editor.CanUndo,
            "syntax highlighting preserves text, selection and the native WPF undo history");
        editor.TextChanged -= changed;
        editor.Undo(); Pump();
        Check(editor.Text == compact && editor.CanRedo, "undo removes only the user's edit after highlighting");
        editor.Redo(); Pump();
        Check(editor.Text == edited, "redo restores the user's exact edit");
        json.Content = edited;
        Check(!editor.CanUndo && !editor.CanRedo, "loading another request clears undo even when the body text is identical");
        json.Content = compact;
        Invoke(window, "FormatRequestClick", null, null); Pump();
        Check(editor.Text.Contains("\r\n") && JsonText.Compact(editor.Text) == compact, "WPF format action preserves JSON values and numeric spelling");
        editor.Undo(); Pump();
        Check(editor.Text == compact, "formatting can be undone as one edit");

        json.Content = JsonText.Format(compact); Pump(); json.Highlight(); Pump();
        var frame = new RenderTargetBitmap((int)Math.Ceiling(json.ActualWidth), (int)Math.Ceiling(json.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        frame.Render(json);
        var editorImage = new PngBitmapEncoder(); editorImage.Frames.Add(BitmapFrame.Create(frame));
        using (var stream = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wpf-editor.png"))) editorImage.Save(stream);
        Check(ContainsColor(frame, Color.FromRgb(142,181,255)) && ContainsColor(frame, Color.FromRgb(116,215,189)) &&
            ContainsColor(frame, Color.FromRgb(240,184,107)) && ContainsColor(frame, Color.FromRgb(239,202,107)),
            "native WPF rendering displays keys, strings, numbers and nested bracket colors");
        var response = (JsonEditor)Element<ContentControl>(window, "ResponseHost").Content;
        Check(response.TextBox.IsReadOnly && !editor.IsReadOnly, "response text is read-only while request text remains editable");
        json.Content = "{\n" + string.Join(",\n", Array.ConvertAll(new int[200], x => "  \"中文\": \"测试值\"")) + "\n}";
        editor.ScrollToLine(120); Pump();
        double offset = editor.VerticalOffset;
        editor.Select(editor.Text.IndexOf("测试值"), 2);
        json.Highlight(); Pump();
        Check(offset > 0 && Math.Abs(editor.VerticalOffset - offset) < .1,
            "highlighting leaves a scrolled JSON document at the same position");
        json.Content = "{\"long\":\"" + new string('x', 250000) + "\"}";
        Pump(); json.Highlight();
        editor.ScrollToHorizontalOffset(10000); Pump();
        Check(editor.Text.Length > 200000 && editor.HorizontalOffset > 0,
            "large single-line responses remain available for horizontal scrolling");
        json.Content = compact;
    }

    private static void ScrollBarUi(StudioWindow window, JsonEditor json)
    {
        string line = "  \"value\": \"" + new string('x', 360) + "\"";
        string body = "{\n" + string.Join(",\n", Array.ConvertAll(new int[120], x => line)) + "\n}";
        json.Content = body;
        var editor = json.TextBox;
        editor.Select(4, 3); Pump();
        var scroll = (ScrollViewer)editor.Template.FindName("PART_ContentHost", editor);
        var vertical = (ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar", scroll);
        var horizontal = (ScrollBar)scroll.Template.FindName("PART_HorizontalScrollBar", scroll);
        var verticalTrack = (Track)vertical.Template.FindName("PART_Track", vertical);
        var horizontalTrack = (Track)horizontal.Template.FindName("PART_Track", horizontal);
        Check(vertical.IsVisible && horizontal.IsVisible && vertical.ActualWidth == 12 && horizontal.ActualHeight == 12,
            "custom scrollbars use slim tracks (vertical=" + vertical.ActualWidth + ", horizontal=" + horizontal.ActualHeight + ")");
        Check(verticalTrack.Thumb.ActualHeight >= 24 && horizontalTrack.Thumb.ActualWidth >= 24,
            "large-document thumbs stay large enough to drag (vertical=" + verticalTrack.Thumb.ActualHeight + ", horizontal=" + horizontalTrack.Thumb.ActualWidth + ")");
        Check(vertical.Opacity == (scroll.IsMouseOver ? 1 : 0) && horizontal.Opacity == (scroll.IsMouseOver ? 1 : 0),
            "scrollbars are hidden outside the scroll region and revealed within it");
        double viewportWidth = scroll.ViewportWidth, viewportHeight = scroll.ViewportHeight;
        try
        {
            Check(verticalTrack.Thumb.CaptureMouse(), "the scrollbar thumb can capture a drag");
            Check(vertical.IsMouseCaptureWithin && vertical.Opacity == 1,
                "capturing a thumb keeps its scrollbar visible outside the hover region");
        }
        finally { if (Mouse.Captured == verticalTrack.Thumb) verticalTrack.Thumb.ReleaseMouseCapture(); }
        Pump();
        Check(vertical.Opacity == (scroll.IsMouseOver ? 1 : 0) && scroll.ViewportWidth == viewportWidth && scroll.ViewportHeight == viewportHeight,
            "releasing the scrollbar restores auto-hide without changing the content viewport");
        double before = scroll.VerticalOffset;
        Drag(verticalTrack.Thumb, 0, 35);
        Check(scroll.VerticalOffset > before && editor.Text == body && editor.SelectionStart == 4 && editor.SelectionLength == 3,
            "dragging the vertical thumb scrolls the editor without changing its selection");
        before = scroll.HorizontalOffset;
        Drag(horizontalTrack.Thumb, 45, 0);
        Check(scroll.HorizontalOffset > before, "dragging the horizontal thumb scrolls wide content in the correct direction");
        scroll.ScrollToHome(); Pump();
        scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.MouseWheelEvent }); Pump();
        Check(scroll.VerticalOffset > 0, "mouse-wheel scrolling works with the custom viewer template");
        scroll.ScrollToHome(); Pump();
        ((RoutedCommand)verticalTrack.IncreaseRepeatButton.Command).Execute(null, verticalTrack.IncreaseRepeatButton); Pump();
        Check(scroll.VerticalOffset > 0, "clicking the vertical track pages downward");
        ((RoutedCommand)horizontalTrack.IncreaseRepeatButton.Command).Execute(null, horizontalTrack.IncreaseRepeatButton); Pump();
        Check(scroll.HorizontalOffset > 0, "clicking the horizontal track pages right");
        var theme = Element<Button>(window, "ThemeButton");
        foreach (int mode in new[] { 1, 2 })
        {
            theme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            ((MenuItem)theme.ContextMenu.Items[mode]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            theme.ContextMenu.IsOpen = false; Pump();
            var fill = (Border)verticalTrack.Thumb.Template.FindName("ThumbFill", verticalTrack.Thumb);
            Check(((SolidColorBrush)fill.Background).Color == ((SolidColorBrush)window.FindResource("ScrollThumb")).Color,
                "scrollbar thumb colors follow live theme changes: " + mode);
            SaveElementPreview(Element<Grid>(window, "WindowRoot"), Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "wpf-scrollbars-" + (mode == 1 ? "light" : "dark") + ".png"));
        }
        json.Content = "{}"; Pump();
        Check(scroll.ComputedVerticalScrollBarVisibility == Visibility.Collapsed && scroll.ComputedHorizontalScrollBarVisibility == Visibility.Collapsed,
            "unneeded custom scrollbars disappear when the document becomes small");
        var tree = Element<TreeView>(window, "CollectionTree");
        int count = tree.Items.Count;
        try
        {
            for (int i = 0; i < 70; i++) tree.Items.Add(new TreeViewItem { Header = "滚动验证 " + i });
            Pump();
            var treeScroll = FindVisualChild<ScrollViewer>(tree);
            var treeBar = (ScrollBar)treeScroll.Template.FindName("PART_VerticalScrollBar", treeScroll);
            Check(treeBar.IsVisible && treeBar.ActualWidth == 12, "overflowing interface trees use the same custom scrollbar");
            treeScroll.ScrollToEnd(); Pump();
            double endOffset = treeScroll.VerticalOffset;
            treeScroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = Mouse.MouseWheelEvent }); Pump();
            Check(endOffset > 0 && treeScroll.VerticalOffset < endOffset, "the interface tree still scrolls by mouse wheel");
        }
        finally { while (tree.Items.Count > count) tree.Items.RemoveAt(count); Pump(); }
    }

    private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T) return (T)parent;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var found = FindVisualChild<T>(VisualTreeHelper.GetChild(parent, i));
            if (found != null) return found;
        }
        return null;
    }

    private static void ChromeUi(StudioWindow window)
    {
        var chrome = WindowChrome.GetWindowChrome(window);
        Check(window.WindowStyle == WindowStyle.None && !window.AllowsTransparency && chrome != null &&
            chrome.CaptionHeight == 48 && chrome.ResizeBorderThickness.Left == 6 && !chrome.UseAeroCaptionButtons,
            "the custom title bar keeps caption dragging and edge resizing without native caption buttons");
        Check(Element<Border>(window, "TitleBar").ActualHeight == 48 &&
            WindowChrome.GetIsHitTestVisibleInChrome((IInputElement)Element<Button>(window, "ThemeButton").Parent),
            "the custom title bar has its own theme and window control area");
        Check(HitTestWindow(window, new Point(350, 24)) == 2 && HitTestWindow(window, new Point(1, 200)) == 10,
            "the custom frame reports a draggable title area and a resizable left edge");
        var button = Element<Button>(window, "ThemeButton");
        Check(HitTestWindow(window, button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), window)) == 1,
            "title-bar controls receive client clicks instead of starting a window drag");
        Element<Button>(window, "MaximizeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(window.WindowState == WindowState.Maximized && Element<Button>(window, "MaximizeButton").ToolTip.ToString() == "还原" &&
            Element<Border>(window, "WindowFrame").BorderThickness.Left == 0,
            "the custom maximize button changes state, icon label and frame together");
        Check(window.ActualHeight <= SystemParameters.WorkArea.Height + 1 && window.ActualWidth <= SystemParameters.WorkArea.Width + 1,
            "maximizing the custom frame stays inside the monitor work area");
        MinimizeRestoreUi(window);
        Element<Button>(window, "MaximizeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(window.WindowState == WindowState.Normal && Element<Button>(window, "MaximizeButton").ToolTip.ToString() == "最大化",
            "the custom restore button returns to the normal window");
        Element<Button>(window, "MinimizeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(window.WindowState == WindowState.Minimized, "the custom minimize button minimizes the window");
        window.WindowState = WindowState.Normal; Pump();
    }

    private static void MinimizeRestoreUi(StudioWindow window)
    {
        var source = (HwndSource)PresentationSource.FromVisual(window);
        var theme = Element<Button>(window, "ThemeButton");
        foreach (int mode in new[] { 1, 2 })
        {
            theme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            ((MenuItem)theme.ContextMenu.Items[mode]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            theme.ContextMenu.IsOpen = false; Pump();
            Check(source.CompositionTarget.BackgroundColor == ((SolidColorBrush)window.Background).Color,
                "the native render surface clear color matches the theme before a restore: " + mode);
            foreach (WindowState state in new[] { WindowState.Maximized, WindowState.Normal })
            {
                window.WindowState = state; Pump();
                var frame = Element<Border>(window, "WindowFrame");
                var title = Element<Border>(window, "TitleBar");
                var glyph = Element<System.Windows.Shapes.Path>(window, "MaximizeIcon").Data;
                Thickness border = frame.BorderThickness;
                CornerRadius corners = title.CornerRadius;
                double width = Element<ColumnDefinition>(window, "SidebarColumn").Width.Value;
                double height = Element<RowDefinition>(window, "RequestRow").Height.Value;
                var editor = ((JsonEditor)Element<ContentControl>(window, "RequestHost").Content).TextBox;
                string body = editor.Text;
                editor.Select(2, 3);
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    Element<Button>(window, "MinimizeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                    Check(window.WindowState == WindowState.Minimized && frame.BorderThickness == border && title.CornerRadius == corners &&
                        object.ReferenceEquals(Element<System.Windows.Shapes.Path>(window, "MaximizeIcon").Data, glyph),
                        "minimizing preserves the last visible frame instead of rebuilding it: " + state + "/" + mode + "/" + cycle);
                    window.WindowState = state; Pump();
                    Check(frame.BorderThickness == border && title.CornerRadius == corners &&
                        object.ReferenceEquals(Element<System.Windows.Shapes.Path>(window, "MaximizeIcon").Data, glyph) &&
                        Math.Abs(Element<ColumnDefinition>(window, "SidebarColumn").Width.Value - width) < .1 &&
                        Math.Abs(Element<RowDefinition>(window, "RequestRow").Height.Value - height) < .1 &&
                        source.CompositionTarget.BackgroundColor == ((SolidColorBrush)window.Background).Color &&
                        editor.Text == body && editor.SelectionStart == 2 && editor.SelectionLength == 3,
                        "restoring reuses the frame and retains theme, splitters and editor selection: " + state + "/" + mode + "/" + cycle);
                }
                editor.Select(0, 0);
            }
        }
        window.WindowState = WindowState.Maximized; Pump();
    }

    private static void DialogUi(StudioWindow window, string output)
    {
        foreach (int mode in new[] { 1, 2 })
        {
            var theme = Element<Button>(window, "ThemeButton");
            theme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            ((MenuItem)theme.ContextMenu.Items[mode]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            theme.ContextMenu.IsOpen = false; Pump();
            SaveElementPreview(Element<Grid>(window, "WindowRoot"), Path.Combine(output, "wpf-workbench-" + (mode == 1 ? "light" : "dark") + ".png"));
            RespondToDialog(window, delegate { Invoke(window, "Warn", "请求体不是有效的 JSON。\r\n请检查第 28 行的逗号，然后重试。", "JSON 格式错误"); }, delegate(StudioDialog dialog)
            {
                Check(Window.GetWindow(dialog) == window && PresentationSource.FromVisual(dialog) == PresentationSource.FromVisual(window),
                    "warnings render inside the application's existing window surface");
                Check(KeyboardNavigation.GetTabNavigation(dialog) == KeyboardNavigationMode.Cycle &&
                    KeyboardNavigation.GetControlTabNavigation(dialog) == KeyboardNavigationMode.Cycle,
                    "dialog keyboard navigation stays inside the modal card");
                Check(Element<Button>(dialog, "DialogCancelButton").Visibility == Visibility.Collapsed &&
                    Element<Button>(dialog, "DialogConfirmButton").Content.ToString() == "知道了" &&
                    Element<TextBlock>(dialog, "DialogTitle").Text == "JSON 格式错误",
                    "the warning dialog presents the title and a single acknowledgement action");
                Check(((SolidColorBrush)dialog.FindResource("Surface")).Color.R == (mode == 1 ? 255 : 26),
                    "custom dialogs follow the current application theme: " + mode);
                if (mode == 1) DragDialogUi(window, dialog);
                SaveElementPreview((FrameworkElement)dialog.Content, Path.Combine(output, "wpf-dialog-" + (mode == 1 ? "light" : "dark") + ".png"));
                if (mode == 2) SaveElementPreview(Element<Grid>(window, "WindowRoot"), Path.Combine(output, "wpf-modal-overlay.png"));
                if (mode == 1)
                {
                    bool clicked = false;
                    var confirm = Element<Button>(dialog, "DialogConfirmButton");
                    confirm.Focus();
                    dialog.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    Check(dialog.IsKeyboardFocusWithin, "Tab from the last dialog button cycles within the modal card");
                    confirm.Focus();
                    confirm.Click += delegate { clicked = true; };
                    AccessKeyManager.ProcessKey(PresentationSource.FromVisual(dialog), "\r", false);
                    Check(clicked, "Enter activates the acknowledgement button inside an embedded dialog");
                }
                else dialog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog), Environment.TickCount, Key.Escape)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            });
        }
        var tree = Element<TreeView>(window, "CollectionTree");
        ((TreeViewItem)tree.SelectedItem).IsSelected = false;
        RespondToDialog(window, delegate { Invoke(window, "AddFolderClick", null, null); }, delegate(StudioDialog dialog)
        {
            var panel = (StackPanel)Element<ContentControl>(dialog, "DialogBody").Content;
            TextBox input = null;
            foreach (var child in panel.Children) if (child is TextBox) { input = (TextBox)child; break; }
            Check(input != null && input.SelectedText == "新建文件夹", "name entry uses the shared custom dialog and selects the initial name");
            input.Text = " ";
            Check(!Element<Button>(dialog, "DialogConfirmButton").IsEnabled, "custom name dialogs reject blank names");
            input.Text = "弹窗验证目录";
            SaveElementPreview((FrameworkElement)dialog.Content, Path.Combine(output, "wpf-dialog-name.png"));
            Element<Button>(dialog, "DialogConfirmButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        var added = (ApiItem)((TreeViewItem)tree.Items[1]).Tag;
        Check(added.IsFolder && added.Name == "弹窗验证目录", "confirming the custom name dialog creates the requested folder");
        RespondToDialog(window, delegate { Invoke(window, "DeleteItem", added); }, delegate(StudioDialog dialog)
        {
            Check(Element<Button>(dialog, "DialogCancelButton").IsDefault && !Element<Button>(dialog, "DialogConfirmButton").IsDefault &&
                Element<Button>(dialog, "DialogConfirmButton").Content.ToString() == "删除",
                "deletion confirmation defaults to Cancel and labels its destructive action explicitly");
            SaveElementPreview((FrameworkElement)dialog.Content, Path.Combine(output, "wpf-dialog-delete.png"));
            bool cancelled = false;
            Element<Button>(dialog, "DialogCancelButton").Click += delegate { cancelled = true; };
            AccessKeyManager.ProcessKey(PresentationSource.FromVisual(dialog), "\r", false);
            Check(cancelled, "Enter in a deletion dialog cancels rather than deleting");
        });
        Check(tree.Items.Count == 2, "closing deletion confirmation preserves the folder");
        RespondToDialog(window, delegate { Invoke(window, "DeleteItem", added); }, delegate(StudioDialog dialog)
        {
            Element<Button>(dialog, "DialogConfirmButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Check(tree.Items.Count == 1, "confirming deletion in the custom dialog removes the folder");
        RespondToDialog(window, delegate { Invoke(window, "Warn", "窗口关闭时先退出当前弹层。", "提示"); }, delegate(StudioDialog dialog)
        {
            window.Close();
        });
        Check(window.IsVisible, "closing the window during a modal card dismisses the card without closing the workspace");
        StandaloneDialogUi();
    }

    private static void DragDialogUi(StudioWindow owner, StudioDialog dialog)
    {
        var root = Element<Grid>(owner, "WindowRoot");
        var handle = Element<Thumb>(dialog, "DialogDragHandle");
        Point before = dialog.TranslatePoint(new Point(), root);
        Drag(handle, 75, 35);
        Point after = dialog.TranslatePoint(new Point(), root);
        Check(Math.Abs(after.X - before.X - 75) < .1 && Math.Abs(after.Y - before.Y - 35) < .1 &&
            PresentationSource.FromVisual(dialog) == PresentationSource.FromVisual(owner),
            "dragging a message header moves the card within the existing window surface");
        Drag(handle, 100000, 100000);
        after = dialog.TranslatePoint(new Point(), root);
        Check(after.X + dialog.ActualWidth <= root.ActualWidth - 16 + .1 && after.Y + dialog.ActualHeight <= root.ActualHeight - 16 + .1,
            "dragging a message keeps its right and bottom edges inside the workspace");
        Drag(handle, -200000, -200000);
        after = dialog.TranslatePoint(new Point(), root);
        Check(after.X >= 16 - .1 && after.Y >= 16 - .1, "dragging a message keeps its top and left edges reachable");
        var offset = (TranslateTransform)dialog.RenderTransform;
        Drag(handle, -offset.X, -offset.Y);
    }

    private static void RespondToDialog(StudioWindow owner, Action show, Action<StudioDialog> respond)
    {
        owner.Activate(); Pump();
        Exception failure = null;
        bool observed = false;
        int windows = Application.Current.Windows.Count, activationChanges = 0, enableChanges = 0;
        var source = (HwndSource)PresentationSource.FromVisual(owner);
        uint ownerProcess;
        GetWindowThreadProcessId(source.Handle, out ownerProcess);
        bool externalActivation = false;
        HwndSourceHook hook = delegate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x6 || message == 0x86) activationChanges++; // WM_ACTIVATE / WM_NCACTIVATE
            if (message == 0x6 && lParam != IntPtr.Zero)
            {
                uint otherProcess;
                GetWindowThreadProcessId(lParam, out otherProcess);
                if (otherProcess != 0 && otherProcess != ownerProcess) externalActivation = true;
            }
            if (message == 0xA) enableChanges++; // WM_ENABLE
            return IntPtr.Zero;
        };
        source.AddHook(hook);
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(delegate
        {
            var dialog = Element<ContentControl>(owner, "DialogHost").Content as StudioDialog;
            try
            {
                Check(dialog != null && dialog.IsVisible && Element<Grid>(owner, "DialogShade").IsVisible, "custom modal dialogs dim their owner");
                Check(Application.Current.Windows.Count == windows && IsWindowEnabled(source.Handle),
                    "opening a dialog creates no top-level window and does not disable the native owner");
                Check(!Element<Grid>(owner, "WorkspaceGrid").IsHitTestVisible && !Element<Border>(owner, "TitleBar").IsHitTestVisible,
                    "the modal card blocks interaction with the workspace and title bar");
                observed = true;
                respond(dialog);
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                if (dialog != null && dialog.IsVisible)
                    Element<Button>(dialog, "DialogCloseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }), DispatcherPriority.ApplicationIdle);
        try { show(); Pump(); }
        finally { source.RemoveHook(hook); }
        if (failure != null) throw failure;
        Check(observed && !Element<Grid>(owner, "DialogShade").IsVisible && Element<ContentControl>(owner, "DialogHost").Content == null &&
            Element<Grid>(owner, "WorkspaceGrid").IsHitTestVisible && Element<Border>(owner, "TitleBar").IsHitTestVisible,
            "closing the dialog clears the card and restores workspace interaction");
        Check(Application.Current.Windows.Count == windows && (activationChanges == 0 || externalActivation) && enableChanges == 0,
            "dialogs avoid native window transitions, excluding focus changes to other applications (activation=" + activationChanges + ", enable=" + enableChanges + ")");
    }

    private static void StandaloneDialogUi()
    {
        var dialog = (StudioDialog)typeof(StudioDialog).GetMethod("CreateMessage", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { null, "没有主窗口时仍然可以显示异常信息。", "程序异常", false });
        bool shown = false;
        Exception failure = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(delegate
        {
            try
            {
                var host = Window.GetWindow(dialog);
                Check(host != null && host.IsVisible && host.WindowStyle == WindowStyle.None && host.Content == dialog,
                    "startup errors without a visible workspace retain a standalone custom dialog");
                var close = Element<Button>(dialog, "DialogCloseButton");
                Check(HitTestWindow(host, close.TranslatePoint(new Point(close.ActualWidth / 2, close.ActualHeight / 2), host)) == 1,
                    "the standalone dialog's close button receives clicks in its custom caption");
                double left = host.Left, top = host.Top;
                Drag(Element<Thumb>(dialog, "DialogDragHandle"), 25, 20);
                Check(Math.Abs(host.Left - left - 25) <= 1 && Math.Abs(host.Top - top - 20) <= 1,
                    "the standalone message header also supports dragging");
                shown = true;
            }
            catch (Exception ex) { failure = ex; }
            finally { Element<Button>(dialog, "DialogConfirmButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        }), DispatcherPriority.ApplicationIdle);
        bool accepted = (bool)typeof(StudioDialog).GetMethod("ShowModal", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, null);
        if (failure != null) throw failure;
        Check(shown && accepted && Window.GetWindow(dialog) == null, "standalone confirmation returns its result and releases the host window");
    }

    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    private static int HitTestWindow(Window window, Point point)
    {
        Point screen = window.PointToScreen(point);
        int coordinates = ((int)screen.X & 0xffff) | (((int)screen.Y & 0xffff) << 16);
        return SendMessage(new WindowInteropHelper(window).Handle, 0x84, IntPtr.Zero, new IntPtr(coordinates)).ToInt32();
    }

    private static void EditorDeletionUi(JsonEditor json)
    {
        var editor = json.TextBox;
        foreach (string newline in new[] { "\r\n", "\n" })
        {
            string body = "{" + newline + string.Join(newline, Array.ConvertAll(new int[80], x => "  \"use_area\": \"\",,,,,,,,,,")) + newline + "}";
            json.Content = body; Pump();
            editor.ScrollToEnd(); Pump();
            for (int i = 0; i < 10; i++)
            {
                int comma = editor.Text.LastIndexOf(',');
                editor.Select(comma, 1); editor.SelectedText = "";
                Pump();
            }
            Check(editor.Text.Length == body.Length - 10 && HasVisibleCharacter(json, editor.Text.Length - 1),
                "deleting trailing commas at the bottom keeps the editor rendering: " + newline.Length);
            int start = editor.Text.IndexOf(newline);
            editor.Select(start, editor.Text.LastIndexOf(newline) - start);
            editor.SelectedText = ""; Pump();
            Check(editor.LineCount == 2 && HasVisibleCharacter(json, 0),
                "deleting many scrolled lines clamps rendering to the remaining lines: " + newline.Length);
            editor.Undo(); Pump(); editor.Redo(); Pump();
            Check(editor.LineCount == 2 && HasVisibleCharacter(json, editor.Text.Length - 1),
                "undo and redo after deleting scrolled lines keep text visible");
            editor.SelectAll(); editor.SelectedText = ""; Pump();
            Check(editor.Text == "" && editor.LineCount == 1, "deleting the entire document renders the empty first line");
        }
    }

    private static void ContinuousRenderingUi(JsonEditor json)
    {
        json.Content = "{\r\n  \"value\": \"\"\r\n}"; Pump();
        var editor = json.TextBox;
        editor.Select(editor.Text.IndexOf("\"\"", StringComparison.Ordinal) + 1, 0);
        for (int i = 0; i < 12; i++)
        {
            string input = i % 3 == 2 ? "\r\n字" : "字";
            int caret = editor.CaretIndex;
            editor.SelectedText = input;
            editor.Select(caret + input.Length, 0);
            Pump();
            var frame = new RenderTargetBitmap((int)Math.Ceiling(json.ActualWidth), (int)Math.Ceiling(json.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            frame.Render(json);
            Check(ContainsColor(frame, Color.FromRgb(142,181,255)) && ContainsColor(frame, Color.FromRgb(116,215,189)) &&
                HasVisibleCharacter(json, editor.CaretIndex - 1), "continuous input keeps syntax colors and the newest character visible: " + i);
            var gutter = new CroppedBitmap(frame, new Int32Rect(0, 0, 48, frame.PixelHeight));
            Check(editor.LineCount == 3 + (i + 1) / 3 && ContainsColor(gutter, Color.FromRgb(151,166,187), Color.FromRgb(22,30,42)),
                "continuous input keeps line numbers visible: " + i);
        }
        var composition = new TextComposition(InputManager.Current, editor, "");
        editor.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent });
        Pump();
        Check(((SolidColorBrush)editor.Foreground).Color.A == 255 && HasVisibleCharacter(json, editor.CaretIndex - 1),
            "IME composition keeps native text visible");
        EnterText(editor, "中文"); Pump();
        var committed = new RenderTargetBitmap((int)Math.Ceiling(json.ActualWidth), (int)Math.Ceiling(json.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        committed.Render(json);
        Check(ContainsColor(committed, Color.FromRgb(116,215,189)) && HasVisibleCharacter(json, editor.CaretIndex - 1),
            "committing IME text restores syntax coloring without hiding the input");
    }

    private static void PairedSymbolsUi(JsonEditor json)
    {
        var editor = json.TextBox;
        foreach (string pair in new[] { "{}", "[]", "()", "\"\"", "''" })
        {
            json.Content = ""; Pump();
            EnterText(editor, pair.Substring(0, 1)); Pump();
            Check(editor.Text == pair && editor.CaretIndex == 1 && editor.SelectionLength == 0,
                "typing an opening symbol inserts its pair with the caret between: " + pair);
            EnterText(editor, pair.Substring(1)); Pump();
            Check(editor.Text == pair && editor.CaretIndex == 2, "typing the existing closing symbol advances the caret: " + pair);
            editor.Undo(); Pump();
            Check(editor.Text == "", "an automatically inserted pair undoes in one step: " + pair);
            editor.Redo(); Pump();
            Check(editor.Text == pair, "redo restores both symbols: " + pair);
        }
        json.Content = "value"; Pump(); editor.SelectAll();
        EnterText(editor, "{"); Pump();
        Check(editor.Text == "{value}" && editor.SelectedText == "value", "opening symbols wrap the selected text");
        editor.Undo(); Pump();
        Check(editor.Text == "value", "wrapping selected text undoes in one step");
        json.Content = ""; Pump();
        foreach (string input in new[] { "{", "[", "\"", "值", "\"", "]", "}" }) EnterText(editor, input);
        Pump();
        Check(editor.Text == "{[\"值\"]}" && editor.CaretIndex == editor.Text.Length,
            "nested pairs accept text inside and skip their existing closing symbols");
        json.Content = "\"ab\""; Pump(); editor.Select(2, 0);
        EnterText(editor, "{"); Pump();
        Check(editor.Text == "\"a{b\"", "brackets typed inside a string remain literal characters");
        json.Content = "\"a\\\""; Pump(); editor.Select(3, 0);
        EnterText(editor, "\""); Pump();
        Check(editor.Text == "\"a\\\"\"" && editor.CaretIndex == 4, "an escaped quote is inserted instead of skipping a closing quote");
        json.Content = ""; Pump(); EnterText(editor, "中文"); Pump();
        Check(editor.Text == "中文", "multi-character input keeps native text insertion");
        json.Content = ""; Pump(); editor.IsReadOnly = true;
        try { EnterText(editor, "{"); Pump(); Check(editor.Text == "", "read-only editors do not insert paired symbols"); }
        finally { editor.IsReadOnly = false; }
    }

    private static void AutoIndentUi(JsonEditor json)
    {
        var editor = json.TextBox;
        foreach (string pair in new[] { "{}", "[]", "()" })
        {
            json.Content = ""; Pump(); EnterText(editor, pair.Substring(0, 1));
            PressEnter(editor); Pump();
            string expanded = pair[0] + "\r\n  \r\n" + pair[1];
            Check(editor.Text == expanded && editor.CaretIndex == 5 && editor.SelectionLength == 0 && editor.LineCount == 3,
                "Enter between paired brackets adds an indented caret line and a closing line: " + pair);
            EnterText(editor, "值"); Pump();
            Check(editor.Text == pair[0] + "\r\n  值\r\n" + pair[1] && HasVisibleCharacter(json, editor.CaretIndex - 1),
                "typing after auto-indent inserts text on the middle line: " + pair);
            editor.Undo(); Pump(); editor.Undo(); Pump();
            Check(editor.Text == pair && editor.CaretIndex == 1, "auto-indent undoes in one step: " + pair);
            editor.Redo(); Pump();
            Check(editor.Text == expanded && editor.CaretIndex == 5, "redo restores the indentation and caret: " + pair);
        }
        foreach (string newline in new[] { "\r\n", "\n" })
        {
            string prefix = "{" + newline + "  \"items\": ";
            json.Content = prefix + "{}" + newline + "}"; Pump(); editor.Select(prefix.Length + 1, 0);
            PressEnter(editor); Pump();
            Check(editor.Text == prefix + "{" + newline + "    " + newline + "  }" + newline + "}" &&
                editor.CaretIndex == prefix.Length + 1 + newline.Length + 4,
                "nested objects inherit indentation and preserve the document's line endings: " + newline.Length);
        }
        json.Content = "\t{  }"; Pump(); editor.Select(3, 0); PressEnter(editor); Pump();
        Check(editor.Text == "\t{\r\n\t\t\r\n\t}" && editor.CaretIndex == 6,
            "spaced bracket pairs expand using the existing tab indentation");
        json.Content = "  \"a\": 1,"; Pump(); editor.Select(editor.Text.Length, 0); PressEnter(editor); Pump();
        Check(editor.Text == "  \"a\": 1,\r\n  " && editor.CaretIndex == editor.Text.Length,
            "ordinary Enter carries the current line indentation forward");
        json.Content = "  {"; Pump(); editor.Select(editor.Text.Length, 0); PressEnter(editor); Pump();
        Check(editor.Text == "  {\r\n    ", "Enter after an opening bracket indents one additional level");
        json.Content = "\"{}\""; Pump(); editor.Select(2, 0); PressEnter(editor); Pump();
        Check(editor.Text == "\"{\r\n}\"", "brackets inside a string do not create an extra closing line");
        json.Content = "{remove}"; Pump(); editor.Select(1, 6); PressEnter(editor); Pump();
        Check(editor.Text == "{\r\n  \r\n}" && editor.CaretIndex == 5,
            "Enter replaces the selection while expanding its surrounding brackets");
        editor.Undo(); Pump();
        Check(editor.Text == "{remove}" && editor.SelectedText == "remove", "undo restores the text replaced by auto-indent");
        json.Content = "{}"; Pump(); editor.Select(1, 0); editor.IsReadOnly = true;
        try { PressEnter(editor); Pump(); Check(editor.Text == "{}", "auto-indent leaves read-only editors unchanged"); }
        finally { editor.IsReadOnly = false; }
    }

    private static void TabIndentUi(JsonEditor json)
    {
        var editor = json.TextBox;
        json.Content = "abcd"; Pump(); editor.Select(2, 0);
        EditingCommands.TabForward.Execute(null, editor); Pump();
        Check(editor.Text == "ab  cd" && editor.CaretIndex == 4 && editor.SelectionLength == 0,
            "Tab inserts two spaces at the caret without replacing surrounding text");
        editor.Undo(); Pump();
        Check(editor.Text == "abcd" && editor.CaretIndex == 2, "a Tab insertion undoes in one step");
        json.Content = ""; Pump(); EditingCommands.TabForward.Execute(null, editor); Pump();
        Check(editor.Text == "  " && editor.CaretIndex == 2, "Tab indents an empty document by two spaces");

        foreach (string newline in new[] { "\r\n", "\n", "\r" })
        {
            string original = "alpha" + newline + "  beta" + newline + newline + "gamma";
            string indented = "  alpha" + newline + "    beta" + newline + "  " + newline + "  gamma";
            json.Content = original; Pump(); editor.SelectAll();
            EditingCommands.TabForward.Execute(null, editor); Pump();
            Check(editor.Text == indented && editor.SelectionStart == 0 && editor.SelectionLength == indented.Length,
                "Tab indents all selected lines including blank lines and preserves line endings: " + newline.Length + (int)newline[0]);
            editor.Undo(); Pump();
            Check(editor.Text == original && editor.SelectedText == original, "block indentation undoes as one edit with the selection restored");
            editor.Redo(); Pump();
            Check(editor.Text == indented && editor.SelectedText == indented, "redo restores all indented lines and their selection");
            EditingCommands.TabBackward.Execute(null, editor); Pump();
            Check(editor.Text == original && editor.SelectedText == original, "Shift+Tab unindents the entire selected block by two spaces");
            editor.Undo(); Pump(); editor.Redo(); Pump();
            Check(editor.Text == original && editor.SelectedText == original, "block unindentation supports one-step undo and redo");

            json.Content = "a" + newline + "b" + newline + "c"; Pump(); editor.Select(0, 1 + newline.Length);
            EditingCommands.TabForward.Execute(null, editor); Pump();
            Check(editor.Text == "  a" + newline + "b" + newline + "c" && editor.SelectedText == "  a" + newline,
                "a selection ending at the next line's start does not indent that next line");
            json.Content = "abcd" + newline + "efgh" + newline + "tail"; Pump();
            int end = editor.Text.IndexOf("gh", StringComparison.Ordinal) + 1;
            editor.Select(1, end - 1);
            EditingCommands.TabForward.Execute(null, editor); Pump();
            Check(editor.Text == "  abcd" + newline + "  efgh" + newline + "tail" && editor.SelectionStart == 3 &&
                editor.SelectedText == "bcd" + newline + "  efg", "partial multi-line selections stay attached to the same text after indentation");
            EditingCommands.TabBackward.Execute(null, editor); Pump();
            Check(editor.Text == "abcd" + newline + "efgh" + newline + "tail" && editor.SelectionStart == 1 && editor.SelectionLength == end - 1,
                "unindenting a partial multi-line selection restores its original bounds");
        }
        json.Content = "abcd"; Pump(); editor.Select(1, 2);
        EditingCommands.TabForward.Execute(null, editor); Pump();
        Check(editor.Text == "  abcd" && editor.SelectedText == "bc", "Tab indents a selected single line and retains its selected text");
        json.Content = "    value"; Pump(); editor.Select(6, 0);
        EditingCommands.TabBackward.Execute(null, editor); Pump();
        Check(editor.Text == "  value" && editor.CaretIndex == 4 && editor.SelectionLength == 0,
            "Shift+Tab removes one leading indent and keeps the caret on the same character");
        json.Content = " value\n  value\n\tvalue\nvalue"; Pump(); editor.SelectAll();
        EditingCommands.TabBackward.Execute(null, editor); Pump();
        Check(editor.Text == "value\nvalue\nvalue\nvalue" && editor.SelectedText == editor.Text,
            "Shift+Tab removes short indents and legacy tabs without deleting content");
        json.Content = " value"; Pump(); editor.Select(0, 0);
        EditingCommands.TabBackward.Execute(null, editor); Pump();
        Check(editor.Text == "value" && editor.CaretIndex == 0, "unindenting at column zero keeps the caret in bounds");
        json.Content = "value"; Pump(); editor.Select(2, 0);
        EditingCommands.TabBackward.Execute(null, editor); Pump();
        Check(editor.Text == "value" && editor.CaretIndex == 2 && !editor.CanUndo,
            "Shift+Tab on an unindented line does not change text or add an undo step");
        json.Content = "a\r\n"; Pump(); editor.SelectAll();
        EditingCommands.TabForward.Execute(null, editor); Pump();
        Check(editor.Text == "  a\r\n", "block indentation does not include an unselected trailing empty line");
        json.Content = "a\nb"; Pump(); editor.SelectAll();
        EditingCommands.TabForward.Execute(null, editor); EditingCommands.TabForward.Execute(null, editor); Pump();
        Check(editor.Text == "    a\n    b" && editor.SelectedText == editor.Text, "repeated Tab presses retain the block selection and add two spaces each time");
        EditingCommands.TabBackward.Execute(null, editor); EditingCommands.TabBackward.Execute(null, editor); Pump();
        Check(editor.Text == "a\nb" && editor.SelectedText == editor.Text, "repeated Shift+Tab presses return the selected block to its original indentation");
        json.Content = "  value"; Pump(); editor.SelectAll(); editor.IsReadOnly = true;
        try
        {
            Check(!EditingCommands.TabForward.CanExecute(null, editor) && !EditingCommands.TabBackward.CanExecute(null, editor),
                "Tab indentation commands are disabled in read-only editors");
        }
        finally { editor.IsReadOnly = false; }
    }

    private static void PressEnter(TextBox editor)
    {
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), Environment.TickCount, Key.Return)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        editor.RaiseEvent(key);
        if (!key.Handled) EditingCommands.EnterParagraphBreak.Execute(null, editor);
    }

    private static void EnterText(TextBox editor, string text)
    {
        var composition = new TextComposition(InputManager.Current, editor, text);
        var preview = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
        editor.RaiseEvent(preview);
        if (!preview.Handled)
            editor.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.TextInputEvent });
    }

    private static void SymbolInputUi(StudioWindow window, JsonEditor json)
    {
        var editor = json.TextBox;
        const string symbols = "{}[]\":,\\!@#$%^&*()_+-=<>?；：“”";
        json.Content = ""; Pump();
        for (int index = 0; index < symbols.Length; index++)
        {
            editor.Select(editor.Text.Length, 0);
            editor.SelectedText = symbols[index].ToString();
            Pump();
            Check(editor.Text == symbols.Substring(0, index + 1) && HasVisibleCharacter(json, index),
                "typed symbol remains visible with live highlighting: " + symbols[index]);
        }
        PumpFor(TimeSpan.FromMilliseconds(250));
        Check(editor.Text == symbols && HasVisibleCharacter(json, symbols.Length - 1),
            "symbols remain visible after automatic syntax refresh without an explicit highlight call");

        var tabs = Element<TabControl>(window, "RequestTabs");
        tabs.SelectedIndex = 1; Pump();
        tabs.SelectedIndex = 0; Pump();
        Check(editor.Text == symbols && HasVisibleCharacter(json, 0),
            "symbols remain visible after leaving and returning to the request body");

        json.Content = ""; Pump();
        editor.SelectedText = "\""; Pump();
        editor.Undo(); Pump();
        editor.Redo(); Pump();
        Check(editor.Text == "\"" && HasVisibleCharacter(json, 0), "a single quote remains visible after undo and redo");

        json.Content = new string('x', 200001); Pump();
        Check(HasVisibleCharacter(json, 0), "native text remains visible when large bodies skip syntax highlighting");
    }

    private static bool HasVisibleCharacter(JsonEditor json, int index)
    {
        var editor = json.TextBox;
        Rect leading = editor.GetRectFromCharacterIndex(index);
        Rect trailing = editor.GetRectFromCharacterIndex(index, true);
        if (leading.IsEmpty || trailing.IsEmpty) return false;
        Point origin = editor.TranslatePoint(new Point(0, 0), json);
        int x = Math.Max(0, (int)Math.Floor(origin.X + leading.Left));
        int y = Math.Max(0, (int)Math.Floor(origin.Y + leading.Top));
        int width = Math.Max(1, (int)Math.Ceiling(trailing.Left - leading.Left));
        int height = Math.Max(1, (int)Math.Ceiling(leading.Height));
        var frame = new RenderTargetBitmap((int)Math.Ceiling(json.ActualWidth), (int)Math.Ceiling(json.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        frame.Render(json);
        width = Math.Min(width, frame.PixelWidth - x); height = Math.Min(height, frame.PixelHeight - y);
        if (width <= 0 || height <= 0) return false;
        int stride = width * 4;
        var pixels = new byte[stride * height];
        frame.CopyPixels(new Int32Rect(x, y, width, height), pixels, stride, 0);
        // The editor uses the dark surface here. Exclude its background and
        // require multiple glyph pixels so checking Text alone cannot pass.
        int matches = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            if (Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])) > 90 && ++matches >= 2) return true;
        return false;
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = duration };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static bool ContainsColor(BitmapSource frame, Color color, Color? background = null)
    {
        Color surface = background ?? Color.FromRgb(26, 34, 47);
        int stride = frame.PixelWidth * 4;
        var pixels = new byte[stride * frame.PixelHeight];
        frame.CopyPixels(pixels, stride, 0);
        int matches = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            // Thin glyphs may have no fully covered pixels at fractional DPI.
            double coverage = (pixels[i+2] - (double)surface.R) / (color.R - (double)surface.R);
            if (coverage >= .5 && coverage <= 1.05 && Math.Abs(pixels[i] - (surface.B + (color.B - surface.B) * coverage)) <= 8 &&
                Math.Abs(pixels[i+1] - (surface.G + (color.G - surface.G) * coverage)) <= 8 && ++matches > 3) return true;
        }
        return false;
    }

    private static void WebServiceUi(StudioWindow window, WorkspaceStore store, TextBox address, JsonEditor json)
    {
        var type = Element<ComboBox>(window, "RequestType");
        var method = Element<ComboBox>(window, "RequestMethod");
        var actionHint = Element<TextBlock>(window, "SoapActionHint");
        var methodBox = Element<TextBox>(window, "WsMethodBox");
        var namespaceBox = Element<TextBox>(window, "WsNamespaceBox");
        var rows = Element<StackPanel>(window, "WsRows");
        string original = json.Content;
        Check(type.Items.Count == 2 && type.Items[0].ToString() == "API 接口" && type.Items[1].ToString() == "WebService 接口",
            "the call-type selector exposes exactly API and WebService modes");
        type.SelectedIndex = 1; Pump();
        Check(method.SelectedItem.ToString() == "POST" && !method.IsEnabled && actionHint.IsVisible &&
            !Element<DockPanel>(window, "JsonBodyPanel").IsVisible && Element<Grid>(window, "WsBodyPanel").IsVisible &&
            !Element<StackPanel>(window, "BodyFormatActions").IsVisible,
            "WebService mode shows the parameter table, hides the JSON editor and locks POST");
        Check(rows.Children.Count == 1, "switching to WebService seeds one empty parameter row");

        // 只写地址即可：方法名取自 ?op=，Action 由命名空间 + 方法名自动生成。
        address.Text = "http://100.2.2.217:8089/HospitalService.asmx?op=GetBoxInfo"; Pump();
        Check(methodBox.Text == "GetBoxInfo" && actionHint.Text.Contains("http://tempuri.org/GetBoxInfo"),
            "the method name comes from ?op= in the address and drives the automatic Action");
        address.Text = "http://100.2.2.217:8089/HospitalService.asmx/Submit"; Pump();
        Check(methodBox.Text == "Submit", "a .asmx/ path form also supplies the method name");
        address.Text = "http://100.2.2.217:8089/HospitalService.asmx?op=Add"; Pump();
        Check(methodBox.Text == "Add", "changing ?op= in the address updates the method name");
        methodBox.Text = "Subtract"; Pump();
        address.Text = "http://100.2.2.217:8089/HospitalService.asmx?op=GetBoxInfo"; Pump();
        Check(methodBox.Text == "Subtract", "a manually entered method name is not overwritten by the address");
        methodBox.Text = "Add"; Pump();

        // 添加按钮加行；方法名与命名空间驱动 Action 提示。
        Invoke(window, "AddWsRowClick", null, null); Pump();
        Check(rows.Children.Count == 2, "the add button appends a parameter row");
        var row1 = (Grid)rows.Children[0]; var row2 = (Grid)rows.Children[1];
        ((TextBox)row1.Children[1]).Text = "intA";
        ((TextBox)row1.Children[2]).Text = "中文 & <值>";
        ((TextBox)row2.Children[1]).Text = "intB";
        ((TextBox)row2.Children[2]).Text = "20"; Pump();
        Check(actionHint.Text == "Action：http://tempuri.org/Add", "the Action hint follows the method name and namespace");
        namespaceBox.Text = "urn:calc"; Pump();
        Check(actionHint.Text == "Action：urn:calc/Add", "changing the namespace updates the automatic Action");
        namespaceBox.Text = "http://tempuri.org/"; Pump();

        // 删除按钮减行，再补回用于发送。
        ((Button)row2.Children[3]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(rows.Children.Count == 1, "the delete button removes its parameter row");
        Invoke(window, "AddWsRowClick", null, null); Pump();
        var recreated = (Grid)rows.Children[1];
        ((TextBox)recreated.Children[1]).Text = "intB";
        ((TextBox)recreated.Children[2]).Text = "20"; Pump();

        // 临时切回 API 检查两个面板互换，再切回 WebService 保留表格状态供重开验证。
        type.SelectedIndex = 0; Pump();
        Check(Element<DockPanel>(window, "JsonBodyPanel").IsVisible && !Element<Grid>(window, "WsBodyPanel").IsVisible &&
            json.Content == original, "switching back to API restores the unchanged JSON editor");
        type.SelectedIndex = 1; Pump();

        // 通过真实回环服务验证表格生成的 SOAP 报文、Action 和响应展示。
        SendWsThroughWorkBench(window, store, address, "http://tempuri.org/Add");
    }

    private static void SendWsThroughWorkBench(StudioWindow window, WorkspaceStore store, TextBox address, string expectedAction)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(delegate
            {
                using (var client = listener.AcceptTcpClient())
                using (var stream = client.GetStream())
                {
                    client.ReceiveTimeout = 5000;
                    var header = new StringBuilder();
                    while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                    {
                        int value = stream.ReadByte();
                        if (value < 0) throw new EndOfStreamException();
                        header.Append((char)value);
                    }
                    string head = header.ToString();
                    int length = 0;
                    foreach (string line in head.Split('\n'))
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            length = int.Parse(line.Substring(15).Trim());
                    var payload = new byte[length];
                    for (int received = 0; received < length;)
                    {
                        int read = stream.Read(payload, received, length - received);
                        if (read == 0) throw new EndOfStreamException();
                        received += read;
                    }
                    string wireBody = Encoding.UTF8.GetString(payload);
                    if (!head.StartsWith("POST /verify HTTP/") ||
                        !head.Contains("Content-Type: text/xml; charset=utf-8") ||
                        !head.Contains("SOAPAction: \"" + expectedAction + "\""))
                        throw new Exception("WPF WebService request has incorrect method or headers: " + head);
                    var document = new System.Xml.XmlDocument { XmlResolver = null };
                    document.LoadXml(wireBody);
                    const string ns = "http://schemas.xmlsoap.org/soap/envelope/";
                    var soapBody = document.DocumentElement["Body", ns];
                    var add = soapBody == null ? null : soapBody["Add", "http://tempuri.org/"];
                    if (document.DocumentElement.LocalName != "Envelope" || document.DocumentElement.NamespaceURI != ns || add == null ||
                        add["intA", "http://tempuri.org/"].InnerText != "中文 & <值>" || add["intB", "http://tempuri.org/"].InnerText != "20")
                        throw new Exception("WPF parameter table was not correctly turned into a SOAP envelope: " + wireBody);
                    byte[] response = Encoding.UTF8.GetBytes("<result><source>WPF</source><message>中文成功</message></result>");
                    byte[] prefix = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/xml; charset=utf-8\r\nConnection: close\r\nContent-Length: " + response.Length + "\r\n\r\n");
                    stream.Write(prefix, 0, prefix.Length); stream.Write(response, 0, response.Length);
                }
            });
            address.Text = "http://127.0.0.1:" + port + "/verify";
            var send = (Task)Invoke(window, "SendRequest");
            var combined = Task.WhenAll(send, server);
            DateTime deadline = DateTime.UtcNow.AddSeconds(12);
            while (!combined.IsCompleted && DateTime.UtcNow < deadline)
            {
                Pump(); System.Threading.Thread.Sleep(10);
            }
            if (!combined.IsCompleted) throw new Exception("WPF WebService loopback request timed out");
            combined.GetAwaiter().GetResult();
            var saved = store.Load().Items[0].Children[0];
            Check(saved.StatusCode == 200 && saved.Response.Contains("WPF") && saved.WsMethod == "Add" &&
                saved.WsNamespace == "http://tempuri.org/" && saved.WsParams.Count == 2 &&
                saved.Body.StartsWith("<Add ") && saved.Body.Contains("&lt;值&gt;") && address.Text.Contains("/verify"),
                "the WPF workbench sends the parameter table as SOAP XML, displays the response and saves the table");
            var responseEditor = (JsonEditor)Element<ContentControl>(window, "ResponseHost").Content;
            Check(!responseEditor.UseJsonSyntax && Element<Button>(window, "FormatResponseButton").Content.ToString() == "格式化 XML",
                "SOAP responses use XML display and formatting");
        }
        finally { listener.Stop(); }
    }

    private static RenderTargetBitmap EditorFrame(JsonEditor json)
    {
        var frame = new RenderTargetBitmap((int)Math.Ceiling(json.ActualWidth), (int)Math.Ceiling(json.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        frame.Render(json);
        return frame;
    }
    private static bool HasColorAt(JsonEditor json, string value, Color color, Color? background = null)
    {
        int index = json.Content.IndexOf(value, StringComparison.Ordinal);
        if (index < 0) return false;
        Rect leading = json.TextBox.GetRectFromCharacterIndex(index), trailing = json.TextBox.GetRectFromCharacterIndex(index + value.Length - 1, true);
        if (leading.IsEmpty || trailing.IsEmpty) return false;
        Point origin = json.TextBox.TranslatePoint(new Point(0, 0), json);
        var frame = EditorFrame(json);
        int x = Math.Max(0, (int)Math.Floor(origin.X + leading.Left)), y = Math.Max(0, (int)Math.Floor(origin.Y + leading.Top));
        int width = Math.Min(frame.PixelWidth - x, (int)Math.Ceiling(trailing.Left - leading.Left));
        int height = Math.Min(frame.PixelHeight - y, (int)Math.Ceiling(leading.Height));
        return width > 0 && height > 0 && ContainsColor(new CroppedBitmap(frame, new Int32Rect(x, y, width, height)), color, background);
    }

    private static void SendThroughWorkBench(StudioWindow window, WorkspaceStore store, TextBox address, TextBox editor, string body = "{\"value\":123}", bool wrapped = false, string expectedAction = "urn:test/Call")
    {
        int type = Element<ComboBox>(window, "RequestType").SelectedIndex;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(delegate
            {
                using (var client = listener.AcceptTcpClient())
                using (var stream = client.GetStream())
                {
                    client.ReceiveTimeout = 5000;
                    var header = new StringBuilder();
                    while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                    {
                        int value = stream.ReadByte();
                        if (value < 0) throw new EndOfStreamException();
                        header.Append((char)value);
                    }
                    string head = header.ToString();
                    int length = 0;
                    foreach (string line in head.Split('\n'))
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            length = int.Parse(line.Substring(15).Trim());
                    var payload = new byte[length];
                    for (int received = 0; received < length;)
                    {
                        int read = stream.Read(payload, received, length - received);
                        if (read == 0) throw new EndOfStreamException();
                        received += read;
                    }
                    string wireBody = Encoding.UTF8.GetString(payload);
                    if (!head.StartsWith("POST /verify HTTP/") || !wrapped && wireBody != body)
                        throw new Exception("WPF request did not send the edited method, URL and body");
                    if (wrapped)
                    {
                        var document = new System.Xml.XmlDocument { XmlResolver = null };
                        document.LoadXml(wireBody);
                        string ns = type == 2 ? "http://www.w3.org/2003/05/soap-envelope" : "http://schemas.xmlsoap.org/soap/envelope/";
                        var soapBody = document.DocumentElement["Body", ns];
                        var method = soapBody == null ? null : soapBody["Add", "http://tempuri.org/"];
                        if (document.DocumentElement.LocalName != "Envelope" || document.DocumentElement.NamespaceURI != ns ||
                            method == null || method["intA", "http://tempuri.org/"].InnerText != "10" || method["intB", "http://tempuri.org/"].InnerText != "20" || !wireBody.Contains(body))
                            throw new Exception("WPF method-only XML was not correctly wrapped on the wire");
                    }
                    if (type == 1 && (!head.Contains("Content-Type: text/xml; charset=utf-8") || !head.Contains("SOAPAction: \"" + expectedAction + "\"")))
                        throw new Exception("WPF SOAP 1.1 request has incorrect headers");
                    if (type == 2 && (!head.Contains("Content-Type: application/soap+xml; charset=utf-8; action=\"" + expectedAction + "\"") || head.Contains("SOAPAction:")))
                        throw new Exception("WPF SOAP 1.2 request has incorrect headers");
                    string output = type == 0 ? "{\"success\":true,\"source\":\"WPF\"}" : "<result><source>WPF</source><message>中文成功</message></result>";
                    byte[] response = Encoding.UTF8.GetBytes(output);
                    byte[] prefix = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: " + (type == 0 ? "application/json" : "text/xml") + "; charset=utf-8\r\nConnection: close\r\nContent-Length: " + response.Length + "\r\n\r\n");
                    stream.Write(prefix, 0, prefix.Length); stream.Write(response, 0, response.Length);
                }
            });
            address.Text = "http://127.0.0.1:" + port + "/verify";
            editor.Text = body;
            var send = (Task)Invoke(window, "SendRequest");
            var combined = Task.WhenAll(send, server);
            DateTime deadline = DateTime.UtcNow.AddSeconds(12);
            while (!combined.IsCompleted && DateTime.UtcNow < deadline)
            {
                Pump(); System.Threading.Thread.Sleep(10);
            }
            if (!combined.IsCompleted) throw new Exception("WPF loopback request timed out");
            combined.GetAwaiter().GetResult();
            var saved = store.Load().Items[0].Children[0];
            Check(saved.StatusCode == 200 && saved.Response.Contains("WPF") && saved.Body == body && editor.Text == body && saved.RequestType == RequestTypes.Values[type] &&
                saved.AutoSoapAction == RequestTypes.IsSoap(saved.RequestType) &&
                Element<TextBlock>(window, "ResponseInfo").Text == "200 OK",
                "the WPF workbench sends requests, displays the response and saves it: " + RequestTypes.Values[type] + (wrapped ? " method-only XML" : ""));
        }
        finally { listener.Stop(); }
    }

    private static void SavePreview(StudioWindow window, string output)
    {
        SaveElementPreview(Element<Grid>(window, "WindowRoot"), Path.Combine(output, "wpf-workbench.png"));
    }
    private static void SaveElementPreview(FrameworkElement root, string path)
    {
        root.UpdateLayout();
        int width = (int)Math.Ceiling(root.ActualWidth), height = (int)Math.Ceiling(root.ActualHeight);
        var frame = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        frame.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using (var stream = File.Create(path)) encoder.Save(stream);
    }

    private static void Drag(GridSplitter splitter, double dx, double dy)
    { Drag((Thumb)splitter, dx, dy); }
    private static void Drag(Thumb thumb, double dx, double dy)
    {
        thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        thumb.RaiseEvent(new DragDeltaEventArgs(dx, dy) { RoutedEvent = Thumb.DragDeltaEvent });
        thumb.RaiseEvent(new DragCompletedEventArgs(dx, dy, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Pump();
    }

    private static T Element<T>(FrameworkElement element, string name) where T : class { return (T)element.FindName(name); }
    private static object Invoke(StudioWindow window, string name, params object[] args)
    { return typeof(StudioWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, args); }
    private static void Pump() { Dispatcher.CurrentDispatcher.Invoke(new Action(delegate { }), DispatcherPriority.ApplicationIdle); }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        passed++;
        Console.WriteLine("PASS: " + description);
    }
}
