using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ApiStudio
{
    public partial class StudioDialog : UserControl
    {
        private readonly Window owner;
        private readonly string title;
        private DispatcherFrame modalFrame;
        private Window standalone;
        private bool accepted, completed;
        private readonly TranslateTransform position = new TranslateTransform();

        internal StudioDialog(Window owner, string title)
        {
            this.owner = owner;
            this.title = title;
            InitializeComponent();
            DialogTitle.Text = title;
            if (owner != null)
            {
                Resources = new ResourceDictionary();
                Resources.MergedDictionaries.Add(owner.Resources);
            }
            else Theme.ApplyResources(Resources);
            MaxHeight = SystemParameters.WorkArea.Height - 48;
            DialogConfirmButton.Click += delegate { Complete(true); };
            DialogCancelButton.Click += delegate { Close(); };
            DialogCloseButton.Click += delegate { Close(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            };
            SizeChanged += delegate { ClampPosition(); };
        }

        private void DialogDragDelta(object sender, DragDeltaEventArgs e)
        {
            if (standalone != null)
            {
                standalone.Left += e.HorizontalChange;
                standalone.Top += e.VerticalChange;
            }
            else if (modalFrame != null)
            {
                position.X += e.HorizontalChange;
                position.Y += e.VerticalChange;
                ClampPosition();
            }
            e.Handled = true;
        }

        private void ClampPosition()
        {
            var studio = owner as StudioWindow;
            if (modalFrame == null || studio == null) return;
            double maxX = Math.Max(0, (studio.DialogShade.ActualWidth - ActualWidth) / 2 - 16);
            double maxY = Math.Max(0, (studio.DialogShade.ActualHeight - ActualHeight) / 2 - 16);
            position.X = Math.Max(-maxX, Math.Min(maxX, position.X));
            position.Y = Math.Max(-maxY, Math.Min(maxY, position.Y));
        }

        internal static StudioDialog CreateMessage(Window owner, string message, string title, bool confirm)
        {
            var dialog = new StudioDialog(owner, title);
            dialog.DialogBody.Content = new ScrollViewer
            {
                Style = (Style)dialog.FindResource("StudioScrollViewer"),
                MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 23 }
            };
            dialog.DialogIcon.Stroke = new SolidColorBrush(confirm ? Theme.Error : Color.FromRgb(225, 163, 56));
            dialog.DialogIconBadge.Background = new SolidColorBrush(confirm ? Color.FromArgb(24, 225, 93, 100) : Color.FromArgb(24, 225, 163, 56));
            if (confirm)
            {
                dialog.DialogConfirmButton.Content = "删除";
                dialog.DialogConfirmButton.Style = (Style)dialog.FindResource("DangerButton");
                dialog.DialogConfirmButton.IsDefault = false;
                dialog.DialogCancelButton.IsDefault = true;
                dialog.Loaded += delegate { dialog.DialogCancelButton.Focus(); };
            }
            else
            {
                dialog.DialogCancelButton.Visibility = Visibility.Collapsed;
                dialog.DialogConfirmButton.Content = "知道了";
                dialog.Loaded += delegate { dialog.DialogConfirmButton.Focus(); };
            }
            return dialog;
        }

        internal bool ShowModal()
        {
            accepted = completed = false;
            var studio = owner as StudioWindow;
            return studio != null && studio.IsVisible ? ShowEmbedded(studio) : ShowStandalone();
        }

        private bool ShowEmbedded(StudioWindow studio)
        {
            if (studio.DialogHost.Content != null) throw new InvalidOperationException("已有对话框正在显示。");
            var focus = Keyboard.FocusedElement;
            bool workspaceInput = studio.WorkspaceGrid.IsHitTestVisible, titleInput = studio.TitleBar.IsHitTestVisible;
            modalFrame = new DispatcherFrame();
            SizeChangedEventHandler resized = delegate { ClampPosition(); };
            studio.DialogShade.SizeChanged += resized;
            try
            {
                // Render the card and shade in the existing surface. No extra HWND,
                // owner disable/enable, activation change or first-frame window erase.
                MaxHeight = Math.Max(160, studio.WindowRoot.ActualHeight - 64);
                studio.WorkspaceGrid.IsHitTestVisible = false;
                studio.TitleBar.IsHitTestVisible = false;
                position.X = position.Y = 0;
                RenderTransform = position;
                studio.DialogHost.Content = this;
                studio.DialogShade.Visibility = Visibility.Visible;
                ComponentDispatcher.PushModal();
                try { Dispatcher.PushFrame(modalFrame); }
                finally { ComponentDispatcher.PopModal(); }
                return accepted;
            }
            finally
            {
                modalFrame = null;
                studio.DialogShade.SizeChanged -= resized;
                studio.DialogShade.Visibility = Visibility.Collapsed;
                studio.DialogHost.Content = null;
                studio.WorkspaceGrid.IsHitTestVisible = workspaceInput;
                studio.TitleBar.IsHitTestVisible = titleInput;
                RenderTransform = Transform.Identity;
                if (studio.IsVisible && focus != null) Keyboard.Focus(focus);
            }
        }

        private bool ShowStandalone()
        {
            // Startup errors have no visible application surface to host a card.
            Measure(new Size(Width, MaxHeight));
            standalone = new Window
            {
                Title = title + " · API Studio", Content = this, Width = Width, Height = DesiredSize.Height,
                Background = (Brush)FindResource("Surface"), ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            if (owner != null && owner.IsVisible)
            {
                standalone.Owner = owner;
                standalone.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            StudioChrome.Attach(standalone, 48, false);
            try { standalone.ShowDialog(); return accepted; }
            finally { standalone.Content = null; standalone = null; }
        }

        internal void Close() { Complete(false); }
        private void Complete(bool result)
        {
            if (completed) return;
            completed = true;
            accepted = result;
            if (DialogDragHandle.IsDragging) DialogDragHandle.CancelDrag();
            if (modalFrame != null) modalFrame.Continue = false;
            if (standalone != null) standalone.Close();
        }
    }
}
