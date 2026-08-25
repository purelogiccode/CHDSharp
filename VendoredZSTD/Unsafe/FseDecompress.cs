using System.Runtime.CompilerServices;
using static VendoredZSTD.UnsafeHelper;

namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
    private static nuint FSE_buildDTable_internal(
        uint* dt,
        short* normalizedCounter,
        uint maxSymbolValue,
        uint tableLog,
        void* workSpace,
        nuint wkspSize
    )
    {
        /* because *dt is unsigned, 32-bits aligned on 32-bits */
        void* tdPtr = dt + 1;
        var tableDecode = (FseDecodeT*)tdPtr;
        var symbolNext = (ushort*)workSpace;
        var spread = (byte*)(symbolNext + maxSymbolValue + 1);
        var maxSv1 = maxSymbolValue + 1;
        var tableSize = (uint)(1 << (int)tableLog);
        var highThreshold = tableSize - 1;
        if (sizeof(short) * (maxSymbolValue + 1) + (1UL << (int)tableLog) + 8 > wkspSize)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMaxSymbolValueTooLarge));
        if (maxSymbolValue > 255)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMaxSymbolValueTooLarge));
        if (tableLog > 14 - 2)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorTableLogTooLarge));
        {
            FseDTableHeader dTableH;
            dTableH.tableLog = (ushort)tableLog;
            dTableH.fastMode = 1;
            {
                var largeLimit = (short)(1 << (int)(tableLog - 1));
                uint s;
                for (s = 0; s < maxSv1; s++)
                    if (normalizedCounter[s] == -1)
                    {
                        tableDecode[highThreshold--].symbol = (byte)s;
                        symbolNext[s] = 1;
                    }
                    else
                    {
                        if (normalizedCounter[s] >= largeLimit)
                            dTableH.fastMode = 0;
                        symbolNext[s] = (ushort)normalizedCounter[s];
                    }
            }

            memcpy(dt, &dTableH, (uint)sizeof(FseDTableHeader));
        }

        if (highThreshold == tableSize - 1)
        {
            nuint tableMask = tableSize - 1;
            nuint step = (tableSize >> 1) + (tableSize >> 3) + 3;
            {
                const ulong add = 0x0101010101010101UL;
                nuint pos = 0;
                ulong sv = 0;
                uint s;
                for (s = 0; s < maxSv1; ++s, sv += add)
                {
                    int i;
                    int n = normalizedCounter[s];
                    MEM_write64(spread + pos, sv);
                    for (i = 8; i < n; i += 8)
                        MEM_write64(spread + pos + i, sv);

                    pos += (nuint)n;
                }
            }

            {
                nuint position = 0;
                nuint s;
                const nuint unroll = 2;
                assert(tableSize % unroll == 0);
                for (s = 0; s < tableSize; s += unroll)
                {
                    nuint u;
                    for (u = 0; u < unroll; ++u)
                    {
                        var uPosition = (position + u * step) & tableMask;
                        tableDecode[uPosition].symbol = spread[s + u];
                    }

                    position = (position + unroll * step) & tableMask;
                }

                assert(position == 0);
            }
        }
        else
        {
            var tableMask = tableSize - 1;
            var step = (tableSize >> 1) + (tableSize >> 3) + 3;
            uint s,
                position = 0;
            for (s = 0; s < maxSv1; s++)
            {
                int i;
                for (i = 0; i < normalizedCounter[s]; i++)
                {
                    tableDecode[position].symbol = (byte)s;
                    position = (position + step) & tableMask;
                    while (position > highThreshold)
                        position = (position + step) & tableMask;
                }
            }

            if (position != 0)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorGeneric));
        }

        {
            uint u;
            for (u = 0; u < tableSize; u++)
            {
                var symbol = tableDecode[u].symbol;
                uint nextState = symbolNext[symbol]++;
                tableDecode[u].nbBits = (byte)(tableLog - ZSTD_highbit32(nextState));
                tableDecode[u].newState = (ushort)(
                    (nextState << tableDecode[u].nbBits) - tableSize
                );
            }
        }

        return 0;
    }

    private static nuint FSE_buildDTable_wksp(
        uint* dt,
        short* normalizedCounter,
        uint maxSymbolValue,
        uint tableLog,
        void* workSpace,
        nuint wkspSize
    )
    {
        return FSE_buildDTable_internal(
            dt,
            normalizedCounter,
            maxSymbolValue,
            tableLog,
            workSpace,
            wkspSize
        );
    }

    /*-*******************************************************
     *  Decompression (Byte symbols)
     *********************************************************/
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint FSE_decompress_usingDTable_generic(
        void* dst,
        nuint maxDstSize,
        void* cSrc,
        nuint cSrcSize,
        uint* dt,
        uint fast
    )
    {
        var ostart = (byte*)dst;
        var op = ostart;
        var omax = op + maxDstSize;
        var olimit = omax - 3;
        BitDStreamT bitD;
        FseDStateT state1;
        FseDStateT state2;
        {
            var varErr = BIT_initDStream(&bitD, cSrc, cSrcSize);
            if (ERR_isError(varErr))
                return varErr;
        }

        FSE_initDState(&state1, &bitD, dt);
        FSE_initDState(&state2, &bitD, dt);
        for (
            ;
            BIT_reloadDStream(&bitD) == BitDStreamStatus.BitDStreamUnfinished && op < olimit;
            op += 4
        )
        {
            op[0] =
                fast != 0 ? FSE_decodeSymbolFast(&state1, &bitD) : FSE_decodeSymbol(&state1, &bitD);
            if ((14 - 2) * 2 + 7 > sizeof(nuint) * 8)
                BIT_reloadDStream(&bitD);
            op[1] =
                fast != 0 ? FSE_decodeSymbolFast(&state2, &bitD) : FSE_decodeSymbol(&state2, &bitD);
            if ((14 - 2) * 4 + 7 > sizeof(nuint) * 8)
                if (BIT_reloadDStream(&bitD) > BitDStreamStatus.BitDStreamUnfinished)
                {
                    op += 2;
                    break;
                }

            op[2] =
                fast != 0 ? FSE_decodeSymbolFast(&state1, &bitD) : FSE_decodeSymbol(&state1, &bitD);
            if ((14 - 2) * 2 + 7 > sizeof(nuint) * 8)
                BIT_reloadDStream(&bitD);
            op[3] =
                fast != 0 ? FSE_decodeSymbolFast(&state2, &bitD) : FSE_decodeSymbol(&state2, &bitD);
        }

        while (true)
        {
            if (op > omax - 2)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));
            *op++ =
                fast != 0 ? FSE_decodeSymbolFast(&state1, &bitD) : FSE_decodeSymbol(&state1, &bitD);
            if (BIT_reloadDStream(&bitD) == BitDStreamStatus.BitDStreamOverflow)
            {
                *op++ =
                    fast != 0
                        ? FSE_decodeSymbolFast(&state2, &bitD)
                        : FSE_decodeSymbol(&state2, &bitD);
                break;
            }

            if (op > omax - 2)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));
            *op++ =
                fast != 0 ? FSE_decodeSymbolFast(&state2, &bitD) : FSE_decodeSymbol(&state2, &bitD);
            if (BIT_reloadDStream(&bitD) == BitDStreamStatus.BitDStreamOverflow)
            {
                *op++ =
                    fast != 0
                        ? FSE_decodeSymbolFast(&state1, &bitD)
                        : FSE_decodeSymbol(&state1, &bitD);
                break;
            }
        }

        return (nuint)(op - ostart);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint FSE_decompress_wksp_body(
        void* dst,
        nuint dstCapacity,
        void* cSrc,
        nuint cSrcSize,
        uint maxLog,
        void* workSpace,
        nuint wkspSize,
        int bmi2
    )
    {
        var istart = (byte*)cSrc;
        var ip = istart;
        uint tableLog;
        uint maxSymbolValue = 255;
        var wksp = (FseDecompressWksp*)workSpace;
        if (wkspSize < (nuint)sizeof(FseDecompressWksp))
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorGeneric));
        {
            var nCountLength = FSE_readNCount_bmi2(
                wksp->ncount,
                &maxSymbolValue,
                &tableLog,
                istart,
                cSrcSize,
                bmi2
            );
            if (ERR_isError(nCountLength))
                return nCountLength;
            if (tableLog > maxLog)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorTableLogTooLarge));
            assert(nCountLength <= cSrcSize);
            ip += nCountLength;
            cSrcSize -= nCountLength;
        }

        if (
            (
                (ulong)(1 + (1 << (int)tableLog) + 1)
                + (
                    sizeof(short) * (maxSymbolValue + 1)
                    + (1UL << (int)tableLog)
                    + 8
                    + sizeof(uint)
                    - 1
                ) / sizeof(uint)
                + (255 + 1) / 2
                + 1
            ) * sizeof(uint)
            > wkspSize
        )
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorTableLogTooLarge));
        assert(
            (nuint)(sizeof(FseDecompressWksp) + (1 + (1 << (int)tableLog)) * sizeof(uint))
            <= wkspSize
        );
        workSpace =
            (byte*)workSpace
            + sizeof(FseDecompressWksp)
            + (1 + (1 << (int)tableLog)) * sizeof(uint);
        wkspSize -= (nuint)(sizeof(FseDecompressWksp) + (1 + (1 << (int)tableLog)) * sizeof(uint));
        {
            var varErr = FSE_buildDTable_internal(
                wksp->dtable,
                wksp->ncount,
                maxSymbolValue,
                tableLog,
                workSpace,
                wkspSize
            );
            if (ERR_isError(varErr))
                return varErr;
        }

        {
            void* ptr = wksp->dtable;
            var dTableH = (FseDTableHeader*)ptr;
            uint fastMode = dTableH->fastMode;
            if (fastMode != 0)
                return FSE_decompress_usingDTable_generic(
                    dst,
                    dstCapacity,
                    ip,
                    cSrcSize,
                    wksp->dtable,
                    1
                );
            return FSE_decompress_usingDTable_generic(
                dst,
                dstCapacity,
                ip,
                cSrcSize,
                wksp->dtable,
                0
            );
        }
    }

    /* Avoids the FORCE_INLINE of the _body() function. */
    private static nuint FSE_decompress_wksp_body_default(
        void* dst,
        nuint dstCapacity,
        void* cSrc,
        nuint cSrcSize,
        uint maxLog,
        void* workSpace,
        nuint wkspSize
    )
    {
        return FSE_decompress_wksp_body(
            dst,
            dstCapacity,
            cSrc,
            cSrcSize,
            maxLog,
            workSpace,
            wkspSize,
            0
        );
    }

    private static nuint FSE_decompress_wksp_bmi2(
        void* dst,
        nuint dstCapacity,
        void* cSrc,
        nuint cSrcSize,
        uint maxLog,
        void* workSpace,
        nuint wkspSize,
        int bmi2
    )
    {
        return FSE_decompress_wksp_body_default(
            dst,
            dstCapacity,
            cSrc,
            cSrcSize,
            maxLog,
            workSpace,
            wkspSize
        );
    }
}