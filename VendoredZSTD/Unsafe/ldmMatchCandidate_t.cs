using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct LdmMatchCandidateT
{
    public byte* split;
    public uint hash;
    public uint checksum;
    public LdmEntryT* bucket;
}