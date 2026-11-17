using System;
using System.Collections.Generic;

namespace ApiStudio
{
    public static class WorkspaceTree
    {
        public static ApiItem CreateRequest(Workspace workspace, ApiItem parent, string name, string directoryName)
        {
            if (workspace == null || workspace.Items == null) throw new ArgumentException("工作空间无效。");
            if (parent != null && (!parent.IsFolder || FindList(workspace.Items, parent) == null)) throw new ArgumentException("目标目录不存在。");
            name = (name ?? "").Trim(); directoryName = (directoryName ?? "").Trim();
            if (name.Length == 0 || name.Length > 80 || directoryName.Length > 80) throw new ArgumentException("请填写 1 到 80 个字符的接口名称，目录名不超过 80 个字符。");
            int parentDepth = parent == null ? -1 : Depth(workspace.Items, parent, 0);
            if (parentDepth + (directoryName.Length == 0 ? 1 : 2) > 32) throw new ArgumentException("目录层级不能超过 32 层。");
            var destination = parent == null ? workspace.Items : parent.Children;
            if (directoryName.Length != 0)
            {
                ApiItem folder = destination.Find(x => x.IsFolder && string.Equals(x.Name, directoryName, StringComparison.OrdinalIgnoreCase));
                if (folder == null) { folder = new ApiItem { Name = directoryName, IsFolder = true }; destination.Add(folder); }
                destination = folder.Children;
            }
            var item = new ApiItem { Name = name }; destination.Add(item); return item;
        }
        public static bool CanMove(Workspace workspace, ApiItem item, ApiItem folder)
        {
            if (workspace == null || workspace.Items == null || item == null) return false;
            var source = FindList(workspace.Items, item); if (source == null) return false;
            if (folder != null && (!folder.IsFolder || folder == item || FindList(workspace.Items, folder) == null || FindList(item.Children, folder) != null)) return false;
            var target = folder == null ? workspace.Items : folder.Children;
            int depth = folder == null ? 0 : Depth(workspace.Items, folder, 0) + 1;
            return !object.ReferenceEquals(source, target) && depth + Height(item) <= 32;
        }
        public static bool Move(Workspace workspace, ApiItem item, ApiItem folder)
        {
            if (!CanMove(workspace, item, folder)) return false;
            var source = FindList(workspace.Items, item); var target = folder == null ? workspace.Items : folder.Children;
            source.Remove(item); target.Add(item); return true;
        }
        public static bool CanMoveRelative(Workspace workspace, ApiItem item, ApiItem anchor, bool after)
        {
            if (workspace == null || workspace.Items == null || item == null || anchor == null || item == anchor) return false;
            var source = FindList(workspace.Items, item); var target = FindList(workspace.Items, anchor);
            if (source == null || target == null || FindList(item.Children, anchor) != null) return false;
            if (Depth(workspace.Items, anchor, 0) + Height(item) > 32) return false;
            int index = target.IndexOf(anchor) + (after ? 1 : 0);
            if (object.ReferenceEquals(source, target))
            {
                int oldIndex = source.IndexOf(item); if (oldIndex < index) index--;
                if (oldIndex == index) return false;
            }
            return true;
        }
        public static bool MoveRelative(Workspace workspace, ApiItem item, ApiItem anchor, bool after)
        {
            if (!CanMoveRelative(workspace, item, anchor, after)) return false;
            var source = FindList(workspace.Items, item); var target = FindList(workspace.Items, anchor);
            source.Remove(item); target.Insert(target.IndexOf(anchor) + (after ? 1 : 0), item); return true;
        }
        private static List<ApiItem> FindList(List<ApiItem> items, ApiItem item)
        {
            if (items.Contains(item)) return items;
            foreach (var child in items) { var found = FindList(child.Children, item); if (found != null) return found; }
            return null;
        }
        private static int Depth(List<ApiItem> items, ApiItem item, int depth)
        {
            foreach (var child in items) { if (child == item) return depth; int found = Depth(child.Children, item, depth + 1); if (found >= 0) return found; }
            return -1;
        }
        private static int Height(ApiItem item)
        { int height = 0; foreach (var child in item.Children) height = Math.Max(height, 1 + Height(child)); return height; }
    }
}
