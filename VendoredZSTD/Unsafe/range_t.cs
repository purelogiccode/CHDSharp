using System.Runtime.InteropServices;
namespace VendoredZSTD.Unsafe
{
    /* ====   Serial State   ==== */
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct range_t
    {
        public void* start;
        public nuint size;
        public range_t(void* start, nuint size)
        {
            this.start = start;
            this.size = size;
        }
    }
}
