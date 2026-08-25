using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ZDICT_legacy_params_t
    {
        /* 0 means default; larger => select more => larger dictionary */
        public uint selectivityLevel;
        public ZDICT_params_t zParams;
    }
}