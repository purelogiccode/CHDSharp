using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdOptimalT
{
    public int price;
    public uint off;
    public uint mlen;
    public uint litlen;
    public fixed uint rep[3];
}