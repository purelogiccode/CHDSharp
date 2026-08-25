using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct rankValCol_t
{
    public fixed uint Body[13];
}