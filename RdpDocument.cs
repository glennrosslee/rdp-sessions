using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class RdpLine
{
    private static readonly Regex Property = new Regex(@"^([^:\r\n]+):([^:\r\n]+):(.*)$");
    public string Text;
    public string End;

    public RdpLine(string text, string end) { Text = text; End = end; }

    public bool Field(out string name, out string type, out string value)
    {
        Match match = Property.Match(Text);
        name = match.Groups[1].Value;
        type = match.Groups[2].Value;
        value = match.Groups[3].Value;
        return match.Success;
    }

    public void SetValue(string value)
    {
        string name, type, previous;
        if (!Field(out name, out type, out previous) || value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            throw new ArgumentException("Enter a single-line value. Use Raw file to change the field structure.");
        Text = name + ":" + type + ":" + value;
    }
}

internal sealed class RdpDocument
{
    public const string AdminField = "administrative session";
    public readonly string Path;
    public readonly string Original;
    public readonly Encoding Encoding;
    public readonly byte[] OriginalBytes;
    public readonly List<RdpLine> Lines = new List<RdpLine>();

    public RdpDocument(string path)
    {
        ShortcutStore.CheckShortcutPath(path);
        Path = path;
        OriginalBytes = File.ReadAllBytes(path);
        using (var buffer = new MemoryStream(OriginalBytes))
        using (var reader = new StreamReader(buffer, new UTF8Encoding(false, true), true))
        {
            try
            {
                Original = reader.ReadToEnd();
                Encoding = reader.CurrentEncoding;
            }
            catch (DecoderFallbackException)
            {
                Encoding = Encoding.Default;
                Original = Encoding.GetString(OriginalBytes);
            }
        }
        SetText(Original);
    }

    public string Text { get { return String.Concat(Lines.Select(line => line.Text + line.End)); } }
    public bool HasSignature { get { return ContainsProperty(Original, "signature"); } }
    public bool Admin { get { return ReadAdmin(Lines); } }

    public static bool ReadAdmin(string path)
    {
        try { return ReadAdmin(Parse(File.ReadAllText(path, Encoding.Default))); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool ReadAdmin(IEnumerable<RdpLine> lines)
    {
        return lines.Where(line => IsProperty(line, AdminField)).Select(line =>
        {
            string key, type, value;
            line.Field(out key, out type, out value);
            return type.Equals("i", StringComparison.OrdinalIgnoreCase) && value.Trim() == "1";
        }).LastOrDefault();
    }

    private static bool ContainsProperty(string text, string property)
    {
        return Parse(text).Any(line => IsProperty(line, property));
    }

    private static bool IsProperty(RdpLine line, string property)
    {
        string key, type, value;
        return line.Field(out key, out type, out value) && key.Trim().Equals(property, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<RdpLine> Parse(string text)
    {
        foreach (Match match in Regex.Matches(text, @"([^\r\n]*)(\r\n|\r|\n|$)"))
            if (match.Length != 0)
                yield return new RdpLine(match.Groups[1].Value, match.Groups[2].Value);
    }

    public void SetText(string text)
    {
        Lines.Clear();
        Lines.AddRange(Parse(text));
    }

    public void SetAdmin(bool enabled)
    {
        SetValue(AdminField, "i", enabled ? "1" : "0");
    }

    public string GetValue(string key, out string type)
    {
        var line = Lines.LastOrDefault(candidate => IsProperty(candidate, key));
        type = null;
        if (line == null) return null;
        string name, value;
        line.Field(out name, out type, out value);
        return value;
    }

    public void SetValue(string key, string type, string value)
    {
        if (value == null)
        {
            Lines.RemoveAll(line => IsProperty(line, key));
            return;
        }
        string entry = FormatField(key, type, value);
        var last = Lines.LastOrDefault(line => IsProperty(line, key));
        if (last == null) Add(entry);
        else last.Text = entry;
    }

    public RdpLine Add(string text)
    {
        string separator = Lines.Select(line => line.End).FirstOrDefault(end => end.Length != 0) ?? "\r\n";
        if (Lines.Count > 0 && Lines[Lines.Count - 1].End.Length == 0)
            Lines[Lines.Count - 1].End = separator;
        var field = new RdpLine(text, separator);
        Lines.Add(field);
        return field;
    }

    public static string FormatField(string key, string type, string value)
    {
        key = key.Trim();
        type = type.Trim();
        if (key.Length == 0 || key.IndexOfAny(new[] { ':', '\r', '\n' }) >= 0 ||
            type.Length != 1 || !Char.IsLetter(type[0]) || value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            throw new ArgumentException("Enter a setting name, a single-letter type, and a one-line value.");
        return key + ":" + type + ":" + value;
    }

    public bool Save(string name)
    {
        return ShortcutStore.SaveRdpDocument(this, name);
    }

    public void SaveUnsignedCopy(string name)
    {
        string text = String.Concat(Lines.Where(line => !IsProperty(line, "signature") && !IsProperty(line, "signscope"))
            .Select(line => line.Text + line.End));
        ShortcutStore.SaveRdpCopy(this, name, text);
    }
}
