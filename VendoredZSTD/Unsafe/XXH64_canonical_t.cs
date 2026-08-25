using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe
{
    /*******   Canonical representation   *******/
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XXH64_canonical_t
    {
        public fixed byte digest[8];
    }
}
