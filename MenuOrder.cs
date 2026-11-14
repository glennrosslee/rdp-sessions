using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

internal sealed class MenuOrder
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    private readonly string file;
    private readonly List<string> folders = new List<string>();
    private readonly Dictionary<string, List<string>> shortcuts = new Dictionary<string, List<string>>(Names);

    public MenuOrder(string root)
    {
        file = Path.Combine(root, ".rdp-menu-order");
        if (!File.Exists(file))
            return;
        foreach (string line in File.ReadAllLines(file))
        {
            string[] fields = line.Split('\t');
            if (fields.Length == 1 && fields[0].Length > 0)
                folders.Add(fields[0]);
            if (fields.Length == 2)
                Links(fields[0]).Add(fields[1]);
        }
    }

    private List<string> Links(string group)
    {
        List<string> links;
        if (!shortcuts.TryGetValue(group, out links))
        {
            links = new List<string>();
            shortcuts.Add(group, links);
        }
        return links;
    }

    public ShortcutFolder[] Apply(ShortcutFolder[] groups)
    {
        return Ordered(groups, folders, group => group.Name)
            .Select(group => group.WithLinks(Ordered(group.Links, Links(group.Name), Path.GetFileName)))
            .ToArray();
    }

    private static IEnumerable<T> Ordered<T>(IEnumerable<T> values, IList<string> order, Func<T, string> key)
    {
        var positions = new Dictionary<string, int>(Names);
        for (int i = 0; i < order.Count; i++)
            if (!positions.ContainsKey(order[i]))
                positions.Add(order[i], i);
        return values.OrderBy(value =>
        {
            int index;
            return positions.TryGetValue(key(value), out index) ? index : Int32.MaxValue;
        });
    }

    public void Capture(ShortcutFolder[] groups)
    {
        folders.Clear();
        shortcuts.Clear();
        foreach (var group in groups)
        {
            folders.Add(group.Name);
            Links(group.Name).AddRange(group.Links.Select(Path.GetFileName));
        }
    }

    public void ReorderFolder(string source, string target, bool after)
    {
        Insert(folders, source, target, after);
        Save();
    }

    public void ReorderShortcut(string source, string oldGroup, string newGroup, string target, bool after)
    {
        Links(oldGroup).RemoveAll(name => Names.Equals(name, source));
        Insert(Links(newGroup), source, target, after);
        Save();
    }

    public void RenameFolder(string oldName, string newName)
    {
        Replace(folders, oldName, newName);
        var links = Links(oldName);
        shortcuts.Remove(oldName);
        shortcuts[newName] = links;
        Save();
    }

    public void RenameShortcut(string group, string oldName, string newName)
    {
        Replace(Links(group), oldName, newName);
        Save();
    }

    private static void Replace(List<string> list, string oldName, string newName)
    {
        int index = list.FindIndex(name => Names.Equals(name, oldName));
        if (index >= 0)
            list[index] = newName;
    }

    private static void Insert(List<string> list, string source, string target, bool after)
    {
        if (Names.Equals(source, target))
            return;
        list.RemoveAll(name => Names.Equals(name, source));
        int index = list.FindIndex(name => Names.Equals(name, target));
        list.Insert(index < 0 ? list.Count : index + (after ? 1 : 0), source);
    }

    private void Save()
    {
        string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var rows = folders.Concat(shortcuts.SelectMany(group => group.Value.Select(name => group.Key + "\t" + name)));
            File.WriteAllLines(temp, rows, new UTF8Encoding(false));
            if (File.Exists(file))
                File.Replace(temp, file, null);
            else
                File.Move(temp, file);
        }
        finally { File.Delete(temp); }
    }
}
