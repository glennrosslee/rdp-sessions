using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal static class RdpSettingsEditor
{
    public static Panel Create(RdpDocument document, LauncherTheme theme, Action<string> saved, Action back, Form owner)
    {
        var panel = new Panel { BackColor = theme.Surface, ForeColor = theme.Text };
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 118, ColumnCount = 1, RowCount = 4, Padding = new Padding(14, 0, 14, 6) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        top.Controls.Add(new Label { Text = "Shortcut name", Dock = DockStyle.Fill });
        var name = Input(theme, "Shortcut name");
        name.Text = Path.GetFileNameWithoutExtension(document.Path);
        top.Controls.Add(name);
        var admin = new CheckBox { Text = "Administrative session (/admin)", AccessibleName = "Administrative session",
            Dock = DockStyle.Fill, Checked = document.Admin, ForeColor = theme.Text, BackColor = theme.Surface };
        var mode = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        mode.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        mode.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        mode.Controls.Add(admin, 0, 0);
        var copy = new CheckBox { Text = "Edit as an unsigned copy", Dock = DockStyle.Fill, Visible = document.HasSignature,
            ForeColor = theme.Text, BackColor = theme.Surface };
        mode.Controls.Add(copy, 1, 0);
        top.Controls.Add(mode);

        var tabs = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Margin = Padding.Empty };
        string[] tabNames = RdpPropertyPages.Titles.Concat(new[] { "Properties", "Raw file", "Review changes" }).ToArray();
        int propertiesPage = RdpPropertyPages.Titles.Length;
        int rawPage = propertiesPage + 1;
        int reviewPage = rawPage + 1;
        int activePage = 0;
        var tabButtons = tabNames.Select(label => theme.Button(label)).ToArray();
        foreach (var control in tabButtons)
        {
            control.AutoSize = true;
            control.Margin = new Padding(0, 0, 6, 0);
            tabs.Controls.Add(control);
        }
        top.Controls.Add(tabs);

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 14, 0) };
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var search = Input(theme, "Filter settings");
        var header = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        var searchBox = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        searchBox.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        searchBox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchBox.Controls.Add(new Label { Text = "Filter", ForeColor = theme.Text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        searchBox.Controls.Add(search, 1, 0);
        header.Controls.Add(searchBox, 0, 0);
        header.BackColor = theme.Surface;
        header.Controls.Add(new Label
        {
            Text = "Edit values here. Add or remove fields in Raw file.",
            AutoEllipsis = true, ForeColor = theme.Secondary, TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill, Margin = new Padding(12, 0, 0, 0)
        }, 1, 0);
        var grid = new DataGridView
        {
            AccessibleName = "RDP properties", Dock = DockStyle.Fill, BackgroundColor = theme.Card,
            BorderStyle = BorderStyle.None, GridColor = theme.Border, RowHeadersVisible = false,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            EnableHeadersVisualStyles = false, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.CellSelect, Margin = Padding.Empty
        };
        grid.DefaultCellStyle.BackColor = theme.Card;
        grid.DefaultCellStyle.ForeColor = theme.Text;
        grid.DefaultCellStyle.SelectionBackColor = theme.Hover;
        grid.DefaultCellStyle.SelectionForeColor = theme.HighContrast ? SystemColors.HighlightText : theme.Text;
        grid.ColumnHeadersDefaultCellStyle.BackColor = theme.Surface;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = theme.Text;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = theme.Surface;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = theme.Text;
        grid.RowTemplate.Height = 27;
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Setting", FillWeight = 38, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Type", Width = 44, ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", FillWeight = 62, SortMode = DataGridViewColumnSortMode.NotSortable });
        fields.Controls.Add(header, 0, 0);
        fields.Controls.Add(grid, 0, 1);
        content.Controls.Add(fields);

        var raw = Input(theme, "Raw RDP file");
        raw.Multiline = true;
        raw.WordWrap = false;
        raw.ScrollBars = ScrollBars.Both;
        raw.AcceptsReturn = true;
        raw.AcceptsTab = true;
        raw.Font = new Font("Consolas", 10f);
        raw.Visible = false;
        content.Controls.Add(raw);
        var review = Input(theme, "Changes to the RDP file");
        review.Multiline = true;
        review.ReadOnly = true;
        review.WordWrap = false;
        review.ScrollBars = ScrollBars.Both;
        review.Font = raw.Font;
        review.Visible = false;
        content.Controls.Add(review);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 67, Padding = new Padding(14, 0, 14, 0) };
        var status = new Label { Dock = DockStyle.Top, Height = 31, ForeColor = theme.Secondary };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 35, FlowDirection = FlowDirection.RightToLeft };
        var save = theme.Button("Save changes", true);
        var cancel = theme.Button("Back");
        save.AutoSize = true;
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        footer.Controls.Add(status);
        footer.Controls.Add(buttons);
        panel.Controls.Add(content);
        panel.Controls.Add(footer);
        panel.Controls.Add(top);

        bool updating = false;
        string rawBaseline = null;
        Control guided = null;
        Action updateStatus = () =>
        {
            int count = document.Lines.Count(line => { string key, type, value; return line.Field(out key, out type, out value); });
            status.ForeColor = theme.Secondary;
            status.Text = copy.Checked ? "The signed original will be kept. The copy will have no publisher signature."
                : document.HasSignature ? "Signed shortcut. Choose Edit as an unsigned copy to change properties."
                : count + " properties  ·  " + (document.Text == document.Original ? "No unsaved file changes" : "Unsaved file changes");
        };
        Action<string> showError = message =>
        {
            status.ForeColor = theme.Error;
            status.Text = message;
        };
        Action populate = () =>
        {
            updating = true;
            grid.Rows.Clear();
            string query = search.Text.Trim();
            foreach (var line in document.Lines)
            {
                string key, type, value;
                if (!line.Field(out key, out type, out value) || line.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var row = grid.Rows[grid.Rows.Add(key, type, value)];
                row.Tag = line;
            }
            admin.Checked = document.Admin;
            updating = false;
            updateStatus();
        };

        grid.CellValidating += (sender, args) =>
        {
            if (updating || args.RowIndex < 0 || args.ColumnIndex != 2)
                return;
            var row = grid.Rows[args.RowIndex];
            string value = Convert.ToString(args.FormattedValue);
            if (Convert.ToString(row.Cells[2].Value) == value)
                return;
            try
            {
                ((RdpLine)row.Tag).SetValue(value);
                updating = true;
                admin.Checked = document.Admin;
                updating = false;
                updateStatus();
            }
            catch (ArgumentException error) { args.Cancel = true; showError(error.Message); }
        };
        grid.EditingControlShowing += (sender, args) =>
        {
            args.Control.BackColor = theme.Input;
            args.Control.ForeColor = theme.Text;
        };
        search.TextChanged += (sender, args) => { if (grid.EndEdit()) populate(); };
        Action commitRaw = () =>
        {
            if (rawBaseline == null) return;
            if (raw.Text != rawBaseline)
                document.SetText(raw.Text);
            rawBaseline = null;
        };
        Action<int> show = page =>
        {
            if (!owner.ValidateChildren(ValidationConstraints.Visible | ValidationConstraints.Enabled) || !grid.EndEdit()) return;
            commitRaw();
            populate();
            activePage = page;
            fields.Visible = page == propertiesPage;
            raw.Visible = page == rawPage;
            review.Visible = page == reviewPage;
            if (guided != null) { content.Controls.Remove(guided); guided.Dispose(); guided = null; }
            if (page < propertiesPage)
            {
                guided = RdpPropertyPages.Create(page, document, theme, !document.HasSignature || copy.Checked, () =>
                {
                    updating = true;
                    admin.Checked = document.Admin;
                    updating = false;
                    updateStatus();
                }, showError);
                content.Controls.Add(guided);
                guided.BringToFront();
            }
            updating = true;
            if (page == rawPage) { raw.Text = document.Text; rawBaseline = raw.Text; }
            if (page == reviewPage) review.Text = (copy.Checked ? "The copy will omit the signature and signing scope.\r\n\r\n" : "") + DescribeChanges(document, name.Text);
            updating = false;
            for (int i = 0; i < tabButtons.Length; i++)
            {
                tabButtons[i].BackColor = i == page ? theme.Primary : theme.Surface;
                tabButtons[i].ForeColor = i == page ? theme.OnPrimary : theme.Text;
                tabButtons[i].FlatAppearance.MouseOverBackColor = i == page ? theme.Primary : theme.Surface;
                tabButtons[i].FlatAppearance.MouseDownBackColor = tabButtons[i].FlatAppearance.MouseOverBackColor;
            }
        };
        for (int i = 0; i < tabButtons.Length; i++)
        {
            int page = i;
            tabButtons[i].Click += (sender, args) => show(page);
        }
        admin.CheckedChanged += (sender, args) =>
        {
            if (updating || !grid.EndEdit()) return;
            bool requested = admin.Checked;
            commitRaw();
            document.SetAdmin(requested);
            show(activePage);
        };
        save.Click += (sender, args) =>
        {
            if (!owner.ValidateChildren(ValidationConstraints.Visible | ValidationConstraints.Enabled) || !grid.EndEdit()) return;
            commitRaw();
            try
            {
                if (copy.Checked)
                {
                    document.SaveUnsignedCopy(name.Text);
                    saved(Path.GetFileNameWithoutExtension(document.Path));
                }
                else
                {
                    document.Save(name.Text);
                    saved(name.Text.Trim());
                }
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is ArgumentException || error is UnauthorizedAccessException)) throw;
                showError(error.Message);
            }
        };
        cancel.CausesValidation = false;
        cancel.Click += (sender, args) => panel.BeginInvoke(back);
        owner.CancelButton = cancel;
        Action setEditability = () =>
        {
            bool editable = !document.HasSignature || copy.Checked;
            grid.ReadOnly = !editable;
            raw.ReadOnly = !editable;
            admin.Enabled = editable;
        };
        copy.CheckedChanged += (sender, args) =>
        {
            if (!copy.Checked)
            {
                grid.CancelEdit();
                document.SetText(document.Original);
                rawBaseline = null;
            }
            name.Text = Path.GetFileNameWithoutExtension(document.Path) + (copy.Checked ? " (unsigned copy)" : "");
            setEditability();
            show(activePage);
        };
        setEditability();
        var monoFont = raw.Font;
        panel.Disposed += (sender, args) => monoFont.Dispose();
        show(0);
        return panel;
    }

    private static TextBox Input(LauncherTheme theme, string name)
    {
        return new TextBox { AccessibleName = name, Dock = DockStyle.Fill, BackColor = theme.Input, ForeColor = theme.Text,
            BorderStyle = BorderStyle.FixedSingle };
    }

    private static string DescribeChanges(RdpDocument document, string name)
    {
        string originalName = Path.GetFileNameWithoutExtension(document.Path);
        string title = name == originalName ? "" : "Name: " + originalName + " → " + name + "\r\n\r\n";
        string after = document.Text;
        if (after == document.Original)
            return title.Length == 0 ? "No changes to review." : title;
        var previous = document.Original.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var current = after.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        if (previous.SequenceEqual(current))
            return title + "Line endings changed.";
        var removed = LinesMissingFrom(previous, current).ToArray();
        var added = LinesMissingFrom(current, previous).ToArray();
        if (removed.Length == 0 && added.Length == 0)
            return title + "Line order changed. Open Raw file to review the new order.";
        return title + String.Join("\r\n", removed.Select(line => "− " + line).Concat(added.Select(line => "+ " + line)));
    }

    private static System.Collections.Generic.IEnumerable<string> LinesMissingFrom(string[] source, string[] other)
    {
        var counts = other.GroupBy(line => line).ToDictionary(group => group.Key, group => group.Count());
        foreach (string line in source)
        {
            int count;
            if (counts.TryGetValue(line, out count) && count > 0)
                counts[line] = count - 1;
            else
                yield return line;
        }
    }
}
