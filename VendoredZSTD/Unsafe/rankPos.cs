using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct rankPos
    {
        public ushort @base;
        public ushort curr;
    }
}
