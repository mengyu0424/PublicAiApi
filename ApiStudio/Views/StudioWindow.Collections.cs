using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace ApiStudio
{
    public partial class StudioWindow
    {
        private const string TreeDragFormat = "ApiStudio.TreeItem";
        private readonly Dictionary<ApiItem, TreeViewItem> nodes = new Dictionary<ApiItem, TreeViewItem>();
        private readonly Dictionary<ApiItem, ApiItem> parents = new Dictionary<ApiItem, ApiItem>();
        private bool rebuildingTree;
        private Point dragStart;
        private ApiItem dragCandidate, draggedItem;
        private TreeViewItem dropNode;
        private int lastDragScroll;

        private void RebuildTree(ApiItem selection)
        {
            rebuildingTree = true;
            try
            {
                CollectionTree.Items.Clear(); nodes.Clear(); parents.Clear();
                int requests = 0, folders = 0;
                string filter = SearchBox.Text.Trim();
                foreach (var item in workspace.Items)
                {
                    var node = MakeNode(item, null, filter, ref requests, ref folders);
                    if (node != null) CollectionTree.Items.Add(node);
                }
                CollectionCount.Text = requests + " 个接口   ·   " + folders + " 个文件夹";
            }
            finally { rebuildingTree = false; }
            if (selection != null) SelectItem(selection);
        }
        private TreeViewItem MakeNode(ApiItem item, ApiItem parent, string filter, ref int requests, ref int folders)
        {
            parents[item] = parent;
            if (item.IsFolder) folders++; else requests++;
            bool matches = filter.Length == 0 || item.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (!item.IsFolder && (item.Url ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            var node = new TreeViewItem { Tag = item, IsExpanded = true };
            foreach (var child in item.Children)
            {
                var childNode = MakeNode(child, item, matches ? "" : filter, ref requests, ref folders);
                if (childNode != null) node.Items.Add(childNode);
            }
            if (!matches && node.Items.Count == 0) return null;
            nodes[item] = node;
            RefreshNode(item);
            return node;
        }
        private void RefreshNode(ApiItem item)
        {
            TreeViewItem node;
            if (!nodes.TryGetValue(item, out node)) return;
            var header = new Grid { MinHeight = 34, Margin = new Thickness(0, 0, 9, 0) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(item.IsFolder ? 22 : 43) });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = item.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 1); header.Children.Add(name);
            if (item.IsFolder)
            {
                header.Children.Add(new System.Windows.Shapes.Path
                {
                    Style = (Style)FindResource("FolderIcon")
                });
                node.ToolTip = item.Name;
            }
            else
            {
                header.Children.Add(new TextBlock { Text = item.Method, Foreground = Brush(Theme.MethodColor(item.Method)), FontWeight = FontWeights.Bold, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
                string status = ApiStatus.Normalize(item.Status);
                var badge = new System.Windows.Shapes.Ellipse
                {
                    Width = 7, Height = 7, Fill = Brush(Theme.StatusColor(status)),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(badge, 2); header.Children.Add(badge);
                node.ToolTip = item.Name + "  ·  " + item.Method + "  ·  " + status;
            }
            var cue = new Border { IsHitTestVisible = false, CornerRadius = new CornerRadius(4) };
            cue.SetResourceReference(Border.BorderBrushProperty, "Accent");
            Grid.SetColumnSpan(cue, 3);
            header.Children.Add(cue);
            node.Header = header;
        }

        private string FolderPath(ApiItem item)
        {
            var names = new Stack<string>();
            ApiItem parent;
            while (parents.TryGetValue(item, out parent) && parent != null)
            {
                names.Push(parent.Name); item = parent;
            }
            return names.Count == 0 ? "我的接口" : string.Join("  /  ", names);
        }
        private ApiItem ParentOf(ApiItem item)
        {
            ApiItem parent;
            return item != null && parents.TryGetValue(item, out parent) ? parent : null;
        }
        private ApiItem SelectedItem { get { var node = CollectionTree.SelectedItem as TreeViewItem; return node == null ? null : node.Tag as ApiItem; } }
        private void SelectItem(ApiItem item)
        {
            TreeViewItem node;
            if (!nodes.TryGetValue(item, out node)) return;
            node.IsSelected = true;
            node.BringIntoView();
            if (!item.IsFolder && current != item) LoadItem(item);
        }
        private ApiItem FirstRequest(IEnumerable<ApiItem> items)
        {
            foreach (var item in items)
            {
                if (!item.IsFolder && nodes.ContainsKey(item)) return item;
                var child = FirstRequest(item.Children);
                if (child != null) return child;
            }
            return null;
        }
        private void SelectFirstRequest()
        {
            var item = FirstRequest(workspace.Items);
            if (item == null) ClearEditor(); else SelectItem(item);
        }
        private void SearchChanged(object sender, TextChangedEventArgs e)
        {
            if (workspace != null) RebuildTree(current);
        }
        private void TreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = e.NewValue as TreeViewItem;
            if (rebuildingTree || node == null) return;
            var item = (ApiItem)node.Tag;
            if (!item.IsFolder && item != current) LoadItem(item);
        }
        private ApiItem DestinationFolder()
        {
            var item = SelectedItem;
            return item != null && item.IsFolder ? item : ParentOf(item);
        }
        private List<ApiItem> Siblings(ApiItem parent) { return parent == null ? workspace.Items : parent.Children; }

        private bool EditName(string title, string initial, string directoryHint, out string name, out string directory)
        {
            var dialog = new StudioDialog(this, title);
            var panel = new StackPanel();
            if (directoryHint != null)
                panel.Children.Add(new TextBlock { Text = directoryHint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            panel.Children.Add(new TextBlock { Text = "名称", Margin = new Thickness(0, 0, 0, 5) });
            var input = new TextBox { Text = initial, MaxLength = 80, MinHeight = 34 };
            System.Windows.Automation.AutomationProperties.SetName(input, "名称");
            panel.Children.Add(input);
            TextBox directoryInput = null;
            if (directoryHint == "位置 · 根目录")
            {
                panel.Children.Add(new TextBlock { Text = "目录（可选）", Margin = new Thickness(0, 12, 0, 5) });
                directoryInput = new TextBox { MaxLength = 80, MinHeight = 34 };
                System.Windows.Automation.AutomationProperties.SetName(directoryInput, "目录（可选）");
                panel.Children.Add(directoryInput);
            }
            var confirm = dialog.DialogConfirmButton;
            confirm.IsEnabled = initial.Trim().Length > 0;
            input.TextChanged += delegate { confirm.IsEnabled = input.Text.Trim().Length > 0; };
            dialog.DialogBody.Content = panel;
            dialog.Loaded += delegate { input.Focus(); input.SelectAll(); };
            bool accepted = dialog.ShowModal();
            name = input.Text.Trim();
            directory = directoryInput == null ? "" : directoryInput.Text.Trim();
            return accepted;
        }

        private void AddItem(bool folder)
        {
            var parent = DestinationFolder();
            string name, directory;
            string location = folder ? null : parent == null ? "位置 · 根目录" : "位置 · " + FolderPath(parent) + "  /  " + parent.Name;
            if (!EditName(folder ? "新建文件夹" : "新建接口", folder ? "新建文件夹" : "新建接口", location, out name, out directory)) return;
            try
            {
                ApiItem item;
                if (folder)
                {
                    item = new ApiItem { Name = name, IsFolder = true };
                    Siblings(parent).Add(item);
                }
                else item = WorkspaceTree.CreateRequest(workspace, parent, name, directory);
                SearchBox.Clear(); RebuildTree(item); MarkDirty();
            }
            catch (ArgumentException ex) { Warn(ex.Message, "无法创建接口"); }
        }
        private void AddFolderClick(object sender, RoutedEventArgs e) { AddItem(true); }
        private void AddRequestClick(object sender, RoutedEventArgs e) { AddItem(false); }
        private void RenameItem()
        {
            var item = SelectedItem;
            string name, ignored;
            if (item == null || !EditName("重命名", item.Name, null, out name, out ignored)) return;
            item.Name = name; RebuildTree(item);
            if (current == item) RequestTitle.Text = name;
            if (current != null) { Breadcrumb.Text = FolderPath(current); Breadcrumb.ToolTip = Breadcrumb.Text; }
            MarkDirty();
        }
        private void DuplicateItem(ApiItem item)
        {
            var copy = JsonText.Deserialize<ApiItem>(JsonText.Serialize(item));
            copy.Id = Guid.NewGuid().ToString("N"); copy.Name += " 副本";
            Siblings(ParentOf(item)).Add(copy);
            SearchBox.Clear(); RebuildTree(copy); MarkDirty();
        }
        private void DeleteItem(ApiItem item)
        {
            string question = "确定删除“" + item.Name + "”" + (item.IsFolder ? "及其所有接口" : "") + "？";
            if (!StudioDialog.CreateMessage(this, question, "删除确认", true).ShowModal()) return;
            Siblings(ParentOf(item)).Remove(item);
            current = null;
            RebuildTree(null); SelectFirstRequest(); MarkDirty();
        }
        private void ImportClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "导入接口目录（API Studio JSON 文件）", Filter = "API Studio 集合 (*.json)|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var imported = WorkspaceStore.Read(dialog.FileName);
                ResetIds(imported.Items); workspace.Items.AddRange(imported.Items);
                SearchBox.Clear(); RebuildTree(current);
                if (current == null) SelectFirstRequest();
                MarkDirty(); Footer.Text = "已导入集合，原有接口已保留";
            }
            catch (Exception ex) { Warn(ex.Message, "导入失败"); }
        }
        private void ExportClick(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Title = "导出接口集合（含请求和响应）", Filter = "API Studio 集合 (*.json)|*.json", FileName = "api-studio-collection.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { WorkspaceStore.Write(dialog.FileName, workspace); Footer.Text = "接口集合已导出"; }
            catch (Exception ex) { Warn(ex.Message, "导出失败"); }
        }
        private static void ResetIds(IEnumerable<ApiItem> items)
        {
            foreach (var item in items) { item.Id = Guid.NewGuid().ToString("N"); ResetIds(item.Children); }
        }

        private ContextMenu CreateMenu()
        {
            var menu = new ContextMenu { Style = (Style)FindResource("StudioMenu") };
            menu.Resources.MergedDictionaries.Add(Resources);
            return menu;
        }
        private MenuItem AddMenuItem(ContextMenu menu, string title, Action action)
        {
            var item = new MenuItem { Header = title, Style = (Style)FindResource("StudioMenuItem") };
            item.Click += delegate { action(); };
            menu.Items.Add(item);
            return item;
        }
        private void TreeRightClick(object sender, MouseButtonEventArgs e)
        {
            var node = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (node != null) node.IsSelected = true;
            else
            {
                var selected = CollectionTree.SelectedItem as TreeViewItem;
                if (selected != null) selected.IsSelected = false;
            }
            OpenTreeMenu(node, PlacementMode.MousePoint);
            e.Handled = true;
        }
        private void OpenTreeMenu(TreeViewItem node, PlacementMode placement)
        {
            var item = node == null ? null : (ApiItem)node.Tag;
            var menu = CreateMenu();
            if (item == null || item.IsFolder)
            {
                AddMenuItem(menu, "新建接口", delegate { AddItem(false); });
                AddMenuItem(menu, "新建文件夹", delegate { AddItem(true); });
            }
            if (item != null)
            {
                AddMenuItem(menu, "重命名", RenameItem);
                if (!item.IsFolder)
                {
                    AddMenuItem(menu, "复制接口", delegate { DuplicateItem(item); });
                    foreach (string value in ApiStatus.Values)
                    {
                        string status = value;
                        AddMenuItem(menu, "设为" + status, delegate { SetStatus(item, status); }).IsChecked = status == ApiStatus.Normalize(item.Status);
                    }
                }
                AddMenuItem(menu, "移到根目录", delegate { if (WorkspaceTree.Move(workspace, item, null)) CompleteMove(item); })
                    .IsEnabled = WorkspaceTree.CanMove(workspace, item, null);
                AddMenuItem(menu, "删除", delegate { DeleteItem(item); });
            }
            menu.PlacementTarget = node == null ? (UIElement)CollectionTree : node;
            menu.Placement = placement;
            menu.IsOpen = true;
        }
        private void SetStatus(ApiItem item, string status)
        {
            item.Status = status;
            if (current == item) RequestStatus.SelectedItem = status;
            else { RefreshNode(item); MarkDirty(); }
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                var match = current as T;
                if (match != null) return match;
                current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
            }
            return null;
        }
        private void TreeMouseDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            var node = FindAncestor<TreeViewItem>(source);
            dragCandidate = node == null || FindAncestor<ToggleButton>(source) != null ? null : node.Tag as ApiItem;
            dragStart = e.GetPosition(CollectionTree);
            if (node == null && FindAncestor<ScrollBar>(source) == null)
            {
                var selection = CollectionTree.SelectedItem as TreeViewItem;
                if (selection != null) selection.IsSelected = false;
            }
        }
        private void TreeMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { dragCandidate = null; return; }
            if (dragCandidate == null || draggedItem != null) return;
            var point = e.GetPosition(CollectionTree);
            if (Math.Abs(point.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            draggedItem = dragCandidate;
            dragCandidate = null;
            Footer.Text = "拖到行上缘或下缘调整顺序 · 拖到目录中间移入 · 空白处移到根目录";
            try { DragDrop.DoDragDrop(CollectionTree, new DataObject(TreeDragFormat, draggedItem), DragDropEffects.Move); }
            finally { draggedItem = null; SetDropNode(null, 0); }
        }
        private bool TryDropTarget(DragEventArgs e, out TreeViewItem node, out int position)
        {
            node = null; position = 0;
            if (draggedItem == null || e.Data.GetData(TreeDragFormat) != draggedItem) return false;
            node = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (node == null) return WorkspaceTree.CanMove(workspace, draggedItem, null);
            var target = (ApiItem)node.Tag;
            var row = (FrameworkElement)node.Header;
            double y = e.GetPosition(row).Y;
            position = target.IsFolder
                ? (y < row.ActualHeight * .25 ? -1 : y >= row.ActualHeight * .75 ? 1 : 0)
                : (y < row.ActualHeight / 2 ? -1 : 1);
            return position == 0 ? WorkspaceTree.CanMove(workspace, draggedItem, target) :
                WorkspaceTree.CanMoveRelative(workspace, draggedItem, target, position > 0);
        }
        private void TreeDragOver(object sender, DragEventArgs e)
        {
            if (draggedItem != null && unchecked(Environment.TickCount - lastDragScroll) >= 110)
            {
                var scroll = CollectionTree.Template.FindName("_tv_scrollviewer_", CollectionTree) as ScrollViewer;
                var point = e.GetPosition(CollectionTree);
                if (scroll != null && (point.Y < 34 || point.Y > CollectionTree.ActualHeight - 34))
                {
                    if (point.Y < 34) scroll.LineUp(); else scroll.LineDown();
                    lastDragScroll = Environment.TickCount;
                }
            }
            TreeViewItem node; int position;
            bool allowed = TryDropTarget(e, out node, out position);
            e.Effects = allowed ? DragDropEffects.Move : DragDropEffects.None;
            SetDropNode(allowed ? node : null, position);
            if (allowed && node != null && position == 0) node.IsExpanded = true;
            e.Handled = true;
        }
        private void TreeDragLeave(object sender, DragEventArgs e) { SetDropNode(null, 0); }
        private void TreeDrop(object sender, DragEventArgs e)
        {
            TreeViewItem node; int position;
            if (TryDropTarget(e, out node, out position))
            {
                var target = node == null ? null : (ApiItem)node.Tag;
                bool moved = position == 0 ? WorkspaceTree.Move(workspace, draggedItem, target) :
                    WorkspaceTree.MoveRelative(workspace, draggedItem, target, position > 0);
                if (moved) CompleteMove(draggedItem);
            }
            SetDropNode(null, 0);
            e.Handled = true;
        }
        private void SetDropNode(TreeViewItem node, int position)
        {
            if (dropNode != null)
            {
                var previous = (Grid)dropNode.Header;
                ((Border)previous.Children[previous.Children.Count - 1]).BorderThickness = new Thickness(0);
            }
            dropNode = node;
            if (node == null) return;
            var header = (Grid)node.Header;
            ((Border)header.Children[header.Children.Count - 1]).BorderThickness =
                position == 0 ? new Thickness(1) : position < 0 ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 0, 2);
        }
        private void CompleteMove(ApiItem item)
        {
            SearchBox.Clear(); RebuildTree(item);
            if (current != null) { Breadcrumb.Text = FolderPath(current); Breadcrumb.ToolTip = Breadcrumb.Text; }
            MarkDirty(); Footer.Text = "已调整接口目录或顺序  ·  即将自动保存";
        }
    }
}
