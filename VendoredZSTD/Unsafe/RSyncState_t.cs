using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct RsyncStateT
{
    public ulong hash;
    public ulong hitMask;
    public ulong primePower;
}