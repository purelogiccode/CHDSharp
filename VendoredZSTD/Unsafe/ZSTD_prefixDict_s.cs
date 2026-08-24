using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdPrefixDictS
{
    public void* dict;
    public nuint dictSize;
    public ZstdDictContentTypeE dictContentType;
}