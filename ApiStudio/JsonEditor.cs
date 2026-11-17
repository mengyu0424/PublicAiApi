using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ApiStudio
{
    // Syntax is painted in a separate WPF visual. Coloring never edits the text
    // document, so the TextBox keeps its native undo, selection and IME behavior.
    public sealed class JsonEditor : Grid
    {
        private readonly JsonTextLayer textLayer, gutter;
        private readonly DispatcherTimer highlightTimer;
        private List<JsonToken> tokens = new List<JsonToken>();
        private string highlightedText;
        private bool composing, syntaxVisible, renderUpdatePending;
        private bool useJsonSyntax = true;
        public TextBox TextBox { get; private set; }
        public bool UseJsonSyntax
        {
            get { return useJsonSyntax; }
            set
            {
                if (useJsonSyntax == value) return;
                useJsonSyntax = value;
                ((JsonTextBox)TextBox).SelectJsonValues = value;
                Highlight();
            }
        }
        public string Content
        {
            get { return TextBox.Text; }
            set
            {
                value = value ?? "";
                if (TextBox.Text != value) TextBox.Text = value;
                TextBox.IsUndoEnabled = false;
                TextBox.IsUndoEnabled = true;
                TextBox.Select(0, 0);
                TextBox.ScrollToHome();
                Highlight();
            }
        }

        public JsonEditor() : this(false) { }
        public JsonEditor(bool readOnly)
        {
            ClipToBounds = true;
            SetResourceReference(BackgroundProperty, "Surface");
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            ColumnDefinitions.Add(new ColumnDefinition());
            TextBox = new JsonTextBox
            {
                AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap,
                IsReadOnly = readOnly, IsReadOnlyCaretVisible = true, IsInactiveSelectionHighlightEnabled = true,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"), FontSize = 14,
                Padding = new Thickness(12, 10, 12, 8), BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Foreground = Frozen(Theme.Ink),
                SelectionTextBrush = Frozen(Theme.Ink), SelectionOpacity = .35, UndoLimit = 200,
                VerticalContentAlignment = VerticalAlignment.Top, FlowDirection = FlowDirection.LeftToRight
            };
            TextOptions.SetTextFormattingMode(TextBox, TextFormattingMode.Display);
            // Avoid inheriting the single-line input template used elsewhere.
            var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            host.SetValue(ScrollViewer.FocusableProperty, false);
            TextBox.Template = new ControlTemplate(typeof(TextBox)) { VisualTree = host };
            TextBox.SetResourceReference(TextBoxBase.CaretBrushProperty, "Ink");
            TextBox.SetResourceReference(TextBoxBase.SelectionBrushProperty, "Accent");
            SetColumn(TextBox, 1); Children.Add(TextBox);
            gutter = new JsonTextLayer(this, true);
            Children.Add(gutter);
            textLayer = new JsonTextLayer(this, false);
            SetColumn(textLayer, 1); Children.Add(textLayer);
            highlightTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                { Interval = TimeSpan.FromMilliseconds(180) };
            highlightTimer.Tick += delegate { Highlight(); };
            TextBox.TextChanged += delegate
            {
                highlightedText = null;
                RefreshTextRendering();
                highlightTimer.Stop();
                if (TextBox.Text.Length <= 200000 && IsLoaded) highlightTimer.Start();
                Redraw();
            };
            TextBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(delegate { Redraw(); }));
            TextBox.SizeChanged += delegate { Redraw(); };
            Loaded += delegate { Highlight(); };
            Unloaded += delegate { highlightTimer.Stop(); };
            TextCompositionManager.AddPreviewTextInputStartHandler(TextBox, delegate { SetComposing(true); });
            TextCompositionManager.AddPreviewTextInputHandler(TextBox, delegate
            {
                if (composing) Dispatcher.BeginInvoke(new Action(delegate { SetComposing(false); Highlight(); }), DispatcherPriority.Input);
            });
            TextBox.LostKeyboardFocus += delegate { SetComposing(false); };
            var menu = new ContextMenu();
            AddCommand(menu, "撤销", ApplicationCommands.Undo);
            AddCommand(menu, "重做", ApplicationCommands.Redo);
            menu.Items.Add(new Separator());
            AddCommand(menu, "剪切", ApplicationCommands.Cut);
            AddCommand(menu, "复制", ApplicationCommands.Copy);
            AddCommand(menu, "粘贴", ApplicationCommands.Paste);
            menu.Items.Add(new Separator());
            AddCommand(menu, "全选", ApplicationCommands.SelectAll);
            TextBox.ContextMenu = menu;
        }

        private void AddCommand(ContextMenu menu, string title, ICommand command)
        { menu.Items.Add(new MenuItem { Header = title, Command = command, CommandTarget = TextBox }); }
        private void SetComposing(bool value)
        {
            composing = value;
            // Let WPF draw the live composition and its IME decorations itself.
            RefreshTextRendering();
            Redraw();
        }
        private void RefreshTextRendering()
        {
            // TextChanged runs before TextBox has laid out the new characters.
            // Keep native text visible until both syntax and layout are ready;
            // restarting the highlight timer must never make typed text disappear.
            bool ready = !composing && highlightedText == TextBox.Text && TextBox.Text.Length <= 200000;
            syntaxVisible = ready && TextBox.IsArrangeValid;
            Brush foreground = syntaxVisible ? Brushes.Transparent : Frozen(Theme.Ink);
            var current = TextBox.Foreground as SolidColorBrush;
            if (current == null || current.Color != ((SolidColorBrush)foreground).Color) TextBox.Foreground = foreground;
            current = TextBox.SelectionTextBrush as SolidColorBrush;
            if (current == null || current.Color != ((SolidColorBrush)foreground).Color) TextBox.SelectionTextBrush = foreground;
            if (ready && !syntaxVisible) QueueRenderUpdate();
        }
        private void QueueRenderUpdate()
        {
            if (renderUpdatePending || !IsLoaded || !TextBox.IsVisible) return;
            renderUpdatePending = true;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                renderUpdatePending = false;
                if (!IsLoaded || !TextBox.IsVisible) return;
                TextBox.UpdateLayout();
                RefreshTextRendering();
                Redraw();
            }), DispatcherPriority.Loaded);
        }
        public void ReplaceContent(string text)
        {
            // Formatting is one undoable edit. Loading another request uses Content.
            TextBox.BeginChange();
            try { TextBox.SelectAll(); TextBox.SelectedText = text ?? ""; TextBox.Select(0, 0); }
            finally { TextBox.EndChange(); }
            TextBox.ScrollToHome();
            Highlight();
        }
        public void Highlight()
        {
            highlightTimer.Stop();
            string text = TextBox.Text;
            tokens = text.Length <= 200000 ? (useJsonSyntax ? JsonSyntax.Scan(text) : XmlSyntax.Scan(text)) : new List<JsonToken>();
            highlightedText = text;
            RefreshTextRendering();
            Redraw();
        }
        internal void RefreshTheme() { RefreshTextRendering(); Redraw(); }
        private void Redraw() { textLayer.InvalidateVisual(); gutter.InvalidateVisual(); }
        private static SolidColorBrush Frozen(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
        private static Brush TokenBrush(JsonToken token)
        {
            switch (token.Kind)
            {
                case JsonTokenKind.Key: return Frozen(Theme.JsonKey);
                case JsonTokenKind.XmlNode: return Frozen(Theme.JsonKey);
                case JsonTokenKind.String: return Frozen(Theme.JsonString);
                case JsonTokenKind.Number: return Frozen(Theme.JsonNumber);
                case JsonTokenKind.XmlAttribute: return Frozen(Theme.JsonNumber);
                case JsonTokenKind.XmlMarkup:
                case JsonTokenKind.XmlComment: return Frozen(Theme.Muted);
                case JsonTokenKind.Bracket: return Frozen(Theme.BracketColor(token.Depth));
                case JsonTokenKind.Error: return Frozen(Theme.Error);
                default: return Frozen(Theme.Ink);
            }
        }
        private FormattedText Format(string text, Brush brush)
        {
            return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(TextBox.FontFamily, TextBox.FontStyle, TextBox.FontWeight, TextBox.FontStretch),
                TextBox.FontSize, brush, null, TextFormattingMode.Display, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }
        private void Draw(DrawingContext drawing, bool numbers, Size size)
        {
            var editor = TextBox;
            if (!editor.IsVisible) return;
            if (!editor.IsArrangeValid) { QueueRenderUpdate(); return; }
            int first = editor.GetFirstVisibleLineIndex(), last = editor.GetLastVisibleLineIndex();
            if (first < 0 || last < first) return;
            string text = editor.Text;
            var scroll = editor.Template.FindName("PART_ContentHost", editor) as ScrollViewer;
            double width = editor.ActualWidth - (scroll != null && scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible ? SystemParameters.VerticalScrollBarWidth : 0);
            double height = editor.ActualHeight - (scroll != null && scroll.ComputedHorizontalScrollBarVisibility == Visibility.Visible ? SystemParameters.HorizontalScrollBarHeight : 0);
            drawing.PushClip(new RectangleGeometry(new Rect(0, 0, numbers ? size.Width : Math.Max(0, width), Math.Max(0, height))));
            for (int line = first; line <= last; line++)
            {
                int start = editor.GetCharacterIndexFromLineIndex(line);
                if (start < 0 || start > text.Length) continue;
                Rect position = editor.GetRectFromCharacterIndex(start);
                if (position.IsEmpty) continue;
                if (numbers)
                {
                    var number = Format((line + 1).ToString(CultureInfo.InvariantCulture), Frozen(Theme.Muted));
                    drawing.DrawText(number, new Point(size.Width - number.Width - 9, position.Top));
                    continue;
                }
                if (!syntaxVisible) continue;
                int end = Math.Min(text.Length, start + editor.GetLineLength(line));
                while (end > start && (text[end - 1] == '\r' || text[end - 1] == '\n')) end--;
                // Paint only the visible slice, including when a huge response is one line.
                int left = editor.GetCharacterIndexFromPoint(new Point(0, position.Top + position.Height / 2), true);
                int right = editor.GetCharacterIndexFromPoint(new Point(width, position.Top + position.Height / 2), true);
                start = Math.Max(start, left - 2);
                end = Math.Min(end, right + 3);
                if (end <= start) continue;
                // Tabs are positioned by the TextBox formatter, so each tab-delimited
                // segment starts at the actual caret rectangle instead of an estimate.
                for (int segment = start; segment < end;)
                {
                    if (text[segment] == '\t') { segment++; continue; }
                    int stop = text.IndexOf('\t', segment, end - segment);
                    if (stop < 0) stop = end;
                    Rect origin = editor.GetRectFromCharacterIndex(segment);
                    if (!origin.IsEmpty)
                    {
                        var formatted = Format(text.Substring(segment, stop - segment), Frozen(Theme.Ink));
                        if (highlightedText == text)
                            foreach (var token in tokens)
                            {
                                if (token.Start >= stop) break;
                                int a = Math.Max(segment, token.Start), b = Math.Min(stop, token.Start + token.Length);
                                if (b > a) formatted.SetForegroundBrush(TokenBrush(token), a - segment, b - a);
                            }
                        drawing.DrawText(formatted, origin.TopLeft);
                    }
                    segment = stop;
                }
            }
            drawing.Pop();
        }
        private sealed class JsonTextLayer : FrameworkElement
        {
            private readonly JsonEditor owner;
            private readonly bool numbers;
            public JsonTextLayer(JsonEditor editor, bool isGutter) { owner = editor; numbers = isGutter; IsHitTestVisible = false; }
            protected override void OnRender(DrawingContext drawing)
            {
                if (numbers) drawing.DrawRectangle(Frozen(Theme.Subtle), null, new Rect(RenderSize));
                owner.Draw(drawing, numbers, RenderSize);
            }
        }
    }
    internal sealed class JsonTextBox : TextBox
    {
        internal bool SelectJsonValues = true;
        internal bool SelectTokenAt(int index)
        {
            if (!SelectJsonValues) return false;
            int start, length;
            if (!JsonSyntax.SelectionAt(Text, index, out start, out length)) return false;
            Select(start, length); return true;
        }
        protected override void OnPreviewMouseDoubleClick(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && SelectTokenAt(GetCharacterIndexFromPoint(e.GetPosition(this), true)))
            { Focus(); e.Handled = true; }
            base.OnPreviewMouseDoubleClick(e);
        }
    }
}
