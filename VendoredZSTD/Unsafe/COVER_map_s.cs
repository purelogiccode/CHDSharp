using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct COVER_map_s
{
    public COVER_map_pair_t_s* data;
    public uint sizeLog;
    public uint size;
    public uint sizeMask;
}