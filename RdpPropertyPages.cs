using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

internal static class RdpPropertyPages
{
    internal static readonly string[] Titles = { "Display", "Devices", "Gateway", "Advanced" };
    private const string ClientDefault = "Client default";

    private sealed class Setting
    {
        internal string Key, Label, Help, Syntax;
        internal string[] Choices;
        internal bool Toggle;
        internal int Min, Max;
    }

    private static Setting Switch(string key, string label, string help)
    {
        return new Setting { Key = key, Label = label, Help = help, Syntax = "0 = Off, 1 = On", Toggle = true };
    }

    private static Setting Choice(string key, string label, string help, params string[] choices)
    {
        return new Setting { Key = key, Label = label, Help = help, Syntax = String.Join(", ", choices.Select(ChoiceValue)), Choices = choices };
    }

    private static Setting Text(string key, string label, string help, string format)
    {
        return new Setting { Key = key, Label = label, Help = help, Syntax = format };
    }

    private static Setting Number(string key, string label, string help, int min, int max)
    {
        return new Setting { Key = key, Label = label, Help = help, Syntax = min.ToString(CultureInfo.InvariantCulture) + "–" + max.ToString(CultureInfo.InvariantCulture), Min = min, Max = max };
    }

    private static string ChoiceValue(string option) { return option.Substring(0, option.IndexOf(' ')); }
    private static string ChoiceLabel(string option) { return option.Substring(option.IndexOf(" — ", StringComparison.Ordinal) + 3) + "  (" + ChoiceValue(option) + ")"; }

    private static readonly string[][] SectionTitles =
    {
        new[] { "Window and resolution", "Monitors and scaling", "Image quality", "Window placement and presets" },
        new[] { "Local resources", "Sound and keyboard", "Peripheral and video redirection" },
        new[] { "Gateway connection", "Sign-in" },
        new[] { "Connection and sign-in", "Security and recovery", "RemoteApp and startup", "Connection performance", "Desktop appearance", "Additional sign-in settings", "Session broker and RemoteApp details" }
    };

    private static readonly int[][] SectionStarts =
    {
        new[] { 0, 3, 7, 8 }, new[] { 0, 6, 9 }, new[] { 0, 3 }, new[] { 0, 5, 9, 13, 19, 26, 30 }
    };

