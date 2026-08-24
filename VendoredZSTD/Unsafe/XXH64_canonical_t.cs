using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/*!
 * @brief Canonical (big endian) representation of @ref XXH64_hash_t.
 */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Xxh64CanonicalT
{
    public fixed byte digest[8];
}