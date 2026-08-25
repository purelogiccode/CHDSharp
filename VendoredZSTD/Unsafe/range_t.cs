using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/* ====   Serial State   ==== */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct RangeT
{
    public void* start;
    public nuint size;

    public RangeT(void* start, nuint size)
    {
        this.start = start;
        this.size = size;
    }
}