    private static readonly Setting[][] Settings =
    {
        new[]
        {
            Choice("screen mode id", "Open session in", "Start in a window or full screen. Multiple monitors can override this choice.", "1 — A window", "2 — Full screen"),
            Number("desktopwidth", "Desktop width", "Requested session width in pixels.", 200, 8192),
            Number("desktopheight", "Desktop height", "Requested session height in pixels.", 200, 8192),
            Switch("use multimon", "Use multiple monitors", "Span the remote desktop across local displays."),
            Text("selectedmonitors", "Choose which monitors to use", "With multiple monitors enabled, choose adjacent displays. The first ID is primary. Run mstsc /l to find the IDs.", "Comma-separated IDs, e.g. 0,1"),
            Switch("smart sizing", "Scale the image to fit", "Fit the image to the window and keep the remote desktop resolution fixed."),
            Switch("dynamic resolution", "Resize the remote resolution", "Change the remote desktop resolution when you resize the window."),
            Choice("session bpp", "Colour depth", "Request the colour depth in bits per pixel. The client and server can negotiate another value.", "8 — 8 bit", "15 — 15 bit", "16 — 16 bit", "24 — 24 bit", "32 — 32 bit"),
            Choice("desktop size id", "Resolution preset", "Pick a preset resolution. An explicit width and height override it.", "0 — 640 × 480", "1 — 800 × 600", "2 — 1024 × 768", "3 — 1280 × 1024", "4 — 1600 × 1200"),
            Choice("desktopscalefactor", "Remote desktop scale", "Change how large text and controls appear. Microsoft is retiring this setting.", "100 — 100%", "125 — 125%", "150 — 150%", "175 — 175%", "200 — 200%", "250 — 250%", "300 — 300%", "400 — 400%", "500 — 500%"),
            Text("winposstr", "Saved window placement", "Restore the saved placement of the window. Copy this value from a shortcut saved by Remote Desktop if you need exact coordinates.", "Six comma-separated integers"),
            Switch("displayconnectionbar", "Full-screen connection bar", "Show the connection bar at the top of a full-screen session."),
            Switch("span monitors", "Span monitors (legacy)", "Treat compatible displays as one large desktop. Use multiple monitors for separate remote displays.")
        },
        new[]
        {
            Switch("redirectclipboard", "Clipboard", "Share the local clipboard with the remote session."),
            Switch("redirectprinters", "Printers", "Expose local printers to the remote session."),
            Switch("redirectsmartcards", "Smart cards", "Expose local smart cards to the remote session."),
            Switch("redirectcomports", "Serial ports", "Expose local COM ports to the remote session."),
            Text("drivestoredirect", "Drives", "Choose which local drives the remote session can access.", @"Empty, *, DynamicDrives, or C:\;E:\;"),
            Text("camerastoredirect", "Cameras", "Choose which local cameras to expose to the remote session.", "Empty, *, or semicolon-separated device paths"),
            Choice("audiomode", "Sound playback", "Choose where session sound plays.", "0 — On this PC", "1 — On the remote PC", "2 — Muted"),
            Switch("audiocapturemode", "Microphone", "Send local microphone audio to the remote session."),
            Choice("keyboardhook", "Windows key shortcuts", "Choose where shortcuts such as Alt+Tab are handled.", "0 — Local", "1 — Remote desktop", "2 — Remote in full screen", "3 — RemoteApp when focused (client-dependent)"),
            Switch("redirectlocation", "Local location", "Let the session use the location of this PC, subject to Windows permissions."),
            Switch("redirectwebauthn", "Local passkeys and security keys", "Send WebAuthn prompts to this PC so you can answer with a local authenticator."),
            Text("devicestoredirect", "Media and camera peripherals", "Choose MTP or PTP devices to share with the session.", "Empty, *, DynamicDevices, or device instance paths separated by semicolons"),
            Text("usbdevicestoredirect", "USB device redirection", "Redirect supported USB devices at the USB level when policy allows it.", "Empty, *, device class GUIDs in braces, or USB instance IDs"),
            Switch("redirectposdevices", "Point-of-sale devices", "Request redirection of supported point-of-sale devices when policy allows it."),
            Switch("redirectdrives", "Drive redirection (legacy)", "Enable drive redirection on older clients. Use Drives above to choose specific drives."),
            Switch("encode redirected video capture", "Encode camera video", "Compress video captured from a redirected camera."),
            Choice("redirected video capture encoding quality", "Camera video quality", "Choose the balance between camera image quality and bandwidth.", "0 — High compression", "1 — Medium compression", "2 — Highest picture quality")
        },
        new[]
        {
            Text("gatewayhostname", "Gateway address", "Set the Remote Desktop Gateway hostname.", "Hostname, optionally with :port"),
            Choice("gatewayusagemethod", "When to use the gateway", "Controls the connection route. The gateway profile below controls which settings are used.", "0 — Connect directly", "1 — Always use the gateway", "2 — Use gateway if direct access fails", "3 — Follow the gateway profile", "4 — Connect directly (bypass selected)"),
            Choice("gatewayprofileusagemethod", "Gateway profile", "Choose administrator defaults or the gateway settings in this shortcut.", "0 — Default profile", "1 — Settings from this file"),
            Choice("gatewaycredentialssource", "Gateway authentication", "Choose how the gateway authenticates you. Available methods depend on the client.", "0 — Password (NTLM)", "1 — Smart card",
                "2 — Current Windows credentials", "3 — Basic authentication", "4 — Choose at sign-in", "5 — Cookie-based authentication"),
            Switch("promptcredentialonce", "Share sign-in", "Offer the same credentials to the gateway and remote computer.")
        },
        new[]
        {
            Text("full address", "Server address", "Set the computer Remote Desktop connects to.", "Hostname or IP, optionally with :port"),
            Number("server port", "Server port", "TCP port used when the address doesn’t specify one.", 1, 65535),
            Text("username", "User name", "Pre-fill the account name for sign-in.", @"User, DOMAIN\user, or user@example.invalid"),
            Text("domain", "Domain", "Pre-fill the sign-in domain.", "Domain name"),
            Switch(RdpDocument.AdminField, "Administrative session", "Request an administrative session on the server. The launcher passes /admin."),
            Choice("authentication level", "Server identity check", "Choose what happens if the remote server’s identity can’t be verified.", "0 — Continue without warning", "1 — Refuse connection", "2 — Ask before connecting", "3 — Unspecified"),
            Switch("enablecredsspsupport", "CredSSP", "Allow the client to use CredSSP for authentication when supported."),
            Switch("enablerdsaadauth", "Microsoft Entra authentication", "Use Microsoft Entra ID when the client and remote PC support it."),
            Switch("autoreconnection enabled", "Automatic reconnect", "Attempt to reconnect after an interruption."),
            Switch("remoteapplicationmode", "RemoteApp mode", "Request a published application instead of a full desktop."),
            Text("remoteapplicationprogram", "RemoteApp program", "Identify the published app the server should open.", "Published alias or program path"),
            Text("remoteapplicationcmdline", "RemoteApp arguments", "Pass arguments when the published app starts.", "Command-line argument string"),
            Text("alternate shell", "Alternate shell", "Request a program as the session shell.", "Executable path"),
            Choice("connection type", "Connection speed preset", "Select a network profile that influences visual quality.", "1 — Modem", "2 — Low-speed broadband", "3 — Satellite or cellular", "4 — High-speed broadband", "5 — WAN", "6 — LAN", "7 — Detect automatically"),
            Switch("networkautodetect", "Detect network type", "Let Remote Desktop detect the connection type."),
            Switch("bandwidthautodetect", "Measure bandwidth automatically", "Let Remote Desktop measure available bandwidth."),
            Switch("compression", "Compress session data", "Use RDP bulk compression for data sent to this PC."),
            Switch("bitmapcachepersistenable", "Keep the image cache", "Store reusable screen images on this PC between sessions."),
            Switch("videoplaybackmode", "Optimise video playback", "Use the efficient video path when available."),
            Switch("disable wallpaper", "Hide desktop wallpaper", "Suppress the remote desktop background when the server allows it."),
            Switch("allow font smoothing", "Smooth text edges", "Allow font smoothing in the remote session."),
            Switch("disable full window drag", "Show only outlines while dragging", "Draw a window outline in place of its contents while dragging."),
            Switch("disable menu anims", "Turn off menu animations", "Disable visual transitions for menus and windows."),
            Switch("disable cursor setting", "Turn off cursor effects", "Request simpler cursor rendering on hosts that honour this setting."),
            Switch("disable themes", "Turn off visual styles (legacy)", "Request classic visual styles on older Windows hosts."),
            Switch("allow desktop composition", "Desktop composition (legacy)", "Allow desktop composition on Windows 7 or Windows Server 2008 R2 hosts."),
            Text("alternate full address", "Alternate server address", "Supply a second name or address for the remote computer.", "Hostname or IP address"),
            Switch("prompt for credentials", "Prompt for credentials", "Ask for sign-in details when starting the connection."),
            Switch("prompt for credentials on client", "Prompt on this PC", "Request the credentials prompt on the client before the remote session."),
            Switch("negotiate security layer", "Negotiate the security layer", "Let the client negotiate an available security layer with the server. Policy can override this setting."),
            Switch("use redirection server name", "Use the redirected server name", "Use the name supplied by the redirection server after a broker redirects the connection."),
            Switch("disableconnectionsharing", "Open a separate connection", "Request a new Remote Desktop Services connection instead of sharing an existing one."),
            Text("loadbalanceinfo", "Broker routing information", "Supply the routing token received from the Remote Desktop deployment.", "Deployment-provided routing string"),
            Text("remoteapplicationname", "RemoteApp display name", "Name shown for the published application.", "Application display text"),
            Text("remoteapplicationfile", "File to open in RemoteApp", "Open this file in the published application. Local paths require drive redirection.", "File path"),
            Text("shell working directory", "Working directory", "Set the starting folder for the shell or published app.", "Directory path"),
            Choice("remoteapplicationexpandcmdline", "Expand argument variables on", "Choose where environment variables in RemoteApp arguments are resolved.", "0 — This PC", "1 — The remote session"),
            Choice("remoteapplicationexpandworkingdir", "Expand working folder variables on", "Choose where environment variables in the working directory are resolved.", "0 — This PC", "1 — The remote session")
        }
    };

