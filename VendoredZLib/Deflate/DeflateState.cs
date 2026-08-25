#nullable disable
// Original code and comments Copyright (C) 1995-2024 Jean-loup Gailly
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

using static VendoredZLib.Deflate.Constants;

namespace VendoredZLib.Deflate;

/// <summary>
///     State maintained between <see cref="ZLib.Deflate(ref ZStream, int)" /> calls.
/// </summary>
internal sealed class DeflateState
{
    private const byte MaxBlBits = 7; // Bit length codes must not exceed MAX_BL_BITS bits

    private static readonly StaticTree SlDesc = new(Tree.SLtree, Literals + 1, LCodes, MaxBits);

    private static readonly StaticTree SdDesc = new(Tree.SDtree, 0, DCodes, MaxBits);

    private static readonly StaticTree SBlDesc = new(null, 0, BlCodes, MaxBlBits);

    internal readonly ushort[]
        BlCount = new ushort[MaxBits + 1]; // number of codes at each bit length for an optimal tree

    internal readonly TreeDescriptor BlDesc; // desc. for bit length tree
    internal readonly TreeNode[] BlTree = new TreeNode[2 * BlCodes + 1]; // Huffman tree for bit lengths
    internal readonly TreeDescriptor DDesc; // desc. for distance tree

    internal readonly byte[]
        Depth = new byte[2 * LCodes + 1]; // Depth of each subtree used as tie breaker for trees of equal frequency

    internal readonly TreeNode[] DynDtree = new TreeNode[2 * DCodes + 1]; // distance tree

    internal readonly TreeNode[] DynLtree = new TreeNode[HeapSize]; // literal and length tree

    internal readonly int[] Heap = new int[2 * LCodes + 1]; // heap used to build the Huffman trees

    internal readonly TreeDescriptor LDesc; // desc. for literal tree

    internal ushort BiBuf; //Output buffer. bits are inserted starting at the bottom (least significant bits).

    internal int BiValid; //Number of valid bits in bi_buf. All bits above the last valid bit are always zero.
    /* Number of bits by which ins_h must be shifted at each input
     * step. It must be such that after MIN_MATCH steps, the oldest
     * byte no longer takes part in the hash key, that is:
     *   hash_shift * MIN_MATCH >= hash_bits
     */

    internal int
        BlockStart; // Window position at the beginning of the current output block. Gets negative when the window is moved backwards.

    internal uint GoodMatch; // Use a faster search when the previous match is longer than this
    internal uint HashBits; // log2(hash_size)
    internal uint HashMask; // hash_size-1

    internal int HashShift;

    internal uint HashSize; // number of elements in hash table
    /* Link to older string with same hash index. To limit the size of this
     * array to 64K, this link is maintained only for the last 32K strings.
     * An index in this array is thus a window index modulo 32K.
     */

    internal ushort[] Head; // Heads of the hash chains or null.
    internal uint HeapLen; // number of elements in the heap
    internal uint HeapMax; // element of largest frequency

    internal uint HighWater;

    internal uint InsH; // hash index of string to be inserted
    internal uint Insert; // bytes at end of window left to insert

    internal int LastFlush; // value of flush param for previous deflate call
    /* Attempt to find a better match only when the current match is strictly
     * smaller than this value. This mechanism is used only for compression
     * levels >= 4.
     */

    internal int Level; // compression level (1..9)

    internal uint LitBufsize;
    internal uint Lookahead; // number of valid bytes ahead in window
    internal bool MatchAvailable; // set if previous match exists

    internal uint MatchLength; // length of best match
    internal uint MatchStart; // start of matching string

    internal uint Matches; // number of string matches in current block
    /* Length of the best match at previous step. Matches not greater than this
     * are discarded. This is used in the lazy match evaluation.
     */

    internal uint MaxChainLength;
    /* To speed up deflation, hash chains are never searched beyond this
     * length.  A higher limit improves compression ratio but degrades the
     * speed.
     */

    internal uint MaxLazyMatch;

    internal byte Method; // can only be DEFLATED

    internal int NiceMatch; // Stop searching when current match exceeds this

    internal uint OptLen; // bit length of current block with optimal trees

    internal uint Pending; // nb of bytes in the pending buffer
    internal byte[] PendingBuf; // output still pending

    internal uint PendingBufSize; // size of pending_buf

    internal byte[] PendingOut; // next pending byte to output to the stream

    internal uint PendingOutOffset;

    internal ushort[] Prev;

    internal uint PrevLength;
    internal uint PrevMatch; // previous match
    internal uint StaticLen; // bit length of current block with static trees

    internal int Status; // as the name implies
    internal int Strategy; // favor or force Huffman coding
    internal uint Strstart; // start of string to insert

    internal uint SymEnd; // symbol table full when sym_next reaches this
    /* Size of match buffer for literals/lengths.  There are 4 reasons for
     * limiting lit_bufsize to 64K:
     *   - frequencies can be kept in 16 bit counters
     *   - if compression is not successful for the first block, all input
     *     data is still in the window so we can still emit a stored block even
     *     when input comes from standard input.  (This can also be done for
     *     all blocks if lit_bufsize is not greater than 32K.)
     *   - if compression is not successful for a file smaller than 64K, we can
     *     even emit a stored file instead of a stored block (saving 5 bytes).
     *     This is applicable only for zip (not gzip or zlib).
     *   - creating new Huffman trees less frequently may not provide fast
     *     adaptation to changes in the input data statistics. (Take for
     *     example a binary file with poorly compressible code followed by
     *     a highly compressible string table.) Smaller buffer sizes give
     *     fast adaptation but have of course the overhead of transmitting
     *     trees more frequently.
     *   - I can't count above 4
     */

    internal uint SymNext; // running index in sym_buf
    internal uint WBits; // log2(w_size)  (8..16)
    internal uint WMask; // w_size - 1

    internal uint WSize; // LZ77 window size (32K by default)

    internal byte[] Window;
    /* Sliding window. Input bytes are read into the second half of the window,
     * and move to the first half later to keep a dictionary of at least wSize
     * bytes. With this organization, matches are limited to a distance of
     * wSize-MAX_MATCH bytes, but this ensures that IO is always
     * performed with a length multiple of the block size. Also, it limits
     * the window size to 64K, which is quite useful on MSDOS.
     * To do: use the user input buffer as sliding window.
     */

    internal uint
        WindowSize; // Actual size of window: 2*wSize, except when the user input buffer is directly used as sliding window.

    internal int Wrap; // bit 0 true for zlib, bit 1 true for gzip

    /// <summary>
    ///     Creates an instance of the <see cref="DeflateState" /> class.
    /// </summary>
    public DeflateState()
    {
        LDesc = new TreeDescriptor(DynLtree, SlDesc);
        DDesc = new TreeDescriptor(DynDtree, SdDesc);
        BlDesc = new TreeDescriptor(BlTree, SBlDesc);
    }

#if DEBUG
    internal uint CompressedLen; // total bit length of compressed file mod 2^32
    internal uint BitsSent; // bit length of compressed data sent mod 2^32
#endif
    /* High water mark offset in window for initialized bytes -- bytes above
     * this are set to zero in order to avoid memory check warnings when
     * longest match routines access bytes past the input.  This is then
     * updated to the new high water mark.
     */
}