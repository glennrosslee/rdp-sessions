using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Windows.Shell;

internal static class RdpTaskbarMenu
{
    private static readonly string AppId = ResolveAppId();
    private const string Title = "RDP Sessions";
    private static readonly string Client = Path.Combine(Environment.SystemDirectory, "mstsc.exe");
    private static readonly string TitleFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher-title.txt");

    private static string ResolveAppId()
    {
        string localOverride = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app-id.txt");
        return File.Exists(localOverride) ? File.ReadAllText(localOverride).Trim() : "RdpSessions.TaskbarLauncher";
    }

    [STAThread]
    private static int Main(string[] arguments)
    {
        try
        {
            Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId));
            string root = ShortcutStore.ResolveRoot();
            var order = new MenuOrder(root);
            var folders = order.Apply(ShortcutStore.ReadFolders(root));

            if (arguments.SequenceEqual(new[] { "--verify" }))
            {
                Console.WriteLine(String.Join(Environment.NewLine,
                    folders.SelectMany(folder => folder.Links.Select(path => folder.Name + "\t" + path))));
                return 0;
            }

            bool register = arguments.SequenceEqual(new[] { "--register" });
            if (register)
            {
                string iconPath = InstallShortcutIcon();
                SaveStartShortcut(iconPath);
                RefreshTaskbarShortcutIcon(iconPath);
            }

            var rejected = RefreshJumpList(folders);
            if (register)
            {
                if (rejected.Count != 0)
                    throw new InvalidOperationException("Windows rejected taskbar entries: " + String.Join(", ", rejected));
                return 0;
            }

