using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct CoverMapS
{
    public CoverMapPairTS* data;
    public uint sizeLog;
    public uint size;
    public uint sizeMask;
}