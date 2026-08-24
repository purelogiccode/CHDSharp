using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/*-********************************************
 *  bitStream decoding API (read backward)
 **********************************************/
[StructLayout(LayoutKind.Sequential)]
public unsafe struct BitDStreamT
{
    public nuint bitContainer;
    public uint bitsConsumed;
    public sbyte* ptr;
    public sbyte* start;
    public sbyte* limitPtr;
}