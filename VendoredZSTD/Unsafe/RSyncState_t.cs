using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct RSyncStateT
{
    public ulong hash;
    public ulong hitMask;
    public ulong primePower;
}