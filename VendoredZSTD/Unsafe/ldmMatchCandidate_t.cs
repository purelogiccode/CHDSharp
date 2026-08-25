using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ldmMatchCandidate_t
    {
        public byte* split;
        public uint hash;
        public uint checksum;
        public ldmEntry_t* bucket;
    }
}