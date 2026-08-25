using System.Runtime.InteropServices;
namespace VendoredZSTD.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct dictItem
    {
        public uint pos;
        public uint length;
        public uint savings;
    }
}
