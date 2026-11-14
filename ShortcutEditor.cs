using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal enum EditorKind { Connection, Folder, Title, RenameFolder, EditConnection, MoveConnection, DeleteFolder, DeleteConnection }

internal static class ShortcutEditor
{
    public static string Heading(EditorKind kind)
    {
        switch (kind)
        {
            case EditorKind.Connection: return "Add connection";
            case EditorKind.Folder: return "Add folder";
            case EditorKind.RenameFolder: return "Rename folder";
            case EditorKind.EditConnection: return "Edit shortcut";
            case EditorKind.MoveConnection: return "Move shortcut";
            case EditorKind.DeleteFolder: return "Delete folder";
            case EditorKind.DeleteConnection: return "Delete shortcut";
            default: return "Rename launcher";
        }
    }

    public static Panel Create(string root, ShortcutFolder[] folders, LauncherTheme theme, EditorKind kind,
        string menuTitle, ShortcutFolder folder, string path, Action<string> onSaved, Action onBack, Form owner)
    {
        bool isConnection = kind == EditorKind.Connection;
        bool isTitle = kind == EditorKind.Title;
        bool move = kind == EditorKind.MoveConnection;
        bool delete = kind == EditorKind.DeleteFolder || kind == EditorKind.DeleteConnection;
        bool pickFolder = isConnection || move;
        string item = path == null ? (folder == null ? "" : folder.Name) : Path.GetFileNameWithoutExtension(path);
        string fieldLabel = isConnection ? "Server name or address" : isTitle ? "Menu title" : "Folder name";
        float scale = owner.CurrentAutoScaleDimensions.Height / 96f;
        Func<int, int> px = value => (int)Math.Round(value * scale);
        int width = owner.ClientSize.Width - owner.Padding.Horizontal;
        int gutter = px(20);
        int fieldWidth = width - gutter * 2;

        var editor = new Panel { BackColor = theme.Surface, AutoScroll = true };
        editor.ForeColor = theme.Text;
        TextBox name = null;
        if (move || delete)
        {
            editor.Controls.Add(new Label { Text = item, AutoEllipsis = true, Bounds = new Rectangle(gutter, px(6), fieldWidth, px(24)) });
            if (delete)
            {
                string details = path == null ? "The folder and its contents will move to _archive\\Removed."
                    : "This shortcut will move to _archive\\Removed.";
                editor.Controls.Add(new Label { Text = details, ForeColor = theme.Secondary, Bounds = new Rectangle(gutter, px(35), fieldWidth, px(38)) });
            }
        }
        else
        {
            string initial = isTitle ? menuTitle : kind == EditorKind.RenameFolder ? item : "";
            name = AddInput(editor, theme, fieldLabel, initial, isConnection ? 259 : isTitle ? 60 : 240, gutter, fieldWidth, px, 6);
            if (kind != EditorKind.RenameFolder)
            {
                editor.Controls.Add(new Label
                {
                    Text = isConnection ? "For a custom port, use server.example:3390."
                        : isTitle ? "Leave blank to restore RDP Sessions."
                        : "This folder will appear in the launcher and connection picker.",
                    ForeColor = theme.Secondary,
                    Bounds = new Rectangle(gutter, px(75), fieldWidth, px(30))
                });
            }
        }

        ComboBox picker = null;
        if (pickFolder)
        {
            int folderY = move ? 43 : 112;
            editor.Controls.Add(new Label
            {
                Text = "Save to",
                Bounds = new Rectangle(gutter, px(folderY), fieldWidth, px(22))
            });
            picker = new ComboBox
            {
                AccessibleName = "Save to",
                DropDownStyle = ComboBoxStyle.DropDownList,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = px(24),
                BackColor = theme.Input,
                ForeColor = theme.Text,
                FlatStyle = FlatStyle.Flat,
                Bounds = new Rectangle(gutter, px(folderY + 25), fieldWidth, px(32))
            };
            picker.Items.AddRange(folders.Select(group => group.Name).ToArray());
            picker.SelectedIndex = Array.FindIndex(folders, group => group.Name == (move ? folder.Name : ShortcutStore.RootGroup));
            picker.DrawItem += (sender, args) => DrawFolder(picker, args, theme);
            editor.Controls.Add(picker);
        }

        int statusY = isConnection ? 175 : move ? 108 : 112;
        var error = new Label
        {
            AccessibleName = "Validation message",
            ForeColor = theme.Error,
            Bounds = new Rectangle(gutter, px(statusY), fieldWidth, px(44))
        };
        editor.Controls.Add(error);
        int footerY = statusY + 47;
        editor.Controls.Add(new Panel { BackColor = theme.Border, Bounds = new Rectangle(gutter, px(footerY), fieldWidth, px(1)) });
        var back = theme.Button("Back");
        back.Bounds = new Rectangle(gutter, px(footerY + 11), px(74), px(34));
        back.Click += (sender, args) => editor.BeginInvoke(onBack);
        editor.Controls.Add(back);
        string verb = delete ? "Delete" : move ? "Move shortcut" :
            isConnection ? "Save connection" : isTitle ? "Save title" : kind == EditorKind.RenameFolder ? "Rename folder" : "Create folder";
        var save = theme.Button(verb, !delete);
        if (delete)
            save.ForeColor = theme.Error;
        save.Bounds = new Rectangle(width - gutter - px(138), px(footerY + 11), px(138), px(34));
        editor.Controls.Add(save);
        editor.AutoScrollMinSize = new Size(0, px(footerY + 59));
        owner.AcceptButton = delete ? null : save;
        owner.CancelButton = back;
        save.Click += (sender, args) =>
        {
            try
            {
                switch (kind)
                {
                    case EditorKind.Connection: ShortcutStore.SaveConnection(folders[picker.SelectedIndex], name.Text); break;
                    case EditorKind.Folder: ShortcutStore.CreateFolder(root, name.Text); break;
                    case EditorKind.RenameFolder: ShortcutStore.RenameFolder(root, folder, name.Text); break;
                    case EditorKind.MoveConnection: ShortcutStore.MoveConnection(path, folders[picker.SelectedIndex]); break;
                    case EditorKind.DeleteFolder:
                    case EditorKind.DeleteConnection: ShortcutStore.ArchiveItem(root, folder, path); break;
                }
                onSaved(move ? folders[picker.SelectedIndex].Name : name == null ? null : name.Text.Trim());
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException))
                    throw;
                error.Text = ex.Message;
            }
        };
        editor.HandleCreated += (sender, args) => editor.BeginInvoke(new Action(() =>
        {
            Control first = (Control)name ?? (Control)picker ?? back;
            first.Focus();
            if (name != null)
                name.SelectAll();
        }));
        return editor;
    }

    private static TextBox AddInput(Panel panel, LauncherTheme theme, string label, string value, int limit,
        int gutter, int width, Func<int, int> px, int y)
    {
        panel.Controls.Add(new Label { Text = label, Bounds = new Rectangle(gutter, px(y), width, px(22)) });
        var input = new TextBox
        {
            AccessibleName = label,
            BackColor = theme.Input,
            ForeColor = theme.Text,
            BorderStyle = BorderStyle.None,
            MaxLength = limit,
            Text = value
        };
        AddField(panel, input, theme, new Rectangle(gutter, px(y + 27), width, px(34)), px(9));
        return input;
    }

    private static void AddField(Panel editor, TextBox input, LauncherTheme theme, Rectangle bounds, int inset)
    {
        var field = new Panel { BackColor = theme.Border, Bounds = bounds };
        var fill = new Panel { BackColor = theme.Input, Bounds = new Rectangle(1, 1, bounds.Width - 2, bounds.Height - 2) };
        input.SetBounds(inset, (fill.Height - input.PreferredHeight) / 2, fill.Width - inset * 2, input.PreferredHeight);
        input.GotFocus += (sender, args) => field.BackColor = theme.Accent;
        input.LostFocus += (sender, args) => field.BackColor = theme.Border;
        fill.Click += (sender, args) => input.Focus();
        fill.Controls.Add(input);
        field.Controls.Add(fill);
        editor.Controls.Add(field);
    }

    private static void DrawFolder(ComboBox picker, DrawItemEventArgs args, LauncherTheme theme)
    {
        bool selected = (args.State & DrawItemState.Selected) != 0;
        Color surface = selected ? theme.Hover : theme.Input;
        Color text = selected && theme.HighContrast ? SystemColors.HighlightText : theme.Text;
        using (var fill = new SolidBrush(surface))
            args.Graphics.FillRectangle(fill, args.Bounds);
        if (args.Index >= 0)
            TextRenderer.DrawText(args.Graphics, (string)picker.Items[args.Index], picker.Font,
                Rectangle.Inflate(args.Bounds, -6, 0), text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        args.DrawFocusRectangle();
    }
}
