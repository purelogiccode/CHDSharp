using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ZSTD_SequenceLength
    {
        public uint litLength;
        public uint matchLength;
    }
}
