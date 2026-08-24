using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct Fingerprint
{
    public fixed uint events[1024];
    public nuint nbEvents;
}