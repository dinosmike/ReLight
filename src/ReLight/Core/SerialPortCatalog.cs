using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;

namespace ReLight.Core;

/// <summary>A serial port together with the USB device behind it.</summary>
public sealed record SerialPortInfo(string PortName, string FriendlyName, string? DeviceId, string? HardwareKey)
{
    /// <summary>Chips that Arduino boards and their clones use for USB.</summary>
    public bool LooksLikeLedController =>
        HardwareKey is "VID_1A86&PID_7523" or "VID_1A86&PID_5523"   // CH340 / CH341
            or "VID_10C4&PID_EA60"                                  // CP2102
            or "VID_0403&PID_6001"                                  // FTDI
        || HardwareKey?.StartsWith("VID_2341") == true              // genuine Arduino
        || FriendlyName.Contains("CH340", StringComparison.OrdinalIgnoreCase)
        || FriendlyName.Contains("Arduino", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Finds serial ports and remembers the strip by its USB device, not by COM number:
/// Windows may hand out a different COM number after replugging or a driver update.
/// </summary>
public static partial class SerialPortCatalog
{
    public static IReadOnlyList<SerialPortInfo> List()
    {
        var found = new Dictionary<string, SerialPortInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            foreach (ManagementObject o in searcher.Get())
            {
                using (o)
                {
                    var name = o["Name"] as string ?? "";
                    var m = PortInName().Match(name);
                    if (!m.Success) continue;
                    var port = m.Groups[1].Value.ToUpperInvariant();
                    var friendly = name[..m.Index].Trim();
                    var id = o["PNPDeviceID"] as string;
                    found[port] = new SerialPortInfo(port, friendly, id, HardwareKeyOf(id));
                }
            }
        }
        catch
        {
            // WMI can be broken or disabled; plain port names below still work.
        }

        try
        {
            foreach (var p in SerialPort.GetPortNames())
                found.TryAdd(p.ToUpperInvariant(), new SerialPortInfo(p.ToUpperInvariant(), "", null, null));
        }
        catch { }

        return found.Values.OrderBy(p => PortNumber(p.PortName)).ToList();
    }

    /// <summary>
    /// The port the remembered strip is on now: the very same USB device first,
    /// then the only device of the same model, then the old COM name if it still exists.
    /// </summary>
    public static SerialPortInfo? Resolve(IReadOnlyList<SerialPortInfo> ports, string? deviceId, string? hardwareKey, string? portName)
    {
        if (deviceId != null && ports.FirstOrDefault(p => string.Equals(p.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)) is { } same)
            return same;
        if (hardwareKey != null)
        {
            var model = ports.Where(p => p.HardwareKey == hardwareKey).ToList();
            if (model.Count == 1) return model[0];
        }
        return ports.FirstOrDefault(p => string.Equals(p.PortName, portName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>On the very first start: the strip if exactly one port looks like it.</summary>
    public static SerialPortInfo? Guess(IReadOnlyList<SerialPortInfo> ports)
    {
        var likely = ports.Where(p => p.LooksLikeLedController).ToList();
        return likely.Count == 1 ? likely[0] : null;
    }

    static string? HardwareKeyOf(string? deviceId)
    {
        if (deviceId == null) return null;
        var m = VidPid().Match(deviceId);
        return m.Success ? $"VID_{m.Groups[1].Value}&PID_{m.Groups[2].Value}".ToUpperInvariant() : null;
    }

    static int PortNumber(string name) =>
        int.TryParse(new string(name.Where(char.IsDigit).ToArray()), out var n) ? n : int.MaxValue;

    [GeneratedRegex(@"\((COM\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex PortInName();

    [GeneratedRegex(@"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VidPid();
}
