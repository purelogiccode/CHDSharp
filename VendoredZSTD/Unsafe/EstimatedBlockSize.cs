using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct EstimatedBlockSize
    {
        public nuint estLitSize;
        public nuint estBlockSize;
    }
}
