using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct EstimatedBlockSize
{
    public nuint estLitSize;
    public nuint estBlockSize;
}