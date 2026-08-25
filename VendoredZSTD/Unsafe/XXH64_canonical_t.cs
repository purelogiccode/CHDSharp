using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/*******   Canonical representation   *******/
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Xxh64CanonicalT
{
    public fixed byte digest[8];
}