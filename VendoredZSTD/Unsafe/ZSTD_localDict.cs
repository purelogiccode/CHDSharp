using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdLocalDict
{
    public void* dictBuffer;
    public void* dict;
    public nuint dictSize;
    public ZstdDictContentTypeE dictContentType;
    public ZstdCDictS* cdict;
}