using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdBuildCTableWksp
{
    public fixed short norm[53];
    public fixed uint wksp[285];
}