using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/* **************************************************************
 *  Required declarations
 ****************************************************************/
[StructLayout(LayoutKind.Sequential)]
public struct nodeElt_s
{
    public uint count;
    public ushort parent;
    public byte @byte;
    public byte nbBits;
}