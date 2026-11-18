using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
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
            host.SetResourceReference(FrameworkElement.StyleProperty, "StudioScrollViewer");
            TextBox.Template = new ControlTemplate(typeof(TextBox)) { VisualTree = host };
            TextBox.SetResourceReference(TextBoxBase.CaretBrushProperty, "Ink");
            TextBox.SetResourceReference(TextBoxBase.SelectionBrushProperty, "Accent");
            SetColumn(TextBox, 1); Children.Add(TextBox);
            gutter = new JsonTextLayer(this, true);
            Children.Add(gutter);
            textLayer = new JsonTextLayer(this, false);
            SetColumn(textLayer, 1); Children.Add(textLayer);
            // Keep the syntax snapshot current during typing. Switching to native
            // monochrome text until a debounce timer fires causes visible flashing.
            TextBox.TextChanged += delegate { Highlight(); };
            TextBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(delegate { Redraw(); }));
            TextBox.SizeChanged += delegate { Redraw(); };
            Loaded += delegate { Highlight(); };
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
            // Layout can lag behind TextChanged. The overlay retains its last
            // complete drawing until the new layout is ready instead of blinking.
            syntaxVisible = !composing && highlightedText == TextBox.Text && TextBox.Text.Length <= 200000;
            Brush foreground = syntaxVisible ? Brushes.Transparent : Frozen(Theme.Ink);
            var current = TextBox.Foreground as SolidColorBrush;
            if (current == null || current.Color != ((SolidColorBrush)foreground).Color) TextBox.Foreground = foreground;
            current = TextBox.SelectionTextBrush as SolidColorBrush;
            if (current == null || current.Color != ((SolidColorBrush)foreground).Color) TextBox.SelectionTextBrush = foreground;
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
        private bool Draw(DrawingContext drawing, bool numbers, Size size)
        {
            var editor = TextBox;
            if (!editor.IsVisible) return true;
            // LineCount validates the text view. Read it before the visible range:
            // scroll metrics may still describe the document before an edit.
            int lineCount = editor.LineCount;
            if (lineCount <= 0 || !editor.IsArrangeValid) { QueueRenderUpdate(); return false; }
            int first = editor.GetFirstVisibleLineIndex(), last = editor.GetLastVisibleLineIndex();
            last = Math.Min(last, editor.LineCount - 1);
            if (first < 0 || last < 0) { QueueRenderUpdate(); return false; }
            first = Math.Min(first, last);
            string text = editor.Text;
            var scroll = editor.Template.FindName("PART_ContentHost", editor) as ScrollViewer;
            var vertical = scroll == null ? null : scroll.Template.FindName("PART_VerticalScrollBar", scroll) as ScrollBar;
            var horizontal = scroll == null ? null : scroll.Template.FindName("PART_HorizontalScrollBar", scroll) as ScrollBar;
            double width = editor.ActualWidth - (vertical != null && vertical.IsVisible ? vertical.ActualWidth : 0);
            double height = editor.ActualHeight - (horizontal != null && horizontal.IsVisible ? horizontal.ActualHeight : 0);
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
            return true;
        }
        private sealed class JsonTextLayer : FrameworkElement
        {
            private readonly JsonEditor owner;
            private readonly bool numbers;
            private DrawingGroup lastDrawing;
            public JsonTextLayer(JsonEditor editor, bool isGutter) { owner = editor; numbers = isGutter; IsHitTestVisible = false; }
            protected override void OnRender(DrawingContext drawing)
            {
                // IME composition and oversized documents use native text.
                if (!numbers && !owner.syntaxVisible) { lastDrawing = null; return; }
                var next = new DrawingGroup();
                bool complete;
                using (var context = next.Open())
                {
                    if (numbers) context.DrawRectangle(Frozen(Theme.Subtle), null, new Rect(RenderSize));
                    complete = owner.Draw(context, numbers, RenderSize);
                }
                if (complete || lastDrawing == null) lastDrawing = next;
                drawing.DrawDrawing(lastDrawing);
            }
        }
    }
    internal sealed class JsonTextBox : TextBox
    {
        internal bool SelectJsonValues = true;
        public JsonTextBox()
        {
            BindIndentCommand(EditingCommands.TabForward, false);
            BindIndentCommand(EditingCommands.TabBackward, true);
        }
        private void BindIndentCommand(RoutedCommand command, bool outdent)
        {
            CommandBindings.Add(new CommandBinding(command, delegate(object sender, ExecutedRoutedEventArgs e)
            {
                AdjustIndent(outdent);
                e.Handled = true;
            }, delegate(object sender, CanExecuteRoutedEventArgs e)
            {
                e.CanExecute = !IsReadOnly && AcceptsTab;
                e.Handled = true;
            }));
        }
        private void AdjustIndent(bool outdent)
        {
            int start = SelectionStart, end = start + SelectionLength;
            if (!outdent && start == end)
            {
                ReplaceAndSelect(start, 0, "  ", start + 2, 0);
                return;
            }
            string text = Text;
            int firstLine = FindLineStart(text, start);
            int lastPosition = end;
            // A selection ending at column zero belongs to the preceding line.
            if (end > start && (text[end - 1] == '\r' || text[end - 1] == '\n'))
            {
                lastPosition--;
                if (text[lastPosition] == '\n' && lastPosition > 0 && text[lastPosition - 1] == '\r') lastPosition--;
            }
            int lastLine = FindLineStart(text, lastPosition);
            int rangeEnd = lastLine;
            while (rangeEnd < text.Length && text[rangeEnd] != '\r' && text[rangeEnd] != '\n') rangeEnd++;
            var replacement = new StringBuilder();
            int newStart = start, newEnd = end;
            bool changed = false;
            for (int line = firstLine; ;)
            {
                int lineEnd = line;
                while (lineEnd < rangeEnd && text[lineEnd] != '\r' && text[lineEnd] != '\n') lineEnd++;
                int removed = 0;
                if (outdent)
                {
                    if (line < lineEnd && text[line] == '\t') removed = 1;
                    else while (removed < 2 && line + removed < lineEnd && text[line + removed] == ' ') removed++;
                    newStart -= Math.Min(removed, Math.Max(0, start - line));
                    newEnd -= Math.Min(removed, Math.Max(0, end - line));
                    changed |= removed > 0;
                }
                else
                {
                    replacement.Append("  ");
                    if (line < start) newStart += 2;
                    newEnd += 2;
                    changed = true;
                }
                replacement.Append(text, line + removed, lineEnd - line - removed);
                if (line == lastLine) break;
                int next = lineEnd + 1;
                if (text[lineEnd] == '\r' && next < text.Length && text[next] == '\n') next++;
                replacement.Append(text, lineEnd, next - lineEnd);
                line = next;
            }
            if (changed) ReplaceAndSelect(firstLine, rangeEnd - firstLine, replacement.ToString(), newStart, newEnd - newStart);
        }
        private static int FindLineStart(string text, int index)
        {
            while (index > 0 && text[index - 1] != '\r' && text[index - 1] != '\n') index--;
            return index;
        }
        private void ReplaceAndSelect(int start, int length, string replacement, int selectionStart, int selectionLength)
        {
            BeginChange();
            try
            {
                Select(start, length);
                SelectedText = replacement;
                Select(selectionStart, selectionLength);
            }
            finally { EndChange(); }
        }
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            // Leave modified shortcuts and IME-processed keys to WPF.
            if (!e.Handled && !IsReadOnly && AcceptsReturn && e.Key == Key.Return && Keyboard.Modifiers == ModifierKeys.None)
            {
                InsertIndentedNewLine();
                e.Handled = true;
            }
            base.OnPreviewKeyDown(e);
        }
        private void InsertIndentedNewLine()
        {
            string text = Text;
            int start = SelectionStart, end = start + SelectionLength;
            int lineStart = start;
            while (lineStart > 0 && text[lineStart - 1] != '\r' && text[lineStart - 1] != '\n') lineStart--;
            int indentEnd = lineStart;
            while (indentEnd < start && IsIndentSpace(text[indentEnd])) indentEnd++;
            string indent = text.Substring(lineStart, indentEnd - lineStart);
            int before = start, after = end;
            while (before > lineStart && IsIndentSpace(text[before - 1])) before--;
            while (after < text.Length && IsIndentSpace(text[after])) after++;
            char opening = before > lineStart ? text[before - 1] : '\0';
            bool opensBlock = "{[(".IndexOf(opening) >= 0 && QuoteAt(text, start) == '\0';
            bool splitPair = opensBlock && after < text.Length && text[after] == ClosingSymbol(opening);
            string innerIndent = indent + (opensBlock ? (indent.IndexOf('\t') >= 0 ? "\t" : "  ") : "");
            string newline = Environment.NewLine;
            int breakIndex = text.IndexOfAny(new[] { '\r', '\n' });
            if (breakIndex >= 0)
                newline = text[breakIndex] == '\r' && breakIndex + 1 < text.Length && text[breakIndex + 1] == '\n'
                    ? "\r\n" : text[breakIndex].ToString();
            string inserted = newline + innerIndent;
            int replaceStart = splitPair ? before : start;
            int replaceEnd = splitPair ? after : end;
            int caret = replaceStart + inserted.Length;
            if (splitPair) inserted += newline + indent;
            // Whitespace, line breaks and the caret move form one undoable edit.
            BeginChange();
            try
            {
                Select(replaceStart, replaceEnd - replaceStart);
                SelectedText = inserted;
                Select(caret, 0);
            }
            finally { EndChange(); }
        }
        private static bool IsIndentSpace(char c) { return c == ' ' || c == '\t'; }
        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            if (!IsReadOnly && e.Text.Length == 1 && "{}[]()\"'".IndexOf(e.Text[0]) >= 0)
            {
                char symbol = e.Text[0];
                string text = Text;
                int start = SelectionStart, length = SelectionLength;
                char quote = QuoteAt(text, start);
                bool escaped = IsEscaped(text, start);
                if (length == 0 && start < text.Length && text[start] == symbol &&
                    ((quote == '\0' && "}])".IndexOf(symbol) >= 0) || (quote == symbol && !escaped)))
                {
                    Select(start + 1, 0);
                    e.Handled = true;
                }
                else
                {
                    char closing = ClosingSymbol(symbol);
                    if (closing != '\0' && (length > 0 || quote == '\0' && !escaped &&
                        (start == text.Length || char.IsWhiteSpace(text[start]) || "}]),:".IndexOf(text[start]) >= 0)))
                    {
                        // A pair (or wrapping a selection) is one native undo unit.
                        string selected = SelectedText;
                        BeginChange();
                        try
                        {
                            SelectedText = symbol + selected + closing;
                            Select(start + 1, selected.Length);
                        }
                        finally { EndChange(); }
                        e.Handled = true;
                    }
                }
            }
            base.OnPreviewTextInput(e);
        }
        private static char ClosingSymbol(char opening)
        {
            switch (opening)
            {
                case '{': return '}';
                case '[': return ']';
                case '(': return ')';
                case '"': return '"';
                case '\'': return '\'';
                default: return '\0';
            }
        }
        private static bool IsEscaped(string text, int index)
        {
            int slashes = 0;
            while (index > 0 && text[--index] == '\\') slashes++;
            return slashes % 2 != 0;
        }
        private static char QuoteAt(string text, int end)
        {
            char quote = '\0';
            for (int i = 0; i < end; i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    if (c == '\\') i++;
                    else if (c == quote) quote = '\0';
                }
                else if (c == '"' || c == '\'') quote = c;
            }
            return quote;
        }
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