            System.Windows.Forms.Application.EnableVisualStyles();
            Point anchor = Cursor.Position;
            while (ShowMenu(root, folders, order, anchor))
            {
                order = new MenuOrder(root);
                folders = order.Apply(ShortcutStore.ReadFolders(root));
                RefreshJumpList(folders);
            }
            return 0;
        }
        catch (Exception error)
        {
            if (arguments.Length != 0)
                Console.Error.WriteLine(error.Message);
            else
                MessageBox.Show(error.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static List<string> RefreshJumpList(IEnumerable<ShortcutFolder> folders)
    {
            var jumpList = new JumpList();
            var rejected = new List<string>();
            jumpList.JumpItemsRejected += (sender, eventArgs) =>
                rejected.AddRange(eventArgs.RejectionReasons.Select(reason => reason.ToString()));
            foreach (string path in folders.SelectMany(folder => folder.Links))
            {
                jumpList.JumpItems.Add(new JumpTask
                {
                    Title = DisplayName(path),
                    ApplicationPath = Client,
                    Arguments = ConnectionArguments(path, false),
                    IconResourcePath = Client,
                    IconResourceIndex = 0
                });
            }
            jumpList.Apply();
            return rejected;
    }

    private enum MenuAction { Dismiss, Connect, OpenFolder, Refresh }

    private static bool ShowMenu(string root, ShortcutFolder[] folders, MenuOrder order, Point anchor)
    {
        const int width = 448;
        const int headerHeight = 70;
        const int footerHeight = 54;
        var theme = new LauncherTheme();
        string menuTitle = File.Exists(TitleFile) ? File.ReadAllText(TitleFile).Trim() : Title;
        if (menuTitle.Length == 0)
            menuTitle = Title;
        string selectedShortcut = null;
        bool connectAsAdmin = false;
        MenuAction action = MenuAction.Dismiss;

        using (var regularFont = new Font("Segoe UI", 9f))
        using (var rowFont = new Font("Segoe UI Semibold", 9.5f))
        using (var titleFont = new Font("Segoe UI Semibold", 15f))
        using (var form = new Form
        {
            Text = menuTitle,
            Font = regularFont,
            ForeColor = theme.Text,
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96f, 96f),
            FormBorderStyle = FormBorderStyle.None,
            BackColor = theme.Border,
            Padding = new Padding(1),
            StartPosition = FormStartPosition.Manual,
            TopMost = true,
            KeyPreview = true
        })
        using (var list = new MenuList
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(13, 4, 13, 9),
            BackColor = theme.Surface
        })
        {
            var header = new Panel { Dock = DockStyle.Top, Height = headerHeight, BackColor = theme.Surface };
            var badge = new MonitorBadge(theme) { Bounds = new Rectangle(18, 16, 36, 36) };
            header.Controls.Add(badge);
            var headingText = new Label
            {
                Text = menuTitle,
                Font = titleFont,
                ForeColor = theme.Text,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Bounds = new Rectangle(65, 15, 288, 39)
            };
            header.Controls.Add(headingText);
            var rename = theme.Button("Rename");
            rename.Bounds = new Rectangle(359, 19, 70, 31);
            header.Controls.Add(rename);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = footerHeight, BackColor = theme.Surface };
            footer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = theme.Border });
            var addConnection = AddFooterButton(footer, theme, "Add Connection", 14, 138, true);
            var addFolder = AddFooterButton(footer, theme, "Add Folder", 159, 101);
            var manage = AddFooterButton(footer, theme, "Manage shortcuts", 270, 162);
            manage.Click += (sender, eventArgs) =>
            {
                action = MenuAction.OpenFolder;
                form.Close();
            };

            Panel editor = null;
            bool dragging = false;
            bool showingError = false;
            Action<EditorKind, ShortcutFolder, string> edit = null;
            Action<string, bool> connect = (path, admin) =>
            {
                selectedShortcut = path;
                connectAsAdmin = admin;
                action = MenuAction.Connect;
                form.Close();
            };
            Action refresh = () =>
            {
                action = MenuAction.Refresh;
                form.Close();
            };
            Action<Exception> showError = error =>
            {
                showingError = true;
                try { MessageBox.Show(form, error.Message, menuTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                finally { showingError = false; }
            };
            int openMenus = 0;
            Action<Control, ContextMenuStrip> registerMenu = (owner, menu) =>
            {
                menu.Opening += (sender, args) => openMenus++;
                menu.Closed += (sender, args) =>
                {
                    openMenus--;
                    if (form.IsDisposed || !form.IsHandleCreated)
                        return;
                    form.BeginInvoke(new Action(() =>
                    {
                        if (!form.IsDisposed && !form.ContainsFocus && editor == null && !dragging && !showingError && openMenus == 0)
                            form.Close();
                    }));
                };
                owner.Disposed += (sender, args) => menu.Dispose();
            };
            var layout = new MenuDragLayout(list, theme, folders, order, showError);
            layout.DraggingChanged += value => dragging = value;

            Func<int> populateList = () =>
            {
                int listHeight = list.Padding.Vertical;
                foreach (ShortcutFolder folder in folders)
                {
                    var heading = new FolderHeading(theme)
                    {
                        Text = folder.Name,
                        AutoEllipsis = true,
                        Font = rowFont,
                        ForeColor = theme.Text,
                        BackColor = theme.Card,
                        Height = 33,
                        TextAlign = ContentAlignment.MiddleLeft,
                        Padding = new Padding(12, 0, 0, 0),
                        Margin = new Padding(0, 8, 0, 0),
                        Cursor = Cursors.SizeAll
                    };
                    var group = layout.AddGroup(folder, heading);
                    if (folder.Name != ShortcutStore.RootGroup)
                    {
                        var folderMenu = theme.LauncherMenu();
                        AddItem(folderMenu, "Rename folder", () => edit(EditorKind.RenameFolder, folder, null));
                        AddItem(folderMenu, "Delete folder", () => edit(EditorKind.DeleteFolder, folder, null), true);
                        heading.ContextMenuStrip = folderMenu;
                        registerMenu(heading, folderMenu);
                    }
                    list.Controls.Add(heading);
                    listHeight += heading.Height + heading.Margin.Vertical;
                    if (folder.Links.Length == 0)
                    {
                        var empty = new Label { Text = "No connections yet", ForeColor = theme.Secondary, BackColor = theme.Card,
                            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0), Height = 31, Margin = Padding.Empty };
                        list.Controls.Add(empty);
                        group.Add(empty, null);
                        listHeight += empty.Height;
                    }
                    foreach (string path in folder.Links)
                    {
                        string shortcut = path;
                        var button = new SessionButton(DisplayName(path), theme.Secondary, theme.Accent, theme.Hover)
                        {
                            Font = rowFont,
                            ForeColor = theme.Text,
                            BackColor = theme.Card,
                            Height = 34,
                            Margin = Padding.Empty
                        };
                        var itemMenu = theme.LauncherMenu();
                        AddItem(itemMenu, "Edit shortcut", () => edit(EditorKind.EditConnection, folder, shortcut));
                        AddItem(itemMenu, "Connect as administrator", () => connect(shortcut, true));
                        AddItem(itemMenu, "Move shortcut", () => edit(EditorKind.MoveConnection, folder, shortcut));
                        AddItem(itemMenu, "Delete shortcut", () => edit(EditorKind.DeleteConnection, folder, shortcut), true);
                        button.ContextMenuStrip = itemMenu;
                        registerMenu(button, itemMenu);

                        group.Add(button, shortcut);
                        button.Click += (sender, eventArgs) =>
                        {
                            if (dragging || layout.ConsumeDragClick(button))
                                return;
                            connect(shortcut, false);
                        };
                        list.Controls.Add(button);
                        listHeight += button.Height + button.Margin.Vertical;
                    }
                    var baseLine = new Panel { BackColor = theme.Card, Height = 6, Margin = Padding.Empty };
                    list.Controls.Add(baseLine);
                    group.Add(baseLine, null);
                    listHeight += baseLine.Height;
                }
                return listHeight;
            };
            int initialHeight = populateList();
            layout.Completed += () => form.BeginInvoke(new Action(() =>
            {
                if (form.IsDisposed)
                    return;

                // Finish the native drag loop before replacing its row controls.
                order = new MenuOrder(root);
                folders = order.Apply(ShortcutStore.ReadFolders(root));
                Point scroll = list.AutoScrollPosition;
                list.SuspendLayout();
                try
                {
                    layout.Reset(folders, order);
                    foreach (Control row in list.Controls.Cast<Control>().ToArray())
                        row.Dispose();
                    populateList();
                    FitRows(list);
                }
                finally
                {
                    list.ResumeLayout(true);
                }
                addConnection.Focus();
                list.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
                RefreshJumpList(folders);
            }));
            form.Controls.Add(list);
            form.Controls.Add(footer);
            form.Controls.Add(header);
            Size menuSize = new Size(width, headerHeight + initialHeight + footerHeight + 8);
            form.ClientSize = menuSize;
            list.ClientSizeChanged += (sender, eventArgs) => FitRows(list);
            Size actualMenuSize = menuSize;
            Action showList = () =>
            {
                form.AcceptButton = null;
                form.CancelButton = null;
                form.Controls.Remove(editor);
                editor.Dispose();
                editor = null;
                headingText.Text = menuTitle;
                form.AccessibleName = menuTitle;
                rename.Visible = true;
                list.Visible = true;
                footer.Visible = true;
                PlacePopup(form, actualMenuSize, anchor);
                addConnection.Focus();
            };
            edit = (kind, folder, path) =>
            {
                Panel next;
                try
                {
                    Action<string> onSaved = title =>
                    {
                        if (kind == EditorKind.Title)
                            SaveMenuTitle(title);
                        if (kind == EditorKind.RenameFolder || kind == EditorKind.EditConnection || kind == EditorKind.MoveConnection)
                        {
                            try
                            {
                                order.Capture(folders);
                                if (kind == EditorKind.RenameFolder)
                                    order.RenameFolder(folder.Name, title);
                                else if (kind == EditorKind.EditConnection)
                                    order.RenameShortcut(folder.Name, Path.GetFileName(path), title + Path.GetExtension(path));
                                else
                                    order.ReorderShortcut(Path.GetFileName(path), folder.Name, title, null, true);
                            }
                            catch (Exception error)
                            {
                                if (!(error is IOException || error is UnauthorizedAccessException)) throw;
                                showError(new IOException("The change was saved, but the menu order could not be updated.", error));
                            }
                        }
                        refresh();
                    };
                    next = kind == EditorKind.EditConnection
                        ? RdpSettingsEditor.Create(new RdpDocument(path), theme, onSaved, showList, form)
                        : ShortcutEditor.Create(root, folders, theme, kind, menuTitle, folder, path, onSaved, showList, form);
                }
                catch (Exception error)
                {
                    if (!(error is IOException || error is UnauthorizedAccessException))
                        throw;
                    showError(error);
                    return;
                }
                editor = next;
                list.Visible = false;
                footer.Visible = false;
                rename.Visible = false;
                headingText.Text = ShortcutEditor.Heading(kind);
                form.AccessibleName = headingText.Text;
                Size editorSize = kind == EditorKind.EditConnection
                    ? new Size((int)(actualMenuSize.Width * 1.6), Math.Max(actualMenuSize.Height, (int)(form.CurrentAutoScaleDimensions.Height * 6.25)))
                    : new Size(actualMenuSize.Width, header.Height + editor.AutoScrollMinSize.Height + form.Padding.Vertical);
                PlacePopup(form, editorSize, anchor);
                editor.Bounds = new Rectangle(form.Padding.Left, form.Padding.Top + header.Height,
                    form.ClientSize.Width - form.Padding.Horizontal,
                    form.ClientSize.Height - header.Height - form.Padding.Vertical);
                form.Controls.Add(editor);
            };
            addConnection.Click += (sender, eventArgs) => edit(EditorKind.Connection, null, null);
            addFolder.Click += (sender, eventArgs) => edit(EditorKind.Folder, null, null);
            rename.Click += (sender, eventArgs) => edit(EditorKind.Title, null, null);
            form.Deactivate += (sender, eventArgs) =>
            {
                if (editor == null && !dragging && !showingError && openMenus == 0)
                    form.Close();
            };
            form.KeyDown += (sender, eventArgs) =>
            {
                if (editor == null && eventArgs.KeyCode == Keys.Escape)
                    form.Close();
                if (editor == null && (eventArgs.KeyCode == Keys.Up || eventArgs.KeyCode == Keys.Down))
                {
                    form.SelectNextControl(form.ActiveControl, eventArgs.KeyCode == Keys.Down, true, true, true);
                    eventArgs.Handled = true;
                }
            };
            form.Load += (sender, eventArgs) =>
            {
                int rounded = 2;
                DwmSetWindowAttribute(form.Handle, 33, ref rounded, sizeof(int));
                actualMenuSize = form.Size;
                PlacePopup(form, actualMenuSize, anchor);
                FitRows(list);
            };
            form.Shown += (sender, eventArgs) => form.Activate();
            System.Windows.Forms.Application.Run(form);
        }

        switch (action)
        {
            case MenuAction.Connect:
                ShortcutStore.CheckShortcutPath(selectedShortcut);
                Process.Start(Client, ConnectionArguments(selectedShortcut, connectAsAdmin));
                return false;
            case MenuAction.OpenFolder:
                Process.Start(new ProcessStartInfo(root) { UseShellExecute = true });
                return false;
            case MenuAction.Refresh:
                return true;
            default:
                return false;
        }
    }

    internal static string ConnectionArguments(string path, bool forceAdmin)
    {
        return QuotePath(path) + (forceAdmin || RdpDocument.ReadAdmin(path) ? " /admin" : "");
    }

    private sealed class MenuList : FlowLayoutPanel
    {
        public MenuList() { DoubleBuffered = true; }
    }

    private sealed class FolderHeading : Label
    {
        private readonly LauncherTheme theme;
        public FolderHeading(LauncherTheme theme) { this.theme = theme; }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            int spacing = Math.Max(3, Height / 9);
            int diameter = Math.Max(2, Height / 15);
            int right = Width - spacing * 5;
            using (var brush = new SolidBrush(theme.Secondary))
                for (int column = 0; column < 2; column++)
                    for (int row = -1; row <= 1; row++)
                        args.Graphics.FillEllipse(brush, right + column * spacing, Height / 2 + row * spacing, diameter, diameter);
        }
    }

    private static void AddItem(ContextMenuStrip menu, string label, Action click, bool delete = false)
    {
        if (delete) menu.Items.Add(new ToolStripSeparator());
        var item = new ToolStripMenuItem(label, null, (sender, args) => click())
        {
            Padding = new Padding(11, 6, 18, 6),
            Tag = delete ? "delete" : null
        };
        menu.Items.Add(item);
    }

    private static void PlacePopup(Form form, Size size, Point anchor)
    {
        Rectangle area = Screen.FromPoint(anchor).WorkingArea;
        form.Size = new Size(Math.Min(size.Width, area.Width), Math.Min(size.Height, area.Height));
        form.Location = new Point(
            Math.Max(area.Left, Math.Min(anchor.X - form.Width / 2, area.Right - form.Width)),
            Math.Max(area.Top, Math.Min(anchor.Y - form.Height - 8, area.Bottom - form.Height)));
    }

    private static Button AddFooterButton(Panel footer, LauncherTheme theme, string name, int x, int width, bool primary = false)
    {
        var button = theme.Button(name, primary);
        button.Bounds = new Rectangle(x, 10, width, 34);
        footer.Controls.Add(button);
        return button;
    }

    private static void SaveMenuTitle(string title)
    {
        string temp = TitleFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, title);
            if (File.Exists(TitleFile))
                File.Replace(temp, TitleFile, null);
            else
                File.Move(temp, TitleFile);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static void FitRows(FlowLayoutPanel list)
    {
        int available = Math.Max(1, list.ClientSize.Width - list.Padding.Horizontal);
        foreach (Control control in list.Controls)
            control.Width = Math.Max(1, available - control.Margin.Horizontal);
    }

    private sealed class SessionButton : Button
    {
        private readonly string host;
        private readonly string description;
        private readonly Color muted;
        private readonly Color accent;
        private readonly Color hover;

        public SessionButton(string name, Color muted, Color accent, Color hover)
        {
            int separator = name.IndexOf(" (", StringComparison.Ordinal);
            host = separator < 0 ? name : name.Substring(0, separator);
            description = separator < 0 ? "" : name.Substring(separator + 2).TrimEnd(')');
            this.muted = muted;
            this.accent = accent;
            this.hover = hover;
            Text = name;
            AccessibleName = name;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseMnemonic = false;
            MouseEnter += (sender, eventArgs) => Invalidate();
            MouseLeave += (sender, eventArgs) => Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            bool selected = ClientRectangle.Contains(PointToClient(Cursor.Position)) || (Focused && ShowFocusCues);
            using (var fill = new SolidBrush(selected ? hover : BackColor))
                e.Graphics.FillRectangle(fill, ClientRectangle);
            Color primary = selected && SystemInformation.HighContrast ? SystemColors.HighlightText : ForeColor;
            Color secondary = selected && SystemInformation.HighContrast ? SystemColors.HighlightText : muted;
            Color indicator = selected && !SystemInformation.HighContrast ? accent : secondary;
            int inset = Math.Max(8, Font.Height / 2);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            int hostWidth = TextRenderer.MeasureText(e.Graphics, host, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(e.Graphics, "\u22EE", Font,
                new Rectangle(inset, 0, inset, Height), indicator, flags);
            int hostX = inset * 3;
            TextRenderer.DrawText(e.Graphics, host, Font,
                new Rectangle(hostX, 0, Math.Max(1, Width - hostX - inset), Height), primary, flags);
            int descriptionX = hostX + hostWidth + inset;
            if (descriptionX < Width - inset)
                TextRenderer.DrawText(e.Graphics, description, Parent.Font,
                    new Rectangle(descriptionX, 0, Width - descriptionX - inset, Height), secondary, flags);
        }
    }

    private static string DisplayName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        const string copySuffix = " - Copy";
        return name.EndsWith(copySuffix, StringComparison.OrdinalIgnoreCase)
            ? name.Substring(0, name.Length - copySuffix.Length)
            : name;
    }

    private static string QuotePath(string path)
    {
        return "\"" + path + "\"";
    }

    private static string InstallShortcutIcon()
    {
        using (var resource = typeof(RdpTaskbarMenu).Assembly.GetManifestResourceStream("RdpSessions.ico"))
        using (var buffer = new MemoryStream())
        using (var sha256 = SHA256.Create())
        {
            resource.CopyTo(buffer);
            byte[] artwork = buffer.ToArray();
            // Windows can reuse an icon from its cache while the shortcut's icon path stays the same.
            string revision = BitConverter.ToString(sha256.ComputeHash(artwork), 0, 8).Replace("-", "");
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RdpSessions-" + revision + ".ico");
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(artwork))
            {
                string staged = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    File.WriteAllBytes(staged, artwork);
                    try { File.Move(staged, path); }
                    catch (IOException)
                    {
                        if (!File.Exists(path)) throw;
                        File.Replace(staged, path, null);
                    }
                }
                finally { File.Delete(staged); }
            }
            return path;
        }
    }

    private static void SaveStartShortcut(string iconPath)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Title + ".lnk");
        string launcher = Application.ExecutablePath;
        var link = (IShellLink)new ShellLink();
        var properties = (IPropertyStore)link;
        try
        {
            Marshal.ThrowExceptionForHR(link.SetPath(launcher));
            Marshal.ThrowExceptionForHR(link.SetIconLocation(iconPath, 0));
            var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
            var property = new PropertyVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(AppId) };
            try
            {
                Marshal.ThrowExceptionForHR(properties.SetValue(ref key, ref property));
                Marshal.ThrowExceptionForHR(properties.Commit());
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(path, true);
            }
            finally
            {
                Marshal.FreeCoTaskMem(property.Pointer);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
        NotifyShortcutChanged(path);
    }

    private static void RefreshTaskbarShortcutIcon(string iconPath)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar", Title + ".lnk");
        if (!File.Exists(path)) return;

        string launcher = Application.ExecutablePath;
        var link = (IShellLink)new ShellLink();
        try
        {
            var persisted = (System.Runtime.InteropServices.ComTypes.IPersistFile)link;
            persisted.Load(path, 0);
            var target = new StringBuilder(32768);
            Marshal.ThrowExceptionForHR(link.GetPath(target, target.Capacity, IntPtr.Zero, 0));
            if (!String.Equals(target.ToString(), launcher, StringComparison.OrdinalIgnoreCase)) return;

            Marshal.ThrowExceptionForHR(link.SetIconLocation(iconPath, 0));
            persisted.Save(path, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
        NotifyShortcutChanged(path);
    }

    private static void NotifyShortcutChanged(string path)
    {
        const uint updateItem = 0x00002000;
        const uint unicodePath = 0x0005;
        SHChangeNotify(updateItem, unicodePath, path, IntPtr.Zero);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string path, IntPtr item2);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, IntPtr findData, uint flags);
        void GetIDList();
        void SetIDList();
        void GetDescription();
        void SetDescription();
        void GetWorkingDirectory();
        void SetWorkingDirectory();
        void GetArguments();
        void SetArguments();
        void GetHotkey();
        void SetHotkey();
        void GetShowCmd();
        void SetShowCmd();
        void GetIconLocation();
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath();
        void Resolve();
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropertyVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount();
        void GetAt();
        void GetValue();
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropertyVariant value);
        [PreserveSig] int Commit();
    }
}
