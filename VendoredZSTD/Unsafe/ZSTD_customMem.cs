using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdCustomMem
{
    public void* customAlloc;
    public void* customFree;
    public void* opaque;
    public ZstdCustomMem(void* customAlloc, void* customFree, void* opaque)
    {
        this.customAlloc = customAlloc;
        this.customFree = customFree;
        this.opaque = opaque;
    }
}