using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ApiStudio
{
    public partial class StudioWindow : Window
    {
        private readonly RequestService service = new RequestService();
        private readonly DispatcherTimer saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        private readonly ThemePreferenceStore themeStore;
        private readonly SplitterPositionStore sidebarStore, heightStore;
        private readonly JsonEditor requestBody, responseBody;
        private WorkspaceStore store;
        private Workspace workspace;
        private ApiItem current;
        private CancellationTokenSource requestCancellation;
        private bool loading, dirty, closed;
        private string urlMethod = "";
        private int normalWidth, maximizedWidth, normalHeight, maximizedHeight;
        private static readonly string[] ThemeNames = { "跟随系统", "浅色", "深色" };
        private static readonly string[] ThemePaths =
        {
            "F0 M9,2 A7,7 0 1 0 9,16 A7,7 0 1 0 9,2 M9,2 A7,7 0 0 0 9,16 Z",
            "M9,5.5 A3.5,3.5 0 1 0 9,12.5 A3.5,3.5 0 1 0 9,5.5 M9,1 L9,3 M9,15 L9,17 M1,9 L3,9 M15,9 L17,9 M3.3,3.3 L4.8,4.8 M13.2,13.2 L14.7,14.7 M3.3,14.7 L4.8,13.2 M13.2,4.8 L14.7,3.3",
            "M8,1.5 C3.3,2.3 0.9,7.4 2.9,11.5 C5.3,16.6 12.2,17.4 15.7,12.5 C10.2,13.4 5.6,8.5 8,1.5 Z"
        };

        public StudioWindow() : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data")) { }

        internal StudioWindow(string directory)
        {
            themeStore = new ThemePreferenceStore(Path.Combine(directory, "settings.json"));
            sidebarStore = new SplitterPositionStore(directory, "sidebar-width");
            heightStore = new SplitterPositionStore(directory, "request-height");
            store = new WorkspaceStore(Path.Combine(directory, "workspace.json"));
            normalWidth = sidebarStore.Load(false); maximizedWidth = sidebarStore.Load(true);
            normalHeight = heightStore.Load(false); maximizedHeight = heightStore.Load(true);
            Theme.SetMode(themeStore.Load());

            loading = true;
            InitializeComponent();
            StudioChrome.Attach(this, 48, true);
            UpdateWindowChrome();
            requestBody = new JsonEditor(false); responseBody = new JsonEditor(true);
            AttachEditor(RequestHost, requestBody, "请求体");
            AttachEditor(ResponseHost, responseBody, "响应体");
            requestBody.TextBox.TextChanged += delegate { UpdateCurrent(); RefreshRequestFormat(); };
            RequestMethod.ItemsSource = new[] { "GET", "POST", "PUT" };
            RequestType.ItemsSource = RequestTypes.ModeNames;
            RequestStatus.ItemsSource = ApiStatus.Values;
            WsMethodBox.TextChanged += WsFieldChanged;
            WsNamespaceBox.TextChanged += WsFieldChanged;
            ApplyTheme(false);

            saveTimer.Tick += delegate { SaveWorkspace(false); };
            SidebarDivider.DragCompleted += delegate(object sender, DragCompletedEventArgs e)
            {
                if (!e.Canceled) SavePosition(true, SidebarColumn.Width.Value);
            };
            PayloadDivider.DragCompleted += delegate(object sender, DragCompletedEventArgs e)
            {
                if (!e.Canceled) SavePosition(false, RequestRow.Height.Value);
            };
            // GridSplitter also supports keyboard resizing. Its class handler updates the
            // definition before these handled key events are delivered.
            SidebarDivider.AddHandler(KeyDownEvent, new KeyEventHandler(delegate(object sender, KeyEventArgs e)
            {
                if (e.Handled && (e.Key == Key.Left || e.Key == Key.Right)) SavePosition(true, SidebarColumn.Width.Value);
            }), true);
            PayloadDivider.AddHandler(KeyDownEvent, new KeyEventHandler(delegate(object sender, KeyEventArgs e)
            {
                if (e.Handled && (e.Key == Key.Up || e.Key == Key.Down)) SavePosition(false, RequestRow.Height.Value);
            }), true);
            WorkspaceGrid.SizeChanged += delegate { RestorePositions(); };
            PayloadGrid.SizeChanged += delegate { RestorePositions(); };
            StateChanged += delegate { UpdateWindowChrome(); RestorePositions(); };
            Loaded += delegate { RestorePositions(); };
            PreviewKeyDown += HandleShortcuts;
            SourceInitialized += delegate
            {
                var source = (HwndSource)PresentationSource.FromVisual(this);
                source.AddHook(HandleSystemTheme);
                UpdateCompositionBackground();
            };

            try { workspace = store.Load(); }
            catch (Exception ex)
            {
                Warn("读取本地数据失败，已打开空白工作空间。原文件会保留。\r\n" + ex.Message, "数据加载提示");
                workspace = new Workspace();
                store = new WorkspaceStore(Path.Combine(directory, "workspace-recovered-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"));
            }
            loading = false;
            RebuildTree(null);
            SelectFirstRequest();
        }

        private void AttachEditor(ContentControl host, JsonEditor editor, string label)
        {
            System.Windows.Automation.AutomationProperties.SetName(editor.TextBox, label);
            host.Content = editor;
        }

        private void SavePosition(bool sidebar, double position)
        {
            if (WindowState == WindowState.Minimized || position <= 0) return;
            bool maximized = WindowState == WindowState.Maximized;
            int size = (int)Math.Round(position);
            if (sidebar)
            {
                if (maximized) maximizedWidth = size; else normalWidth = size;
            }
            else
            {
                if (maximized) maximizedHeight = size; else normalHeight = size;
            }
            try { (sidebar ? sidebarStore : heightStore).Save(maximized, size); }
            catch (Exception ex) { Footer.Text = "布局保存失败：" + ex.Message; }
        }

        private void RestorePositions()
        {
            if (WindowState == WindowState.Minimized || SidebarColumn == null || PayloadGrid == null) return;
            bool maximized = WindowState == WindowState.Maximized;
            int width = maximized ? maximizedWidth : normalWidth;
            double maxWidth = WorkspaceGrid.ActualWidth - 10 - MainColumn.MinWidth;
            if (maxWidth >= SidebarColumn.MinWidth)
                SidebarColumn.Width = new GridLength(Math.Max(SidebarColumn.MinWidth, Math.Min(maxWidth, width > 0 ? width : 336)));
            int height = maximized ? maximizedHeight : normalHeight;
            double availableHeight = PayloadGrid.ActualHeight - 12;
            double maxHeight = availableHeight - ResponseRow.MinHeight;
            if (maxHeight >= RequestRow.MinHeight)
                RequestRow.Height = new GridLength(Math.Max(RequestRow.MinHeight, Math.Min(maxHeight, height > 0 ? height : Math.Round(availableHeight * .4))));
        }

        private IntPtr HandleSystemTheme(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if ((message == 0x1A || message == 0x31A) && Theme.Mode == AppThemeMode.System)
            {
                bool previous = Theme.IsDark;
                Theme.RefreshSystemTheme();
                if (previous != Theme.IsDark) ApplyTheme(false);
            }
            return IntPtr.Zero;
        }

        private static SolidColorBrush Brush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        private void ApplyTheme(bool animate)
        {
            Theme.ApplyResources(Resources);
            UpdateCompositionBackground();
            requestBody.RefreshTheme();
            responseBody.RefreshTheme();
            UpdateMethodAppearance();
            ThemeIcon.Data = Geometry.Parse(ThemePaths[(int)Theme.Mode]);
            ThemeIcon.Fill = Theme.Mode == AppThemeMode.System ? (Brush)Resources["Ink"] : Brushes.Transparent;
            ThemeButton.ToolTip = "主题 · " + ThemeNames[(int)Theme.Mode];
            if (animate && SystemParameters.MenuAnimation)
                ThemeRotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-25, 0, TimeSpan.FromMilliseconds(180))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }

        private void ThemeClick(object sender, RoutedEventArgs e)
        {
            var menu = CreateMenu();
            menu.MinWidth = 154;
            for (int i = 0; i < ThemeNames.Length; i++)
            {
                var mode = (AppThemeMode)i;
                var item = AddMenuItem(menu, ThemeNames[i], delegate
                {
                    Theme.SetMode(mode);
                    ApplyTheme(true);
                    try { themeStore.Save(mode); Footer.Text = "主题已切换为“" + ThemeNames[(int)mode] + "”并保存"; }
                    catch (Exception ex) { Footer.Text = "主题设置保存失败：" + ex.Message; }
                });
                item.IsChecked = mode == Theme.Mode;
                var header = new StackPanel { Orientation = Orientation.Horizontal };
                var glyph = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(ThemePaths[i]), Width = 16, Height = 16, Stretch = Stretch.Uniform,
                    StrokeThickness = 1.2, StrokeLineJoin = PenLineJoin.Round, Margin = new Thickness(0, 0, 8, 0)
                };
                glyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Muted");
                if (mode == AppThemeMode.System) glyph.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Muted");
                header.Children.Add(glyph);
                header.Children.Add(new TextBlock { Text = ThemeNames[i] });
                item.Header = header;
                System.Windows.Automation.AutomationProperties.SetName(item, ThemeNames[i]);
            }
            menu.PlacementTarget = ThemeButton;
            menu.Placement = PlacementMode.Bottom;
            menu.HorizontalOffset = ThemeButton.ActualWidth - menu.MinWidth;
            menu.VerticalOffset = 4;
            ThemeButton.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void LoadItem(ApiItem item)
        {
            loading = true;
            try
            {
                current = item;
                RequestTitle.Text = item.Name;
                Breadcrumb.Text = FolderPath(item);
                Breadcrumb.ToolTip = Breadcrumb.Text;
                RequestMethod.SelectedItem = item.Method;
                RequestType.SelectedIndex = RequestTypes.ModeIndex(RequestTypes.Normalize(item.RequestType));
                WsMethodBox.Text = string.IsNullOrWhiteSpace(item.WsMethod) ? RequestTypes.MethodFromUrl(item.Url) : item.WsMethod;
                urlMethod = RequestTypes.MethodFromUrl(item.Url);
                WsNamespaceBox.Text = string.IsNullOrWhiteSpace(item.WsNamespace) ? RequestTypes.DefaultSoapNamespace : item.WsNamespace;
                RebuildWsRows(item.WsParams);
                RequestStatus.SelectedItem = ApiStatus.Normalize(item.Status);
                AddressBox.Text = item.Url ?? "";
                RequestHeaders.Text = item.Headers ?? "";
                requestBody.Content = item.Body;
                RefreshQuery();
                ShowResponse(item);
                UpdateMethodAppearance();
                UpdateTitleHint();
                EmptyPage.Visibility = Visibility.Collapsed;
                EditorPage.Visibility = Visibility.Visible;
            }
            finally { loading = false; }
        }

        private void ClearEditor()
        {
            current = null;
            EditorPage.Visibility = Visibility.Collapsed;
            EmptyPage.Visibility = Visibility.Visible;
        }
        private void TitleChanged(object sender, TextChangedEventArgs e)
        {
            if (loading || current == null) return;
            UpdateTitleHint();
            string name = RequestTitle.Text.Trim();
            if (name.Length == 0 || name == current.Name) return;
            current.Name = name;
            RefreshNode(current);
            MarkDirty();
        }
        private void UpdateTitleHint() { RequestTitle.ToolTip = RequestTitle.Text; }
        private void NormalizeTitle()
        {
            if (current != null && RequestTitle.Text != current.Name) RequestTitle.Text = current.Name;
        }
        private void TitleLostFocus(object sender, KeyboardFocusChangedEventArgs e) { NormalizeTitle(); }
        private void TitleKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
            NormalizeTitle();
            AddressBox.Focus();
            e.Handled = true;
        }
        private void StatusChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loading || current == null || RequestStatus.SelectedItem == null) return;
            current.Status = (string)RequestStatus.SelectedItem;
            RefreshNode(current);
            MarkDirty();
        }
        private void MethodChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loading) return;
            UpdateMethodAppearance();
            UpdateCurrent();
            if (current != null) RefreshNode(current);
        }
        private string SelectedRequestType
        {
            get { return RequestTypes.ModeRequestType(RequestType.SelectedIndex, current == null ? RequestTypes.Soap11 : current.RequestType); }
        }
        private void RequestTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loading || current == null) return;
            string previous = RequestTypes.Normalize(current.RequestType), next = SelectedRequestType;
            loading = true;
            try
            {
                RequestHeaders.Text = RequestTypes.SwitchDefaultHeaders(RequestHeaders.Text, previous, next);
                if (RequestTypes.IsSoap(next))
                {
                    RequestMethod.SelectedItem = "POST";
                    // 从 API 切到 WebService 且尚无表格数据时，方法名优先取地址里的 ?op=，命名空间给默认值。
                    if (!RequestTypes.IsSoap(previous))
                    {
                        urlMethod = RequestTypes.MethodFromUrl(AddressBox.Text);
                        if (string.IsNullOrWhiteSpace(WsMethodBox.Text) && urlMethod.Length > 0) WsMethodBox.Text = urlMethod;
                        if (string.IsNullOrWhiteSpace(WsNamespaceBox.Text)) WsNamespaceBox.Text = RequestTypes.DefaultSoapNamespace;
                        if (WsRows.Children.Count == 0) AddWsRow();
                    }
                }
            }
            finally { loading = false; }
            UpdateMethodAppearance();
            UpdateCurrent();
            RefreshNode(current);
        }
        // Action 完全自动：由命名空间 + 方法名拼出（发送时由 RequestService 计算），界面只做提示。
        private void RefreshSoapAction()
        {
            bool soap = RequestTypes.IsSoap(SelectedRequestType);
            SoapActionHint.Visibility = soap ? Visibility.Visible : Visibility.Collapsed;
            if (!soap) return;
            string action = "";
            try { action = RequestTypes.InferSoapAction(SoapMethodXml()); }
            catch (Exception) { }
            SoapActionHint.Text = action.Length == 0 ? "Action 自动生成：填写方法名或地址中的 ?op=" : "Action：" + action;
        }
        private void RefreshRequestFormat()
        {
            bool xml = RequestTypes.IsSoap(SelectedRequestType) || XmlText.LooksLikeXml(requestBody.Content);
            requestBody.UseJsonSyntax = !xml;
            FormatRequestButton.Content = xml ? "格式化 XML" : "格式化 JSON";
            RefreshSoapAction();
        }
        private void UpdateMethodAppearance()
        {
            string method = RequestMethod.SelectedItem as string;
            bool soap = RequestTypes.IsSoap(SelectedRequestType);
            RequestMethod.IsEnabled = !soap;
            RequestMethod.Foreground = Brush(Theme.MethodColor(method));
            JsonBodyPanel.Visibility = soap ? Visibility.Collapsed : Visibility.Visible;
            WsBodyPanel.Visibility = soap ? Visibility.Visible : Visibility.Collapsed;
            BodyFormatActions.Visibility = soap ? Visibility.Collapsed : Visibility.Visible;
            BodyHint.Text = method == "GET" ? "GET 请求不发送请求体  ·  请在 Query 参数中添加参数" :
                "RAW / JSON / XML  ·  Content-Type 可在请求头中设置  ·  超时 60 秒";
            if (soap) WsHint.Text = "每行一个入参  ·  " + SelectedRequestType + "  ·  方法名取自地址的 ?op=  ·  参数值自动转义  ·  超时 60 秒";
            RefreshRequestFormat();
        }
        private void AddressChanged(object sender, TextChangedEventArgs e)
        {
            if (loading) return;
            SyncMethodFromUrl();
            UpdateCurrent();
            RefreshSoapAction();
            RefreshQuery();
        }
        // 地址形如 .../HospitalService.asmx?op=GetBoxInfo 时，方法名直接取自 ?op=；
        // 用户手改过方法名就不再覆盖，除非地址里的方法名发生变化。
        private void SyncMethodFromUrl()
        {
            if (!RequestTypes.IsSoap(SelectedRequestType)) return;
            string method = RequestTypes.MethodFromUrl(AddressBox.Text);
            if (method == urlMethod) return;
            bool follow = string.IsNullOrWhiteSpace(WsMethodBox.Text) || string.Equals(WsMethodBox.Text, urlMethod, StringComparison.Ordinal);
            urlMethod = method;
            if (method.Length == 0 || !follow) return;
            loading = true;
            try { WsMethodBox.Text = method; }
            finally { loading = false; }
        }
        private void HeadersChanged(object sender, TextChangedEventArgs e) { UpdateCurrent(); }
        private void UpdateCurrent()
        {
            if (loading || current == null) return;
            current.Method = RequestMethod.SelectedItem as string ?? "GET";
            current.RequestType = SelectedRequestType;
            current.Url = AddressBox.Text;
            current.Headers = RequestHeaders.Text;
            if (RequestTypes.IsSoap(current.RequestType))
            {
                current.WsMethod = WsMethodBox.Text;
                current.WsNamespace = WsNamespaceBox.Text;
                current.WsParams = CollectWsRows();
                // 表格是 WebService 模式的入参来源；拼成方法 XML 供发送与保存。
                try { current.Body = SoapMethodXml(); }
                catch (ArgumentException) { /* 方法名非法时保留上一次有效报文，发送处会再次校验提示 */ }
                // Action 不再由用户填写：保存自动推断的结果，发送时 RequestService 也会重新计算。
                current.SoapAction = RequestTypes.InferSoapAction(current.Body);
                current.AutoSoapAction = true;
            }
            else current.Body = requestBody.Content;
            MarkDirty();
        }
        // 由当前表格字段拼出 SOAP 方法 XML；非法字段抛出 ArgumentException / XmlException。
        private string SoapMethodXml()
        {
            return RequestTypes.BuildMethodXml(WsMethodBox.Text, WsNamespaceBox.Text, CollectWsRows());
        }

        // ---- WebService 表格入参 ----
        private static readonly GridLength[] WsColumns =
        {
            new GridLength(42), new GridLength(210), new GridLength(1, GridUnitType.Star), new GridLength(54)
        };
        private void RebuildWsRows(List<WsParam> parameters)
        {
            WsRows.Children.Clear();
            foreach (var p in parameters ?? new List<WsParam>()) AddWsRow(p);
            RenumberWsRows();
        }
        private Grid AddWsRow(WsParam parameter = null)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            foreach (var width in WsColumns) row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
            var number = new TextBlock { Text = "", Margin = new Thickness(10, 0, 0, 0), FontSize = 11 };
            number.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            row.Children.Add(number);
            var nameBox = new TextBox
            {
                Text = parameter == null ? "" : parameter.Name ?? "", Margin = new Thickness(0, 2, 3, 2),
                Padding = new Thickness(7, 4, 7, 4)
            };
            System.Windows.Automation.AutomationProperties.SetName(nameBox, "入参名称");
            nameBox.TextChanged += WsRowChanged;
            Grid.SetColumn(nameBox, 1); row.Children.Add(nameBox);
            var valueBox = new TextBox
            {
                Text = parameter == null ? "" : parameter.Value ?? "", Margin = new Thickness(0, 2, 6, 2),
                Padding = new Thickness(7, 4, 7, 4)
            };
            System.Windows.Automation.AutomationProperties.SetName(valueBox, "入参值");
            valueBox.TextChanged += WsRowChanged;
            Grid.SetColumn(valueBox, 2); row.Children.Add(valueBox);
            var remove = new Button { Content = "✕", ToolTip = "删除该行", Padding = new Thickness(0), MinHeight = 26, Margin = new Thickness(0, 2, 0, 2) };
            remove.Click += RemoveWsRowClick;
            Grid.SetColumn(remove, 3); row.Children.Add(remove);
            WsRows.Children.Add(row);
            RenumberWsRows();
            return row;
        }
        private void AddWsRow()
        {
            var row = AddWsRow(null);
            ((TextBox)row.Children[1]).Focus();
            MarkDirtyAndSync();
        }
        private void AddWsRowClick(object sender, RoutedEventArgs e) { AddWsRow(); }
        private void RemoveWsRowClick(object sender, RoutedEventArgs e)
        {
            var row = FindAncestor<Grid>(sender as DependencyObject);
            if (row == null || row.Parent != WsRows) return;
            WsRows.Children.Remove(row);
            RenumberWsRows();
            MarkDirtyAndSync();
        }
        private void RenumberWsRows()
        {
            for (int i = 0; i < WsRows.Children.Count; i++)
                ((TextBlock)((Grid)WsRows.Children[i]).Children[0]).Text = (i + 1).ToString();
        }
        private List<WsParam> CollectWsRows()
        {
            var result = new List<WsParam>();
            foreach (var child in WsRows.Children)
            {
                var row = child as Grid;
                if (row == null || row.Children.Count < 3) continue;
                result.Add(new WsParam
                {
                    Name = ((TextBox)row.Children[1]).Text,
                    Value = ((TextBox)row.Children[2]).Text
                });
            }
            return result;
        }
        private void WsRowChanged(object sender, TextChangedEventArgs e)
        {
            if (loading) return;
            MarkDirtyAndSync();
        }
        private void WsFieldChanged(object sender, TextChangedEventArgs e)
        {
            if (loading) return;
            MarkDirtyAndSync();
        }
        private void MarkDirtyAndSync()
        {
            if (loading || current == null) return;
            UpdateCurrent();
            RefreshSoapAction();
        }
        private void MarkDirty()
        {
            dirty = true;
            saveTimer.Stop(); saveTimer.Start();
            Footer.Text = "正在编辑  ·  即将自动保存";
        }
        private bool SaveWorkspace(bool explicitSave)
        {
            if (explicitSave) NormalizeTitle();
            saveTimer.Stop();
            if (!dirty && !explicitSave) return true;
            try
            {
                store.Save(workspace);
                dirty = false;
                Footer.Text = "✓ 已保存到本地  " + DateTime.Now.ToString("HH:mm:ss");
                return true;
            }
            catch (Exception ex)
            {
                Footer.Text = "保存失败：" + ex.Message;
                if (explicitSave) Warn("无法保存工作空间：\r\n" + ex.Message, "保存失败");
                return false;
            }
        }
        private void SaveClick(object sender, RoutedEventArgs e) { SaveWorkspace(true); }

        private async Task SendRequest()
        {
            if (requestCancellation != null) return;
            if (current == null) { Warn("请先新建或选择一个接口。", "提示"); return; }
            if (RequestTypes.IsSoap(SelectedRequestType))
            {
                try { SoapMethodXml(); }
                catch (Exception ex)
                {
                    Warn("WebService 入参无法生成报文：" + ex.Message + "\r\n请检查方法名和入参名称是否为合法的 XML 名称。", "入参有误");
                    return;
                }
            }
            UpdateCurrent();
            var target = current;
            var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            requestCancellation = cancellation;
            SendButton.IsEnabled = false; SendButton.Content = "发送中…"; CancelButton.IsEnabled = true;
            ResponseInfo.Text = "请求中…"; ResponseInfo.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            ResponseMeta.Text = "正在连接服务器";
            Footer.Text = "正在发送 " + target.Method + " 请求  ·  可点击取消终止";
            try
            {
                var result = await service.SendAsync(target, cancellation.Token);
                target.Response = result.Body; target.ResponseHeaders = result.Headers;
                target.StatusCode = result.StatusCode; target.StatusText = result.StatusText;
                target.ElapsedMs = result.ElapsedMs; target.ResponseBytes = result.Bytes;
            }
            catch (OperationCanceledException)
            {
                RecordFailure(target, "请求已取消 / 超时", "请求已取消，或已达到 60 秒超时。\r\n请检查服务器地址和网络后重试。");
            }
            catch (Exception ex)
            {
                RecordFailure(target, "请求失败", "请求失败\r\n\r\n" + ex.Message + (ex.InnerException == null ? "" : "\r\n" + ex.InnerException.Message));
            }
            finally
            {
                cancellation.Dispose();
                requestCancellation = null;
                if (!closed)
                {
                    target.LastRun = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    SendButton.IsEnabled = true; SendButton.Content = "发送  ↗"; CancelButton.IsEnabled = false;
                    if (current == target) ShowResponse(target);
                    dirty = true;
                    SaveWorkspace(false);
                }
            }
        }
        private static void RecordFailure(ApiItem target, string status, string message)
        {
            target.StatusText = status; target.StatusCode = 0; target.Response = message;
            target.ResponseHeaders = ""; target.ElapsedMs = 0; target.ResponseBytes = 0;
        }
        private async void SendClick(object sender, RoutedEventArgs e) { await SendRequest(); }
        private void CancelClick(object sender, RoutedEventArgs e) { if (requestCancellation != null) requestCancellation.Cancel(); }
        private void ShowResponse(ApiItem item)
        {
            string body = item.Response ?? "";
            bool xml = XmlText.LooksLikeXml(body);
            if (body.Length <= 200000) { try { body = xml ? XmlText.Format(body) : JsonText.Format(body); } catch (Exception) { } }
            responseBody.UseJsonSyntax = !xml;
            FormatResponseButton.Content = xml ? "格式化 XML" : "格式化 JSON";
            responseBody.Content = body;
            ResponseHeaders.Text = item.ResponseHeaders ?? "";
            ResponseInfo.Text = item.StatusCode > 0 ? item.StatusCode + " " + item.StatusText : string.IsNullOrEmpty(item.StatusText) ? "等待发送" : item.StatusText;
            ResponseInfo.SetResourceReference(TextBlock.ForegroundProperty, item.StatusCode >= 200 && item.StatusCode < 400 ? "Accent" :
                item.StatusCode >= 400 || item.StatusText == "请求失败" ? "Error" : "Muted");
            string bytes = item.ResponseBytes < 1024 ? item.ResponseBytes + " B" :
                item.ResponseBytes < 1048576 ? (item.ResponseBytes / 1024d).ToString("0.0") + " KB" : (item.ResponseBytes / 1048576d).ToString("0.0") + " MB";
            ResponseMeta.Text = string.IsNullOrEmpty(item.LastRun) ? "点击发送，获取真实响应" : item.ElapsedMs + " ms    ·    " + bytes + "    ·    " + item.LastRun;
        }

        private void RequestTabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!loading && e.Source == RequestTabs && RequestTabs.SelectedIndex == 2) RefreshQuery();
        }
        private void RefreshQuery()
        {
            Uri uri;
            if (!Uri.TryCreate(AddressBox.Text, UriKind.Absolute, out uri)) { QueryBox.Text = ""; return; }
            var lines = new List<string>();
            foreach (string pair in uri.Query.TrimStart('?').Split('&'))
            {
                if (pair.Length == 0) continue;
                int equals = pair.IndexOf('=');
                string key = equals < 0 ? pair : pair.Substring(0, equals);
                string value = equals < 0 ? "" : pair.Substring(equals + 1);
                lines.Add(WebUtility.UrlDecode(key) + "=" + WebUtility.UrlDecode(value));
            }
            QueryBox.Text = string.Join(Environment.NewLine, lines);
        }
        private void ApplyQueryClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var builder = new UriBuilder(AddressBox.Text);
                if (builder.Scheme != "http" && builder.Scheme != "https") throw new ArgumentException("请输入 HTTP 或 HTTPS 地址。");
                var pairs = new List<string>();
                foreach (string line in QueryBox.Text.Split('\n'))
                {
                    string pair = line.TrimEnd('\r');
                    if (string.IsNullOrWhiteSpace(pair)) continue;
                    int equals = pair.IndexOf('=');
                    if (equals <= 0) throw new ArgumentException("参数格式应为 key=value：" + pair);
                    pairs.Add(Uri.EscapeDataString(pair.Substring(0, equals).Trim()) + "=" + Uri.EscapeDataString(pair.Substring(equals + 1)));
                }
                builder.Query = string.Join("&", pairs); AddressBox.Text = builder.Uri.AbsoluteUri;
                Footer.Text = "Query 参数已应用到请求地址";
            }
            catch (Exception ex) { Warn(ex.Message, "参数无法应用"); }
        }

        private void FormatEditor(JsonEditor editor, bool compact)
        {
            bool xml = !editor.UseJsonSyntax;
            string format = xml ? "XML" : "JSON";
            if (string.IsNullOrWhiteSpace(editor.Content)) { Footer.Text = "编辑区为空，请先输入 " + format + " 内容"; return; }
            try
            {
                editor.ReplaceContent(xml ? (compact ? XmlText.Compact(editor.Content) : XmlText.Format(editor.Content)) :
                    (compact ? JsonText.Compact(editor.Content) : JsonText.Format(editor.Content)));
                if (editor == responseBody && current != null) { current.Response = editor.Content; MarkDirty(); }
                Footer.Text = format + (compact ? " 已压缩" : " 已格式化");
            }
            catch (Exception ex) { Warn("内容不是有效的 " + format + "：\r\n" + ex.Message, format + " 格式错误"); }
        }
        private void FormatRequestClick(object sender, RoutedEventArgs e) { FormatEditor(requestBody, false); }
        private void CompactRequestClick(object sender, RoutedEventArgs e) { FormatEditor(requestBody, true); }
        private void FormatResponseClick(object sender, RoutedEventArgs e) { FormatEditor(responseBody, false); }
        private string CurrentResponseText() { return ResponseTabs.SelectedIndex == 1 ? ResponseHeaders.Text : responseBody.Content; }
        private void CopyResponseClick(object sender, RoutedEventArgs e)
        {
            string content = CurrentResponseText();
            if (content.Length == 0) return;
            try { Clipboard.SetText(content); Footer.Text = "已复制响应内容"; }
            catch (Exception ex) { Warn(ex.Message, "复制失败"); }
        }
        private void SaveResponseClick(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Title = "保存响应内容", Filter = "XML 文件 (*.xml)|*.xml|JSON 文件 (*.json)|*.json|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                FilterIndex = !responseBody.UseJsonSyntax && ResponseTabs.SelectedIndex == 0 ? 1 : 2,
                FileName = ResponseTabs.SelectedIndex == 1 ? "response-headers.txt" : !responseBody.UseJsonSyntax ? "response.xml" : "response.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { File.WriteAllText(dialog.FileName, CurrentResponseText(), new UTF8Encoding(false)); Footer.Text = "响应已保存"; }
            catch (Exception ex) { Warn(ex.Message, "保存失败"); }
        }

        private void HandleShortcuts(object sender, KeyEventArgs e)
        {
            if (DialogHost.Content != null) return;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && RunShortcut(e.Key)) e.Handled = true;
            if (!CollectionTree.IsKeyboardFocusWithin) return;
            if (e.Key == Key.F2) { RenameItem(); e.Handled = true; }
            if (e.Key == Key.Apps || e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift)
            {
                OpenTreeMenu(CollectionTree.SelectedItem as TreeViewItem, PlacementMode.Bottom);
                e.Handled = true;
            }
        }
        private bool RunShortcut(Key key)
        {
            if (key == Key.S) SaveWorkspace(true);
            else if (key == Key.N) AddItem(false);
            else if (key == Key.Enter) { var request = SendRequest(); }
            else return false;
            return true;
        }
        private void Warn(string message, string title)
        {
            StudioDialog.CreateMessage(this, message, title, false).ShowModal();
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            var dialog = DialogHost.Content as StudioDialog;
            if (dialog != null)
            {
                dialog.Close();
                e.Cancel = true;
                base.OnClosing(e);
                return;
            }
            if (dirty && !SaveWorkspace(true)) e.Cancel = true;
            base.OnClosing(e);
        }
        protected override void OnClosed(EventArgs e)
        {
            closed = true;
            saveTimer.Stop();
            if (requestCancellation != null) requestCancellation.Cancel();
            service.Dispose();
            base.OnClosed(e);
        }
    }
}
