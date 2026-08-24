using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct BlockSummary
{
    public nuint nbSequences;
    public nuint blockSize;
    public nuint litSize;
}