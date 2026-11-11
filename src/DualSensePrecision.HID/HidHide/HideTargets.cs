using DualSensePrecision.HID.Protocol;

namespace DualSensePrecision.HID.HidHide;

/// <summary>Works out which device instance IDs to hide so games only see the virtual pad.</summary>
public static class HideTargets
{
    /// <summary>The DualSense's HID node (what games open) plus its USB interface parent.</summary>
    public static List<string> ForDualSense(string interfacePath)
    {
        var result = new List<string>();
        var inst = DeviceTree.InstanceIdOf(interfacePath);
        if (inst == null || inst.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase)) return result;
        result.Add(inst);
        if (HidHideLogic.ParseVidPid(inst) is { } vp) AddParents(inst, vp, result);
        return result;
    }

    /// <summary>
    /// Every real Xbox controller: the XInput (XUSB) device, its USB parents with the same VID/PID (XInput can open the
    /// USB node directly), and matching HID nodes (what DirectInput/SDL open). Anything under VIIPER's virtual host is skipped.
    /// </summary>
    public static List<string> ForXboxControllers()
    {
        var result = new List<string>();
        var realIds = new HashSet<(ushort, ushort)>();

        foreach (var iface in DeviceTree.Interfaces(DeviceTree.XusbInterface))
        {
            var inst = DeviceTree.InstanceIdOf(iface);
            if (inst == null || inst.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase)) continue;
            if (HidHideLogic.IsUnderVirtualHost(DeviceTree.AncestorIds(inst).Append(inst))) continue; // our virtual pad
            if (!result.Contains(inst, StringComparer.OrdinalIgnoreCase)) result.Add(inst);
            if (HidHideLogic.ParseVidPid(inst) is { } vp)
            {
                realIds.Add(vp);
                AddParents(inst, vp, result);
            }
        }

        foreach (var iface in DeviceTree.Interfaces(DeviceTree.HidInterface))
        {
            var inst = DeviceTree.InstanceIdOf(iface);
            if (inst == null || HidHideLogic.ParseVidPid(inst) is not { } vp || !realIds.Contains(vp)) continue;
            if (HidHideLogic.IsUnderVirtualHost(DeviceTree.AncestorIds(inst).Append(inst))) continue;
            if (!result.Contains(inst, StringComparer.OrdinalIgnoreCase)) result.Add(inst);
            AddParents(inst, vp, result);
        }
        return result;
    }

    private static void AddParents(string inst, (ushort, ushort) vp, List<string> result)
    {
        var cur = inst;
        for (int i = 0; i < 3; i++)
        {
            var parent = DeviceTree.ParentOf(cur);
            if (parent == null || !HidHideLogic.IsSafeToHide(parent, vp)) return;
            if (!result.Contains(parent, StringComparer.OrdinalIgnoreCase)) result.Add(parent);
            cur = parent;
        }
    }
}
