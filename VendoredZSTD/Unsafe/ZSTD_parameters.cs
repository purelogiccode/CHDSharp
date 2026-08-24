using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct ZstdParameters
{
    public ZstdCompressionParameters cParams;
    public ZstdFrameParameters fParams;
}