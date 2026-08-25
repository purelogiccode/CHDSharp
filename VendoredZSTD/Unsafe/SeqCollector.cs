using System.Runtime.InteropServices;
namespace VendoredZSTD.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SeqCollector
    {
        public int collectSequences;
        public ZSTD_Sequence* seqStart;
        public nuint seqIndex;
        public nuint maxSequences;
    }
}
