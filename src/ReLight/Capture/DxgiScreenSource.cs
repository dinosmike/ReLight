using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ReLight.Capture;

/// <summary>
/// Fast capture with DXGI Desktop Duplication. The video card shrinks the picture itself
/// (mipmaps), so only a few kilobytes per frame travel to the CPU even on a 4K TV.
/// </summary>
public sealed class DxgiScreenSource : IScreenSource
{
    const int TargetWidth = 160;

    readonly ID3D11Device _device;
    readonly ID3D11DeviceContext _context;
    readonly IDXGIOutputDuplication _duplication;
    readonly ID3D11Texture2D _mipTexture;
    readonly ID3D11ShaderResourceView _mipView;
    readonly ID3D11Texture2D _staging;
    readonly int _mipLevel;
    readonly ScreenFrame _frame;
    bool _hasFrame;

    public string Name => "DXGI";

    DxgiScreenSource(IDXGIAdapter1 adapter, IDXGIOutput1 output)
    {
        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0],
            out _device!, out _context!).CheckError();
        _duplication = output.DuplicateOutput(_device);

        var mode = _duplication.Description.ModeDescription;
        int w = (int)mode.Width, h = (int)mode.Height;
        int levels = 1;
        while ((w >> (levels - 1)) > TargetWidth && (h >> (levels - 1)) > 2) levels++;
        _mipLevel = levels - 1;

        _mipTexture = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = w,
            Height = h,
            MipLevels = levels,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            MiscFlags = ResourceOptionFlags.GenerateMips,
        });
        _mipView = _device.CreateShaderResourceView(_mipTexture);

        int sw = Math.Max(1, w >> _mipLevel), sh = Math.Max(1, h >> _mipLevel);
        _staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = sw,
            Height = sh,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
        });
        _frame = new ScreenFrame(sw, sh);
    }

    /// <summary>Open duplication for the monitor with this Windows device name (\\.\DISPLAY2).</summary>
    public static DxgiScreenSource? TryCreate(string deviceName)
    {
        if (DXGI.CreateDXGIFactory1(out IDXGIFactory1? factory).Failure || factory == null) return null;
        using (factory)
        {
            for (int a = 0; factory.EnumAdapters1(a, out IDXGIAdapter1? adapter).Success; a++)
            {
                using (adapter)
                {
                    for (int o = 0; adapter.EnumOutputs(o, out IDXGIOutput? output).Success; o++)
                    {
                        using (output)
                        {
                            if (!string.Equals(output.Description.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                                continue;
                            using var output1 = output.QueryInterface<IDXGIOutput1>();
                            return new DxgiScreenSource(adapter, output1);
                        }
                    }
                }
            }
        }
        return null;
    }

    public bool TryGrab(out ScreenFrame? frame)
    {
        frame = null;
        var result = _duplication.AcquireNextFrame(20, out _, out IDXGIResource? resource);
        if (result.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code)
        {
            // Nothing changed on screen: the previous picture is still right.
            frame = _hasFrame ? _frame : null;
            return _hasFrame;
        }
        result.CheckError(); // access lost (mode change, UAC, fullscreen game) → caller recreates us

        try
        {
            using (resource)
            using (var texture = resource!.QueryInterface<ID3D11Texture2D>())
                _context.CopySubresourceRegion(_mipTexture, 0, 0, 0, 0, texture, 0);
        }
        finally
        {
            _duplication.ReleaseFrame();
        }

        _context.GenerateMips(_mipView);
        _context.CopySubresourceRegion(_staging, 0, 0, 0, 0, _mipTexture, _mipLevel);
        var map = _context.Map(_staging, 0, MapMode.Read);
        try
        {
            int rowBytes = _frame.Width * 4;
            for (int y = 0; y < _frame.Height; y++)
                Marshal.Copy(map.DataPointer + (nint)(y * map.RowPitch), _frame.Pixels, y * rowBytes, rowBytes);
        }
        finally
        {
            _context.Unmap(_staging, 0);
        }
        _hasFrame = true;
        frame = _frame;
        return true;
    }

    public void Dispose()
    {
        _staging.Dispose();
        _mipView.Dispose();
        _mipTexture.Dispose();
        _duplication.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
