using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/***********************************************
 *  Sequences *
 ***********************************************/
[StructLayout(LayoutKind.Sequential)]
public struct SeqDefS
{
    /* offBase == Offset + ZSTD_REP_NUM, or repcode 1,2,3 */
    public uint offBase;

    public ushort litLength;

    /* mlBase == matchLength - MINMATCH */
    public ushort mlBase;
}