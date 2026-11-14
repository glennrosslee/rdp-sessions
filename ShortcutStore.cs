using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class ShortcutFolder
{
    public string Name { get; private set; }
    public readonly string Path;
    public readonly string[] Links;

    public ShortcutFolder(string name, string path)
        : this(name, path, Directory.Exists(path)
            ? Directory.GetFiles(path, "*.rdp").Where(file => String.Equals(System.IO.Path.GetExtension(file), ".rdp", StringComparison.OrdinalIgnoreCase)
                && !ShortcutStore.IsNameSurrogate(file))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToArray()
            : new string[0])
    {
    }

    private ShortcutFolder(string name, string path, string[] links)
    {
        Name = name;
        Path = path;
        Links = links;
    }

    public ShortcutFolder WithLinks(System.Collections.Generic.IEnumerable<string> links)
    {
        return new ShortcutFolder(Name, Path, links.ToArray());
    }
}

internal static class ShortcutStore
{
    private const string Archive = "_archive";
    public const string RootGroup = "Other";
    private const string AddressKey = "full address:s:";
    private static readonly Regex ServerPattern = new Regex(
        @"\A(?<name>[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*)(?::(?<port>[0-9]{1,5}))?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string ResolveRoot()
    {
        string configured = Environment.GetEnvironmentVariable("RDP_SESSIONS_HOME");
        if (!String.IsNullOrWhiteSpace(configured))
            return Directory.CreateDirectory(configured).FullName;

        string localSettings = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shortcut-root.txt");
        if (File.Exists(localSettings))
            return Directory.CreateDirectory(File.ReadAllText(localSettings).Trim()).FullName;
        return Directory.CreateDirectory(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Remote Desktop Shortcuts")).FullName;
    }

    public static ShortcutFolder[] ReadFolders(string root)
    {
        var groups = Directory.GetDirectories(root)
            .Where(path => !NameIs(Path.GetFileName(path), Archive) &&
                !NameIs(Path.GetFileName(path), RootGroup) &&
                IsOrdinaryFolder(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new ShortcutFolder(Path.GetFileName(path), path));
        return groups.Concat(new[] { new ShortcutFolder(RootGroup, root) }).ToArray();
    }

    private static bool NameIs(string name, string expected)
    {
        return String.Equals(name, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOrdinaryFolder(string path)
    {
        FileAttributes flags = File.GetAttributes(path);
        return (flags & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !IsNameSurrogate(path);
    }

    internal static bool IsNameSurrogate(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) return false;
        // Resolve the tag without following the entry. Cloud placeholders are not name surrogates.
        FindData data;
        IntPtr handle = FindFirstFile(path, out data);
        if (handle == new IntPtr(-1))
            throw new IOException("Could not inspect a shortcut folder entry.");
        try { return (data.Reserved0 & 0x20000000) != 0; }
        finally { FindClose(handle); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FindData
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Modified;
        public uint SizeHigh, SizeLow, Reserved0, Reserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string AlternateName;
    }

    [DllImport("kernel32.dll", EntryPoint = "FindFirstFileW", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindFirstFile(string path, out FindData data);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr handle);

    internal static void CheckShortcutPath(string path)
    {
        if (IsNameSurrogate(path))
            throw new IOException("This shortcut or its folder redirects to a different location.");
        CheckShortcutFolder(Path.GetDirectoryName(path));
    }

    private static void CheckShortcutFolder(string folder)
    {
        if (IsNameSurrogate(folder) && !NameIs(folder.TrimEnd('\\'), ResolveRoot().TrimEnd('\\')))
            throw new IOException("This shortcut folder redirects to a different location.");
    }

    private static void CheckArchivePath(string path)
    {
        if (Directory.Exists(path) && IsNameSurrogate(path))
        {
            throw new IOException("The archive folder redirects to a different location.");
        }
    }

    public static void CreateFolder(string root, string input)
    {
        string path = FolderPath(root, input);
        if (Directory.Exists(path) || File.Exists(path))
            throw new IOException("A folder or file with that name already exists.");
        Directory.CreateDirectory(path);
    }

    private static string FolderPath(string root, string input)
    {
        string name = input.Trim();
        ValidateFileName(name);
        if (NameIs(name, Archive) || NameIs(name, RootGroup))
            throw new ArgumentException("That name is already used by the launcher.");
        return Path.Combine(root, name);
    }

    public static void RenameFolder(string root, ShortcutFolder folder, string input)
    {
        if (folder.Name == RootGroup)
            throw new ArgumentException("Other represents the main shortcut folder.");
        CheckShortcutFolder(folder.Path);
        string target = FolderPath(root, input);
        if (String.Equals(folder.Path, target, StringComparison.Ordinal))
            return;
        if (NameIs(folder.Path, target))
        {
            // Directory.Move rejects case-only renames on Windows.
            string temporary = Path.Combine(root, "." + Guid.NewGuid().ToString("N"));
            Directory.Move(folder.Path, temporary);
            try { Directory.Move(temporary, target); }
            catch { Directory.Move(temporary, folder.Path); throw; }
            return;
        }
        Directory.Move(folder.Path, target);
    }

    public static void MoveConnection(string path, ShortcutFolder destination)
    {
        CheckShortcutPath(path);
        CheckShortcutFolder(destination.Path);
        string target = Path.Combine(destination.Path, Path.GetFileName(path));
        if (NameIs(path, target))
            return;
        if (File.Exists(target))
            throw new IOException("A shortcut with this name already exists in the destination.");
        File.Move(path, target);
    }

    public static void ArchiveItem(string root, ShortcutFolder folder, string connection)
    {
        if (connection == null && folder.Name == RootGroup)
            throw new ArgumentException("Other represents the main shortcut folder.");
        string item = connection ?? folder.Path;
        if (connection != null) CheckShortcutPath(item);
        else if (IsNameSurrogate(item)) throw new IOException("This folder redirects to a different location.");
        string relative = folder.Name == RootGroup ? "" : folder.Name;
        string archiveRoot = Path.Combine(root, Archive);
        string removed = Path.Combine(archiveRoot, "Removed");
        CheckArchivePath(archiveRoot);
        CheckArchivePath(removed);
        string archive = Path.Combine(removed, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string destination = connection == null ? archive : Path.Combine(archive, relative);
        Directory.CreateDirectory(destination);
        string target = Path.Combine(destination, Path.GetFileName(item));
        if (connection == null)
            Directory.Move(item, target);
        else
            File.Move(item, target);
    }

    public static bool SaveRdpDocument(RdpDocument document, string displayName)
    {
        string path = document.Path;
        CheckShortcutPath(path);
        string name = displayName.Trim();
        ValidateFileName(name);
        string target = Path.Combine(Path.GetDirectoryName(path), name + Path.GetExtension(path));
        if (!NameIs(path, target) && File.Exists(target))
            throw new IOException("A shortcut with this name already exists in this folder.");
        if (!File.ReadAllBytes(path).SequenceEqual(document.OriginalBytes))
            throw new IOException("The shortcut changed on disk. Reopen the editor to load the latest version.");

        string content = document.Text;
        if (content == document.Original)
        {
            if (String.Equals(path, target, StringComparison.Ordinal))
                return false;
            File.Move(path, target);
            return true;
        }
        if (document.HasSignature)
            throw new ArgumentException("This shortcut is signed by its publisher. You can rename it, but changing its contents would invalidate the signature.");

        if (NameIs(path, target))
        {
            WriteShortcut(path, content, document.Encoding, true);
            if (!String.Equals(path, target, StringComparison.Ordinal))
                File.Move(path, target);
            return true;
        }

        WriteShortcut(target, content, document.Encoding, false);
        try { File.Delete(path); }
        catch { File.Delete(target); throw; }
        return true;
    }

    public static void SaveRdpCopy(RdpDocument document, string displayName, string content)
    {
        CheckShortcutPath(document.Path);
        string name = displayName.Trim();
        ValidateFileName(name);
        string target = Path.Combine(Path.GetDirectoryName(document.Path), name + Path.GetExtension(document.Path));
        if (File.Exists(target))
            throw new IOException("Choose a new name for the unsigned copy.");
        if (!File.ReadAllBytes(document.Path).SequenceEqual(document.OriginalBytes))
            throw new IOException("The shortcut changed on disk. Reopen the editor to load the latest version.");
        WriteShortcut(target, content, document.Encoding, false);
    }
    private static string ValidateServer(string input)
    {
        string server = input.Trim().ToLowerInvariant();
        Match match = ServerPattern.Match(server);
        int port;
        if (!match.Success || match.Groups["name"].Length > 253 ||
            (match.Groups["port"].Success && (!Int32.TryParse(match.Groups["port"].Value, out port) || port < 1 || port > 65535)))
            throw new ArgumentException("Enter a server name or IPv4 address, optionally followed by :port.");

        if (match.Groups["port"].Success)
            server = match.Groups["name"].Value + ":" + Int32.Parse(match.Groups["port"].Value).ToString(CultureInfo.InvariantCulture);
        return server;
    }

    public static void SaveConnection(ShortcutFolder folder, string input)
    {
        string server = ValidateServer(input);
        string[] parts = server.Split(':');
        string name = parts.Length == 1 ? parts[0] : parts[0] + " (port " + parts[1] + ")";
        ValidateFileName(name);
        Directory.CreateDirectory(folder.Path);
        CheckShortcutFolder(folder.Path);
        string target = Path.Combine(folder.Path, name + ".rdp");
        if (File.Exists(target))
            throw new IOException("A connection with that name already exists in this folder.");

        WriteShortcut(target, AddressKey + server + "\r\n", Encoding.Unicode, false);
    }

    private static void WriteShortcut(string target, string content, Encoding encoding, bool replace)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(target), "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, content, encoding);
            if (replace)
                File.Replace(temporary, target, null);
            else
                File.Move(temporary, target);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void ValidateFileName(string name)
    {
        if (String.IsNullOrWhiteSpace(name) || name.Length > 240 || name.EndsWith(".", StringComparison.Ordinal) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            Regex.IsMatch(name, @"\A(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³]) *(\.|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new ArgumentException("Enter a valid Windows name. Slashes and reserved names such as CON are unavailable.");
    }
}
