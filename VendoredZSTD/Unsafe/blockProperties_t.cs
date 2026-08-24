using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct blockProperties_t
    {
        public blockType_e blockType;
        public uint lastBlock;
        public uint origSize;
    }
}
