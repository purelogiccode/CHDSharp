using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/* ======    Decompression    ====== */
[StructLayout(LayoutKind.Sequential)]
public struct FseDTableHeader
{
    public ushort tableLog;
    public ushort fastMode;
}