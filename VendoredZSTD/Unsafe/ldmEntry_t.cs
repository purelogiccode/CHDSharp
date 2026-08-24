using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct LdmEntryT
{
    public uint offset;
    public uint checksum;
}