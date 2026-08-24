using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct FpStats
{
    public Fingerprint pastEvents;
    public Fingerprint newEvents;
}