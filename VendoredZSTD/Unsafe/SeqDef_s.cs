using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe
{
    /*-*******************************************
     *  Private declarations
     *********************************************/
    [StructLayout(LayoutKind.Sequential)]
    public struct seqDef_s
    {
        /* offBase == Offset + ZSTD_REP_NUM, or repcode 1,2,3 */
        public uint offBase;
        public ushort litLength;

        /* mlBase == matchLength - MINMATCH */
        public ushort mlBase;
    }
}