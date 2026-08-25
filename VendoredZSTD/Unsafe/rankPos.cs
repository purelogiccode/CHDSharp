using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct rankPos
{
    public ushort @base;
    public ushort curr;
}
