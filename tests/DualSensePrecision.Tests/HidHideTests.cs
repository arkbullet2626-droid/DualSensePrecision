using DualSensePrecision.HID.Protocol;
using Xunit;

namespace DualSensePrecision.Tests;

public class HidHideLogicTests
{
    [Fact]
    public void MultiSzRoundTrips()
    {
        var items = new[] { @"HID\VID_054C&PID_0CE6&MI_03\7&1", @"USB\VID_045E&PID_0B12\3032" };
        var buf = HidHideLogic.ToMultiSz(items);
        Assert.Equal('\0', buf[^1]);
        Assert.Equal('\0', buf[^2]);
        Assert.Equal(items, HidHideLogic.ParseMultiSz(buf));
    }

    [Fact]
    public void EmptyListIsDoubleNul()
    {
        Assert.Equal(new[] { '\0', '\0' }, HidHideLogic.ToMultiSz(Array.Empty<string>()));
        Assert.Empty(HidHideLogic.ParseMultiSz(new[] { '\0', '\0' }));
        Assert.Empty(HidHideLogic.ParseMultiSz(ReadOnlySpan<char>.Empty));
    }

    [Theory]
    [InlineData(@"USB\VID_045E&PID_0B12\3032363030", 0x045E, 0x0B12)]
    [InlineData(@"HID\VID_054C&PID_0CE6&MI_03\7&2a8c&0&0000", 0x054C, 0x0CE6)]
    [InlineData(@"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002045e_PID&0b13&IG_00\9&1", 0x045E, 0x0B13)]
    [InlineData(@"BTHLEDEVICE\{00001812}_DEV_VID&02045e_PID&0b13_REV&0509\8&1", 0x045E, 0x0B13)]
    public void ParsesVidPidInEveryForm(string id, int vid, int pid) =>
        Assert.Equal(((ushort)vid, (ushort)pid), HidHideLogic.ParseVidPid(id));

    [Fact]
    public void NoVidPidIsNull() => Assert.Null(HidHideLogic.ParseVidPid(@"ROOT\USBIP_WIN2\0000"));

    [Fact]
    public void VirtualPadIsRecognisedByItsHost()
    {
        Assert.True(HidHideLogic.IsUnderVirtualHost(new[] { @"USB\ROOT_HUB30\5&1", @"ROOT\USBIP_WIN2\0000" }));
        Assert.False(HidHideLogic.IsUnderVirtualHost(new[] { @"USB\ROOT_HUB30\4&2", @"PCI\VEN_8086&DEV_A36D\3&11583659&0&A0" }));
    }

    [Fact]
    public void NeverHidesHubsOrRootDevicesOrOtherDevices()
    {
        var pad = ((ushort)0x045E, (ushort)0x0B12);
        Assert.True(HidHideLogic.IsSafeToHide(@"USB\VID_045E&PID_0B12\3032", pad));
        Assert.False(HidHideLogic.IsSafeToHide(@"USB\ROOT_HUB30\4&2", pad));
        Assert.False(HidHideLogic.IsSafeToHide(@"USB\VID_05E3&PID_0610\6&1", pad));      // a USB hub
        Assert.False(HidHideLogic.IsSafeToHide(@"ROOT\USBIP_WIN2\0000", pad));
    }

    [Fact]
    public void OnlyOurEntriesAreAddedAndRemoved()
    {
        var existing = new[] { @"HID\SOMEONE_ELSES\1", @"usb\vid_045e&pid_0b12\3032" };
        var (list, added) = HidHideLogic.AddEntries(existing, new[] { @"USB\VID_045E&PID_0B12\3032", @"HID\VID_045E&PID_0B12&IG_00\1" });
        Assert.Equal(3, list.Count);
        Assert.Equal(new[] { @"HID\VID_045E&PID_0B12&IG_00\1" }, added); // the other was already there (case-insensitive)

        var after = HidHideLogic.RemoveEntries(list, added);
        Assert.Equal(existing, after); // exactly what was there before
    }

    [Fact]
    public void DosDevicePathConversion()
    {
        string? Map(string d) => d == "C:" ? @"\Device\HarddiskVolume3" : null;
        Assert.Equal(@"\Device\HarddiskVolume3\Users\ryan\AppData\Local\Programs\DualSense Precision\DualSensePrecision.exe",
            HidHideLogic.ToDosDevicePath(@"c:\Users\ryan\AppData\Local\Programs\DualSense Precision\DualSensePrecision.exe", Map));
        Assert.Null(HidHideLogic.ToDosDevicePath(@"D:\x.exe", Map));
        Assert.Null(HidHideLogic.ToDosDevicePath(@"\\server\share\x.exe", Map));
    }
}
