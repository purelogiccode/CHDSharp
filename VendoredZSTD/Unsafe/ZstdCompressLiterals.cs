using static VendoredZSTD.UnsafeHelper;

namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
    /* **************************************************************
     *  Literals compression - special cases
     ****************************************************************/
    private static nuint ZSTD_noCompressLiterals(void* dst, nuint dstCapacity, void* src, nuint srcSize)
    {
        var ostart = (byte*)dst;
        var flSize = (uint)(1 + (srcSize > 31 ? 1 : 0) + (srcSize > 4095 ? 1 : 0));
        if (srcSize + flSize > dstCapacity)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));
        }

        switch (flSize)
        {
            case 1:
                ostart[0] = (byte)((uint)SymbolEncodingTypeE.SetBasic + (srcSize << 3));
                break;
            case 2:
                MEM_writeLE16(ostart, (ushort)((uint)SymbolEncodingTypeE.SetBasic + (1 << 2) + (srcSize << 4)));
                break;
            case 3:
                MEM_writeLE32(ostart, (uint)((uint)SymbolEncodingTypeE.SetBasic + (3 << 2) + (srcSize << 4)));
                break;
            default:
                assert(0 != 0);
                break;
        }

        memcpy(ostart + flSize, src, (uint)srcSize);
        return srcSize + flSize;
    }

    private static int AllBytesIdentical(void* src, nuint srcSize)
    {
        assert(srcSize >= 1);
        assert(src != null);
        {
            var b = ((byte*)src)[0];
            nuint p;
            for (p = 1; p < srcSize; p++)
            {
                if (((byte*)src)[p] != b)
                    return 0;
            }

            return 1;
        }
    }

    /* ZSTD_compressRleLiteralsBlock() :
     * Conditions :
     * - All bytes in @src are identical
     * - dstCapacity >= 4 */
    private static nuint ZSTD_compressRleLiteralsBlock(void* dst, nuint dstCapacity, void* src, nuint srcSize)
    {
        var ostart = (byte*)dst;
        var flSize = (uint)(1 + (srcSize > 31 ? 1 : 0) + (srcSize > 4095 ? 1 : 0));
        assert(dstCapacity >= 4);
        assert(AllBytesIdentical(src, srcSize) != 0);
        switch (flSize)
        {
            case 1:
                ostart[0] = (byte)((uint)SymbolEncodingTypeE.SetRle + (srcSize << 3));
                break;
            case 2:
                MEM_writeLE16(ostart, (ushort)((uint)SymbolEncodingTypeE.SetRle + (1 << 2) + (srcSize << 4)));
                break;
            case 3:
                MEM_writeLE32(ostart, (uint)((uint)SymbolEncodingTypeE.SetRle + (3 << 2) + (srcSize << 4)));
                break;
            default:
                assert(0 != 0);
                break;
        }

        ostart[flSize] = *(byte*)src;
        return flSize + 1;
    }

    /* ZSTD_minLiteralsToCompress() :
     * returns minimal amount of literals
     * for literal compression to even be attempted.
     * Minimum is made tighter as compression strategy increases.
     */
    private static nuint ZSTD_minLiteralsToCompress(ZstdStrategy strategy, HufRepeat hufRepeat)
    {
        assert((int)strategy >= 0);
        assert((int)strategy <= 9);
        {
            var shift = 9 - (int)strategy < 3 ? 9 - (int)strategy : 3;
            var mintc = hufRepeat == HufRepeat.HufRepeatValid ? 6 : (nuint)8 << shift;
            return mintc;
        }
    }

    /* ZSTD_compressLiterals():
     * @entropyWorkspace: must be aligned on 4-bytes boundaries
     * @entropyWorkspaceSize : must be >= HUF_WORKSPACE_SIZE
     * @suspectUncompressible: sampling checks, to potentially skip huffman coding
     */
    private static nuint ZSTD_compressLiterals(void* dst, nuint dstCapacity, void* src, nuint srcSize, void* entropyWorkspace, nuint entropyWorkspaceSize, ZstdHufCTablesT* prevHuf, ZstdHufCTablesT* nextHuf, ZstdStrategy strategy, int disableLiteralCompression, int suspectUncompressible, int bmi2)
    {
        var lhSize = (nuint)(3 + (srcSize >= 1 * (1 << 10) ? 1 : 0) + (srcSize >= 16 * (1 << 10) ? 1 : 0));
        var ostart = (byte*)dst;
        var singleStream = srcSize < 256 ? 1U : 0U;
        var hType = SymbolEncodingTypeE.SetCompressed;
        nuint cLitSize;
        memcpy(nextHuf, prevHuf, (uint)sizeof(ZstdHufCTablesT));
        if (disableLiteralCompression != 0)
            return ZSTD_noCompressLiterals(dst, dstCapacity, src, srcSize);
        if (srcSize < ZSTD_minLiteralsToCompress(strategy, prevHuf->repeatMode))
            return ZSTD_noCompressLiterals(dst, dstCapacity, src, srcSize);

        if (dstCapacity < lhSize + 1)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));
        }

        {
            var repeat = prevHuf->repeatMode;
            var flags = 0 | (bmi2 != 0 ? (int)HufFlagsE.HufFlagsBmi2 : 0) | (strategy < ZstdStrategy.ZstdLazy && srcSize <= 1024 ? (int)HufFlagsE.HufFlagsPreferRepeat : 0) | (strategy >= ZstdStrategy.ZstdBtultra ? (int)HufFlagsE.HufFlagsOptimalDepth : 0) | (suspectUncompressible != 0 ? (int)HufFlagsE.HufFlagsSuspectUncompressible : 0);
            if (repeat == HufRepeat.HufRepeatValid && lhSize == 3)
            {
                singleStream = 1;
            }

            void* hufCompress = singleStream != 0 ? (delegate* managed<void*, nuint, void*, nuint, uint, uint, void*, nuint, nuint*, HufRepeat*, int, nuint>)(&HUF_compress1X_repeat) : (delegate* managed<void*, nuint, void*, nuint, uint, uint, void*, nuint, nuint*, HufRepeat*, int, nuint>)(&HUF_compress4X_repeat);
            cLitSize = ((delegate* managed<void*, nuint, void*, nuint, uint, uint, void*, nuint, nuint*, HufRepeat*, int, nuint>)hufCompress)(ostart + lhSize, dstCapacity - lhSize, src, srcSize, 255, 11, entropyWorkspace, entropyWorkspaceSize, &nextHuf->CTable.e0, &repeat, flags);
            if (repeat != HufRepeat.HufRepeatNone)
            {
                hType = SymbolEncodingTypeE.SetRepeat;
            }
        }

        {
            var minGain = ZSTD_minGain(srcSize, strategy);
            if (cLitSize == 0 || cLitSize >= srcSize - minGain || ERR_isError(cLitSize))
            {
                memcpy(nextHuf, prevHuf, (uint)sizeof(ZstdHufCTablesT));
                return ZSTD_noCompressLiterals(dst, dstCapacity, src, srcSize);
            }
        }

        if (cLitSize == 1)
        {
            if (srcSize >= 8 || AllBytesIdentical(src, srcSize) != 0)
            {
                memcpy(nextHuf, prevHuf, (uint)sizeof(ZstdHufCTablesT));
                return ZSTD_compressRleLiteralsBlock(dst, dstCapacity, src, srcSize);
            }
        }

        if (hType == SymbolEncodingTypeE.SetCompressed)
        {
            nextHuf->repeatMode = HufRepeat.HufRepeatCheck;
        }

        switch (lhSize)
        {
            case 3:
#if DEBUG
                if (singleStream == 0)
                    assert(srcSize >= 6);
#endif
            {
                var lhc = (uint)hType + ((singleStream == 0 ? 1U : 0U) << 2) + ((uint)srcSize << 4) + ((uint)cLitSize << 14);
                MEM_writeLE24(ostart, lhc);
                break;
            }

            case 4:
                assert(srcSize >= 6);
            {
                var lhc = (uint)(hType + (2 << 2)) + ((uint)srcSize << 4) + ((uint)cLitSize << 18);
                MEM_writeLE32(ostart, lhc);
                break;
            }

            case 5:
                assert(srcSize >= 6);
            {
                var lhc = (uint)(hType + (3 << 2)) + ((uint)srcSize << 4) + ((uint)cLitSize << 22);
                MEM_writeLE32(ostart, lhc);
                ostart[4] = (byte)(cLitSize >> 10);
                break;
            }

            default:
                assert(0 != 0);
                break;
        }

        return lhSize + cLitSize;
    }
}