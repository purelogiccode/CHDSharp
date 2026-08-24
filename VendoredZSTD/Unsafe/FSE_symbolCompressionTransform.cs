using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    /* *****************************************
     *  Implementation of inlined functions
     *******************************************/
    [StructLayout(LayoutKind.Sequential)]
    public struct FSE_symbolCompressionTransform
    {
        public int deltaFindState;
        public uint deltaNbBits;
    }
}
