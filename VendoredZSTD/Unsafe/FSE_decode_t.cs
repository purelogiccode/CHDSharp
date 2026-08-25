using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct FseDecodeT
{
    public ushort newState;
    public byte symbol;
    public byte nbBits;
}