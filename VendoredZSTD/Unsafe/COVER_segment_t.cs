using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe
{
    /**
     * A segment is a range in the source as well as the score of the segment.
     */
    [StructLayout(LayoutKind.Sequential)]
    public struct COVER_segment_t
    {
        public uint begin;
        public uint end;
        public uint score;
    }
}