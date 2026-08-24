using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/* =====   Buffer Pool   ===== */
/* a single Buffer Pool can be invoked from multiple threads in parallel */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct BufferS
{
    public void* start;
    public nuint capacity;
    public BufferS(void* start, nuint capacity)
    {
        this.start = start;
        this.capacity = capacity;
    }
}