using System.Diagnostics;
using System.IO.Ports;

namespace ReLight.Core;

/// <summary>Searching: the strip is not plugged in (its USB device is not in the system).</summary>
public enum DeviceState { Disconnected, Searching, Connecting, Connected, Error }

/// <summary>
/// Talks to an Adalight-compatible controller (Arduino and clones) over a serial port.
/// Packet: 'A' 'd' 'a', (count-1) high byte, (count-1) low byte, high^low^0x55, then R,G,B per LED.
/// Frames are sent from a background thread; callers just hand over the newest colors.
/// Lost connections are retried automatically.
/// </summary>
public sealed class AdalightDevice : IDisposable
{
    const int KeepAliveMs = 1000;   // firmwares switch LEDs off when the PC goes quiet
    const int RetryMs = 2000;

    readonly object _lock = new();
    readonly AutoResetEvent _wake = new(false);
    byte[]? _pending;
    byte[]? _last;
    Thread? _thread;
    volatile bool _running;
    SerialPort? _port;

    public string? PortName { get; private set; }
    public int BaudRate { get; private set; }
    public DeviceState State { get; private set; } = DeviceState.Disconnected;
    public string? LastError { get; private set; }

    /// <summary>True when the last attempt failed because another program holds the port.</summary>
    public bool PortBusy { get; private set; }

    /// <summary>Frames per second actually written to the port.</summary>
    public double Fps { get; private set; }

    /// <summary>Raised from the background thread.</summary>
    public event Action<DeviceState>? StateChanged;

    Func<string?> _resolvePort = () => null;

    /// <summary>
    /// Start talking to the strip. <paramref name="resolvePort"/> is asked for the COM name before
    /// every connection attempt, so a strip that came back under another number is found again.
    /// </summary>
    public void Start(Func<string?> resolvePort, int baudRate)
    {
        Stop();
        _resolvePort = resolvePort;
        BaudRate = baudRate;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "ReLight serial" };
        _thread.Start();
    }

    public void Stop()
    {
        if (_thread != null)
        {
            _running = false;
            _wake.Set();
            _thread.Join(3000);
            _thread = null;
        }
        ClosePort();
        lock (_lock) { _pending = null; _last = null; }
        SetState(DeviceState.Disconnected);
    }

    /// <summary>Queue colors for sending. <paramref name="rgb"/> is 3 bytes per LED, already in device order.</summary>
    /// <summary>Tests: every packet handed to the device, before it reaches the port.</summary>
    internal event Action<byte[]>? PacketQueued;

    /// <summary>Tests: pretend a strip is connected without a serial port.</summary>
    internal void SimulateConnected() => SetState(DeviceState.Connected);

    public void Send(ReadOnlySpan<byte> rgb)
    {
        if (rgb.Length < 3) return;
        var packet = BuildPacket(rgb);
        PacketQueued?.Invoke(packet);
        lock (_lock) _pending = packet;
        _wake.Set();
    }

    public static byte[] BuildPacket(ReadOnlySpan<byte> rgb)
    {
        int leds = rgb.Length / 3;
        var packet = new byte[6 + leds * 3];
        int n = leds - 1;
        packet[0] = (byte)'A';
        packet[1] = (byte)'d';
        packet[2] = (byte)'a';
        packet[3] = (byte)(n >> 8);
        packet[4] = (byte)(n & 0xFF);
        packet[5] = (byte)(packet[3] ^ packet[4] ^ 0x55);
        rgb[..(leds * 3)].CopyTo(packet.AsSpan(6));
        return packet;
    }

    /// <summary>Send all LEDs black and wait until it has gone out.</summary>
    public void Blackout(int leds)
    {
        if (leds <= 0 || _port == null) return;
        Send(new byte[leds * 3]);
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 300)
        {
            lock (_lock) if (_pending == null) break;
            Thread.Sleep(10);
        }
    }

    void Loop()
    {
        var lastWrite = Stopwatch.StartNew();
        var fpsWatch = Stopwatch.StartNew();
        int frames = 0;

        while (_running)
        {
            if (_port == null)
            {
                if (!TryOpen())
                {
                    _wake.WaitOne(RetryMs);
                    continue;
                }
                lastWrite.Restart();
            }

            _wake.WaitOne(KeepAliveMs / 2);
            if (!_running) break;

            byte[]? packet;
            lock (_lock)
            {
                if (_pending != null)
                {
                    packet = _last = _pending;
                    _pending = null;
                }
                else
                {
                    packet = lastWrite.ElapsedMilliseconds >= KeepAliveMs ? _last : null;
                }
            }
            if (packet == null) continue;

            try
            {
                _port!.Write(packet, 0, packet.Length);
                lastWrite.Restart();
                frames++;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                ClosePort();
                SetState(DeviceState.Error);
                continue;
            }

            if (fpsWatch.ElapsedMilliseconds >= 1000)
            {
                Fps = frames * 1000.0 / fpsWatch.ElapsedMilliseconds;
                frames = 0;
                fpsWatch.Restart();
            }
        }
    }

    bool TryOpen()
    {
        SerialPort? port = null;
        try
        {
            PortName = _resolvePort();
            if (PortName == null)
            {
                LastError = null;
                PortBusy = false;
                SetState(DeviceState.Searching);
                return false;
            }
            SetState(DeviceState.Connecting);
            port = new SerialPort(PortName, BaudRate, Parity.None, 8, StopBits.One)
            {
                WriteTimeout = 1000,
                ReadTimeout = 100,
                Handshake = Handshake.None,
            };
            port.Open();

            // Most boards reboot when the port opens and greet with "Ada" once ready.
            // Wait for it (up to ~2.5 s) so the first frames are not lost; go on anyway if it never comes.
            var sw = Stopwatch.StartNew();
            var seen = "";
            while (sw.ElapsedMilliseconds < 2500 && _running)
            {
                try
                {
                    int b = port.ReadByte();
                    if (b < 0) continue;
                    seen += (char)b;
                    if (seen.Length > 3) seen = seen[^3..];
                    if (seen == "Ada") break;
                }
                catch (TimeoutException) { }
            }
            port.DiscardInBuffer();

            _port = port;
            LastError = null;
            PortBusy = false;
            SetState(DeviceState.Connected);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            PortBusy = ex is UnauthorizedAccessException;
            try { port?.Close(); } catch { }
            port?.Dispose();
            SetState(DeviceState.Error);
            return false;
        }
    }

    void ClosePort()
    {
        var port = _port;
        _port = null;
        if (port == null) return;
        try { port.Close(); } catch { }
        port.Dispose();
    }

    void SetState(DeviceState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }

    public void Dispose()
    {
        Stop();
        _wake.Dispose();
    }
}
