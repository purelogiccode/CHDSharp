using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/*-***************************/
/*  single-symbol decoding   */
/*-***************************/
[StructLayout(LayoutKind.Sequential)]
public struct HUF_DEltX1
{
    /* single-symbol decoding */
    public byte nbBits;

    /* single-symbol decoding */
    public byte @byte;
}
