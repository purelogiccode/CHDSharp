using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    /* ====   Serial State   ==== */
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct Range
    {
        public void* start;
        public nuint size;
        public Range(void* start, nuint size)
        {
            this.start = start;
            this.size = size;
        }
    }
}
