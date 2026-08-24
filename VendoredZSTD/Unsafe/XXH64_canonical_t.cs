using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    /*!
     * @brief Canonical (big endian) representation of @ref XXH64_hash_t.
     */
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XXH64_canonical_t
    {
        public fixed byte digest[8];
    }
}
