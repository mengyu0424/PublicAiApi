using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
                "typed symbol remains visible before delayed highlighting: " + symbols[index]);
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
        var root = Element<Grid>(window, "WorkspaceGrid");
        int width = (int)Math.Ceiling(root.ActualWidth), height = (int)Math.Ceiling(root.ActualHeight);
        var frame = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        frame.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using (var stream = File.Create(Path.Combine(output, "wpf-workbench.png"))) encoder.Save(stream);
    }

    private static void Drag(GridSplitter splitter, double dx, double dy)
    {
        splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        splitter.RaiseEvent(new DragDeltaEventArgs(dx, dy) { RoutedEvent = Thumb.DragDeltaEvent });
        splitter.RaiseEvent(new DragCompletedEventArgs(dx, dy, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Pump();
    }

    private static T Element<T>(StudioWindow window, string name) where T : class { return (T)window.FindName(name); }
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
