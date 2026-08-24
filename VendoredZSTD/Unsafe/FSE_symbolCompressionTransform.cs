using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/* *****************************************
 *  Implementation of inlined functions
 *******************************************/
[StructLayout(LayoutKind.Sequential)]
public struct FseSymbolCompressionTransform
{
    public int deltaFindState;
    public uint deltaNbBits;
}