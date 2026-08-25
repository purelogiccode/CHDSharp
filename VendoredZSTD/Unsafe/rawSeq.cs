using System.Runtime.InteropServices;
namespace VendoredZSTD.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct rawSeq
    {
        /* Offset of sequence */
        public uint offset;
        /* Length of literals prior to match */
        public uint litLength;
        /* Raw length of match */
        public uint matchLength;
    }
}
