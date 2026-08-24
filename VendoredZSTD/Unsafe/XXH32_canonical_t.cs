using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/*!
 * @brief Canonical (big endian) representation of @ref XXH32_hash_t.
 */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Xxh32CanonicalT
{
    /*!< Hash bytes, big endian */
    public fixed byte digest[4];
}