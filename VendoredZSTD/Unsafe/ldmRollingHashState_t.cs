using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ldmRollingHashState_t
    {
        public ulong rolling;
        public ulong stopMask;
    }
}
