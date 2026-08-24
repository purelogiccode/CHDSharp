using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct LdmRollingHashStateT
{
    public ulong rolling;
    public ulong stopMask;
}