using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ZSTD_fseState
    {
        public nuint state;
        public ZSTD_seqSymbol* table;
    }
}
