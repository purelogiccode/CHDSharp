using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/* *************************/
/* double-symbols decoding */
/* *************************/
[StructLayout(LayoutKind.Sequential)]
public struct HUF_DEltX2
{
    /* double-symbols decoding */
    public ushort sequence;
    public byte nbBits;
    public byte length;
}