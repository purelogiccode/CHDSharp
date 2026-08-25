using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdOutBufferS
{
    /**
     * < start of output buffer
     */
    public void* dst;

    /**
     * < size of output buffer
     */
    public nuint size;

    /**
     * < position where writing stopped. Will be updated. Necessarily
     * 0
     * <
     * =
     * pos
     * <
     * =
     * size
     */
    public nuint pos;
}