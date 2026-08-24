using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdOptimalT
{
    /* price from beginning of segment to this position */
    public int price;

    /* offset of previous match */
    public uint off;

    /* length of previous match */
    public uint mlen;

    /* nb of literals since previous match */
    public uint litlen;

    /* offset history after previous match */
    public fixed uint rep[3];
}