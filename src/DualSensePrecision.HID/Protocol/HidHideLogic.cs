using System.Text;
using System.Text.RegularExpressions;

namespace DualSensePrecision.HID.Protocol;

/// <summary>
/// The decision-making half of HidHide support, with no Windows calls so it's unit-tested anywhere:
/// list encoding, which devices to hide, and adding/removing only the entries this app owns.
/// </summary>
public static partial class HidHideLogic
{
    /// <summary>Splits a REG_MULTI_SZ-style buffer (strings separated by NUL, ending with an empty string).</summary>
    public static List<string> ParseMultiSz(ReadOnlySpan<char> buffer)
    {
        var list = new List<string>();
        int start = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != '\0') continue;
            if (i == start) break; // empty string = end of list
            list.Add(new string(buffer[start..i]));
            start = i + 1;
        }
        return list;
    }

    /// <summary>Encodes strings as a double-NUL-terminated buffer, as HidHide expects.</summary>
    public static char[] ToMultiSz(IEnumerable<string> items)
    {
        var sb = new StringBuilder();
        foreach (var s in items)
        {
            if (string.IsNullOrEmpty(s)) continue;
            sb.Append(s).Append('\0');
        }
        if (sb.Length == 0) sb.Append('\0');
        sb.Append('\0');
        return sb.ToString().ToCharArray();
    }

    [GeneratedRegex(@"VID[_&]([0-9A-F]{4,8}).*?PID[_&]([0-9A-F]{4,8})", RegexOptions.IgnoreCase)]
    private static partial Regex VidPidRegex();

    /// <summary>
    /// Vendor/product ID from a device instance ID. Handles USB (VID_045E&amp;PID_0B12) and Bluetooth forms
    /// (VID&amp;0002045E_PID&amp;0B13, VID&amp;02045E_PID&amp;0B13), where the ID is the last four hex digits.
    /// </summary>
    public static (ushort Vid, ushort Pid)? ParseVidPid(string instanceId)
    {
        var m = VidPidRegex().Match(instanceId);
        if (!m.Success) return null;
        string v = m.Groups[1].Value, p = m.Groups[2].Value;
        return (Convert.ToUInt16(v[^4..], 16), Convert.ToUInt16(p[^4..], 16));
    }

    /// <summary>
    /// True if a device sits under the usbip-win2 virtual host controller (ROOT\USBIP_WIN2\…) that VIIPER uses —
    /// i.e. it's the app's own virtual pad, which must never be hidden from games.
    /// </summary>
    public static bool IsUnderVirtualHost(IEnumerable<string> ancestorIds) =>
        ancestorIds.Any(id => id.Contains("USBIP", StringComparison.OrdinalIgnoreCase) || id.Contains("VIIPER", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Never hide hubs, host controllers or anything root-enumerated: only nodes that carry the controller's own VID/PID.
    /// </summary>
    public static bool IsSafeToHide(string instanceId, (ushort Vid, ushort Pid) controller) =>
        !instanceId.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase) &&
        !instanceId.Contains("ROOT_HUB", StringComparison.OrdinalIgnoreCase) &&
        ParseVidPid(instanceId) == controller;

    /// <summary>Adds <paramref name="targets"/> to a list, returning the new list and which entries were actually added (so only those are removed later).</summary>
    public static (List<string> List, List<string> Added) AddEntries(IEnumerable<string> existing, IEnumerable<string> targets)
    {
        var list = existing.ToList();
        var added = new List<string>();
        foreach (var t in targets)
        {
            if (list.Contains(t, StringComparer.OrdinalIgnoreCase)) continue;
            list.Add(t);
            added.Add(t);
        }
        return (list, added);
    }

    /// <summary>Removes only the given entries, leaving anything the user or other apps put there.</summary>
    public static List<string> RemoveEntries(IEnumerable<string> existing, IEnumerable<string> ours)
    {
        var drop = new HashSet<string>(ours, StringComparer.OrdinalIgnoreCase);
        return existing.Where(e => !drop.Contains(e)).ToList();
    }

    /// <summary>
    /// "C:\Program Files\x.exe" → "\Device\HarddiskVolume3\Program Files\x.exe" — the form HidHide's application list uses.
    /// <paramref name="queryDosDevice"/> maps "C:" to its NT device name.
    /// </summary>
    public static string? ToDosDevicePath(string win32Path, Func<string, string?> queryDosDevice)
    {
        if (win32Path.Length < 3 || win32Path[1] != ':' || win32Path[2] != '\\') return null;
        var device = queryDosDevice(win32Path[..2].ToUpperInvariant());
        return device == null ? null : device.TrimEnd('\\') + win32Path[2..];
    }
}