    public static Control Create(int page, RdpDocument document, LauncherTheme theme, bool editable, Action changed, Action<string> showError)
    {
        var pageView = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = theme.Surface, ForeColor = theme.Text };
        var sectionFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 10, FontStyle.Bold);
        var labelFont = new Font(SystemFonts.MessageBoxFont.FontFamily, SystemFonts.MessageBoxFont.Size, FontStyle.Bold);
        pageView.Disposed += (sender, args) => { sectionFont.Dispose(); labelFont.Dispose(); };
        var rows = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 4, 9, 0), ColumnCount = 2 };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 61));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39));
        for (int index = 0; index < Settings[page].Length; index++)
        {
            int section = Array.IndexOf(SectionStarts[page], index);
            if (section >= 0)
            {
                int row = rows.RowCount++;
                rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var heading = new Label { Text = SectionTitles[page][section], Font = sectionFont, ForeColor = theme.Text,
                    AutoSize = true, Margin = new Padding(10, index == 0 ? 2 : 11, 0, 6) };
                rows.Controls.Add(heading, 0, row);
                rows.SetColumnSpan(heading, 2);
            }
            AddSetting(rows, Settings[page][index], document, theme, editable, changed, showError, labelFont);
        }
        pageView.Controls.Add(rows);
        return pageView;
    }

    private static void AddSetting(TableLayoutPanel rows, Setting setting, RdpDocument document, LauncherTheme theme,
        bool editable, Action changed, Action<string> showError, Font labelFont)
    {
        int row = rows.RowCount++;
        rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var description = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, BackColor = theme.Card,
            Padding = new Padding(11, 8, 8, 7), Margin = new Padding(0, 0, 0, 6) };
        description.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        description.Controls.Add(new Label { Text = setting.Label, Font = labelFont, ForeColor = theme.Text, AutoSize = true });
        description.Controls.Add(new Label { Text = setting.Help, ForeColor = theme.Secondary, AutoSize = true, Dock = DockStyle.Top });
        string expectedType = setting.Toggle || setting.Choices != null || setting.Max != 0 ? "i" : "s";
        description.Controls.Add(new Label { Text = "RDP field  " + setting.Key + ":" + expectedType + "  ·  Values  " + setting.Syntax,
            ForeColor = theme.Secondary, AutoSize = true, Dock = DockStyle.Top });
        rows.Controls.Add(description, 0, row);
        var editor = new Panel { Dock = DockStyle.Fill, BackColor = theme.Card, Padding = new Padding(8, 9, 10, 8), Margin = new Padding(0, 0, 0, 6) };
        rows.Controls.Add(editor, 1, row);
        string type;
        string value = document.GetValue(setting.Key, out type);
        if (value != null && !String.Equals(type, expectedType, StringComparison.OrdinalIgnoreCase))
        {
            editor.Controls.Add(new Label { Text = "Custom type — use Properties", ForeColor = theme.Secondary, Dock = DockStyle.Top });
            return;
        }
        Action<string> store = proposed =>
        {
            document.SetValue(setting.Key, expectedType, proposed);
            changed();
        };
        if (setting.Toggle && (value == null || value == "0" || value == "1"))
        {
            var toggle = new CheckBox { AccessibleName = setting.Label, Text = value == null ? ClientDefault : value == "1" ? "On" : "Off",
                ThreeState = true, CheckState = value == null ? CheckState.Indeterminate : value == "1" ? CheckState.Checked : CheckState.Unchecked,
                Dock = DockStyle.Top, Height = 30, ForeColor = theme.Text, Enabled = editable };
            toggle.CheckStateChanged += (sender, args) =>
            {
                toggle.Text = toggle.CheckState == CheckState.Indeterminate ? ClientDefault : toggle.Checked ? "On" : "Off";
                store(toggle.CheckState == CheckState.Indeterminate ? null : toggle.Checked ? "1" : "0");
            };
            editor.Controls.Add(toggle);
            return;
        }
        if (setting.Choices != null || setting.Toggle)
        {
            var choices = setting.Choices ?? new[] { "0 — Off", "1 — On" };
            var combo = new ComboBox { AccessibleName = setting.Label, Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = theme.Input, ForeColor = theme.Text, Enabled = editable, FlatStyle = FlatStyle.Flat, DrawMode = DrawMode.OwnerDrawFixed };
            combo.DrawItem += (sender, args) =>
            {
                if (args.Index < 0) return;
                bool selected = (args.State & DrawItemState.Selected) != 0;
                using (var brush = new SolidBrush(selected ? theme.Hover : theme.Input))
                    args.Graphics.FillRectangle(brush, args.Bounds);
                TextRenderer.DrawText(args.Graphics, (string)combo.Items[args.Index], combo.Font, args.Bounds,
                    selected && theme.HighContrast ? SystemColors.HighlightText : theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                args.DrawFocusRectangle();
            };
            combo.Items.Add(ClientDefault);
            combo.Items.AddRange(choices.Select(ChoiceLabel).ToArray());
            int index = Array.FindIndex(choices, option => ChoiceValue(option) == value);
            if (value != null && index < 0)
            {
                combo.Items.Add("Custom — " + value);
                combo.SelectedIndex = combo.Items.Count - 1;
            }
            else
                combo.SelectedIndex = index + 1;
            combo.SelectionChangeCommitted += (sender, args) =>
            {
                if (combo.SelectedIndex <= choices.Length)
                    store(combo.SelectedIndex == 0 ? null : ChoiceValue(choices[combo.SelectedIndex - 1]));
            };
            editor.Controls.Add(combo);
            return;
        }

        var input = new TextBox { AccessibleName = setting.Label, Text = value ?? "", BackColor = theme.Input,
            ForeColor = theme.Text, Dock = DockStyle.Top, ReadOnly = !editable };
        var reset = theme.Button("Reset to client default");
        reset.Dock = DockStyle.Bottom;
        reset.Height = 25;
        var defaultLabel = new Label { Text = ClientDefault, Dock = DockStyle.Bottom, Height = 25, ForeColor = theme.Secondary,
            TextAlign = ContentAlignment.MiddleLeft };
        Action updateDefault = () =>
        {
            reset.Visible = editable && value != null;
            defaultLabel.Visible = value == null;
        };
        reset.CausesValidation = false;
        editor.Controls.Add(input);
        editor.Controls.Add(reset);
        editor.Controls.Add(defaultLabel);
        updateDefault();
        input.Validating += (sender, args) =>
        {
            if (!editable || input.Text == (value ?? "")) return;
            string proposed = setting.Max == 0 ? input.Text : input.Text.Trim();
            int number;
            if (setting.Max != 0 && proposed.Length != 0 &&
                (!Int32.TryParse(proposed, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number < setting.Min || number > setting.Max))
            {
                args.Cancel = true;
                showError(setting.Label + " must be between " + setting.Min + " and " + setting.Max + ".");
                return;
            }
            value = proposed.Length == 0 && setting.Max != 0 ? null : proposed;
            updateDefault();
            store(value);
        };
        reset.Click += (sender, args) => { value = null; input.Clear(); updateDefault(); store(null); };
    }
}
