using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal sealed class MenuDragLayout
{
    private readonly FlowLayoutPanel list;
    private readonly LauncherTheme theme;
    private ShortcutFolder[] folders;
    private MenuOrder order;
    private readonly Action<Exception> showError;
    private readonly List<Group> groups = new List<Group>();
    private readonly string format = "RdpSessions.MenuDrag." + Guid.NewGuid().ToString("N");
    private readonly Panel marker;
    private Control skippedClick;
    private Group highlighted;

    public event Action Completed;
    public event Action<bool> DraggingChanged;

    public MenuDragLayout(FlowLayoutPanel list, LauncherTheme theme, ShortcutFolder[] folders, MenuOrder order, Action<Exception> showError)
    {
        this.list = list;
        this.theme = theme;
        this.folders = folders;
        this.order = order;
        this.showError = showError;
        marker = new InsertionMarker { Visible = false, BackColor = theme.HighContrast ? SystemColors.Highlight : theme.Accent };
        list.ParentChanged += (sender, args) =>
        {
            if (list.Parent != null)
                list.Parent.Controls.Add(marker);
        };
        AllowDrop(list);
    }

    private sealed class InsertionMarker : Panel
    {
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084) // WM_NCHITTEST
                message.Result = new IntPtr(-1); // HTTRANSPARENT
            else
                base.WndProc(ref message);
        }
    }

    public sealed class Group
    {
        internal readonly ShortcutFolder Folder;
        internal readonly Control Header;
        internal readonly List<Control> Controls = new List<Control>();
        internal readonly List<Control> Rows = new List<Control>();
        private readonly MenuDragLayout layout;

        internal Group(MenuDragLayout layout, ShortcutFolder folder, Control header)
        {
            this.layout = layout;
            Folder = folder;
            Header = header;
            Add(header, null);
            layout.HandleDrag(header, this, null);
        }

        public void Add(Control control, string file)
        {
            Controls.Add(control);
            layout.AllowDrop(control);
            if (file != null)
            {
                control.Tag = file;
                Rows.Add(control);
                layout.HandleDrag(control, this, file);
            }
        }
    }

    private sealed class DragItem
    {
        internal Group Group;
        internal string File;
    }

    public Group AddGroup(ShortcutFolder folder, Control header)
    {
        var group = new Group(this, folder, header);
        groups.Add(group);
        return group;
    }

    public void Reset(ShortcutFolder[] updatedFolders, MenuOrder updatedOrder)
    {
        ClearCue();
        groups.Clear();
        folders = updatedFolders;
        order = updatedOrder;
        skippedClick = null;
    }

    public bool ConsumeDragClick(Control control)
    {
        if (skippedClick != control)
            return false;
        skippedClick = null;
        return true;
    }

    private void HandleDrag(Control control, Group group, string file)
    {
        Rectangle start = Rectangle.Empty;
        control.MouseDown += (sender, args) =>
        {
            skippedClick = null;
            if (args.Button == MouseButtons.Left)
            {
                Size size = SystemInformation.DragSize;
                start = new Rectangle(args.X - size.Width / 2, args.Y - size.Height / 2, size.Width, size.Height);
                // Folder headings are labels and otherwise stop reporting moves outside their bounds.
                if (file == null) control.Capture = true;
            }
        };
        control.MouseUp += (sender, args) =>
        {
            start = Rectangle.Empty;
            if (file == null) control.Capture = false;
        };
        control.MouseMove += (sender, args) =>
        {
            if (start.IsEmpty || args.Button != MouseButtons.Left || start.Contains(args.Location))
                return;
            start = Rectangle.Empty;
            skippedClick = control;
            control.Capture = false;
            DraggingChanged(true);
            try
            {
                var data = new DataObject();
                data.SetData(format, false, new DragItem { Group = group, File = file });
                if (control.DoDragDrop(data, DragDropEffects.Move) == DragDropEffects.Move)
                    Completed();
            }
            finally
            {
                if (!list.IsDisposed) ClearCue();
                DraggingChanged(false);
            }
        };
    }

    private void AllowDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += Preview;
        control.DragOver += Preview;
        control.DragLeave += (sender, args) => ClearCue();
        control.DragDrop += Drop;
    }

    private void Preview(object sender, DragEventArgs args)
    {
        ClearCue();
        args.Effect = DragDropEffects.None;
        var item = args.Data.GetData(format, false) as DragItem;
        Point point = list.PointToClient(new Point(args.X, args.Y));
        if (item == null) return;
        ScrollNearEdge(point);
        Group target;
        Control row;
        bool after;
        int line;
        if (!Locate(item, point, out target, out row, out after, out line)) return;
        args.Effect = DragDropEffects.Move;
        highlighted = target;
        if (!theme.HighContrast)
            target.Header.BackColor = theme.Hover;
        if (line < 0 || line >= list.ClientSize.Height || list.Parent == null)
            return;
        Point origin = list.Parent.PointToClient(list.PointToScreen(new Point(target.Header.Left + 8, line)));
        marker.Bounds = new Rectangle(origin, new Size(Math.Max(1, target.Header.Width - 16), 2));
        marker.BringToFront();
        marker.Show();
    }

    private void ClearCue()
    {
        marker.Hide();
        if (highlighted != null)
            highlighted.Header.BackColor = theme.Card;
        highlighted = null;
    }

    private bool Locate(DragItem item, Point point, out Group target, out Control row, out bool after, out int line)
    {
        target = groups.FirstOrDefault(group => point.Y < group.Controls.Last().Bottom);
        row = null;
        after = true;
        line = 0;
        if (!list.ClientRectangle.Contains(point))
            return false;
        if (target == null)
            target = groups.LastOrDefault();
        if (target == null)
            return false;
        if (item.File == null)
        {
            if (target == item.Group) return false;
            after = point.Y >= (target.Header.Top + target.Controls.Last().Bottom) / 2;
            line = after ? target.Controls.Last().Bottom - 2 : target.Header.Top;
            return true;
        }
        if (target.Rows.Count == 0 || point.Y < target.Header.Bottom || point.Y >= target.Rows.Last().Bottom)
        {
            if (target == item.Group && target.Rows.Count < 2) return false;
            line = target.Controls.Last().Bottom - 2;
            return true;
        }
        row = target.Rows.First(candidate => point.Y < candidate.Bottom);
        if (String.Equals(item.File, row.Tag as string, StringComparison.OrdinalIgnoreCase))
            return false;
        after = point.Y >= row.Top + row.Height / 2;
        line = after ? row.Bottom - 2 : row.Top;
        return true;
    }

    private void ScrollNearEdge(Point point)
    {
        if (!list.VerticalScroll.Visible || (point.Y >= 18 && point.Y <= list.ClientSize.Height - 18))
            return;
        int current = -list.AutoScrollPosition.Y;
        int limit = list.VerticalScroll.Maximum - list.VerticalScroll.LargeChange + 1;
        list.AutoScrollPosition = new Point(0, Math.Max(0, Math.Min(limit, current + (point.Y < 18 ? -14 : 14))));
    }

    private void Drop(object sender, DragEventArgs args)
    {
        ClearCue();
        var item = args.Data.GetData(format, false) as DragItem;
        Group target;
        Control row;
        bool after;
        int line;
        args.Effect = DragDropEffects.None;
        if (item == null || !Locate(item, list.PointToClient(new Point(args.X, args.Y)), out target, out row, out after, out line))
            return;
        order.Capture(folders);
        bool moved = false;
        try
        {
            if (item.File == null)
                order.ReorderFolder(item.Group.Folder.Name, target.Folder.Name, after);
            else
            {
                if (item.Group != target)
                {
                    ShortcutStore.MoveConnection(item.File, target.Folder);
                    moved = true;
                }
                order.ReorderShortcut(Path.GetFileName(item.File), item.Group.Folder.Name, target.Folder.Name,
                    row == null ? null : Path.GetFileName((string)row.Tag), after);
            }
            args.Effect = DragDropEffects.Move;
        }
        catch (Exception error)
        {
            if (!(error is IOException || error is UnauthorizedAccessException)) throw;
            if (moved)
            {
                args.Effect = DragDropEffects.Move;
                showError(new IOException("The shortcut moved, but the menu order could not be saved.", error));
            }
            else
                showError(error);
        }
    }
}
