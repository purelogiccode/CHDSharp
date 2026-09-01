using System.Runtime.CompilerServices;
using InlineMethod;
using static VendoredZSTD.UnsafeHelper;

namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
    private static readonly AlgoTimeT[][] AlgoTime = new AlgoTimeT[16][]
    {
        new AlgoTimeT[2] { new(0, 0), new(1, 1) },
        new AlgoTimeT[2] { new(0, 0), new(1, 1) },
        new AlgoTimeT[2] { new(150, 216), new(381, 119) },
        new AlgoTimeT[2] { new(170, 205), new(514, 112) },
        new AlgoTimeT[2] { new(177, 199), new(539, 110) },
        new AlgoTimeT[2] { new(197, 194), new(644, 107) },
        new AlgoTimeT[2] { new(221, 192), new(735, 107) },
        new AlgoTimeT[2] { new(256, 189), new(881, 106) },
        new AlgoTimeT[2] { new(359, 188), new(1167, 109) },
        new AlgoTimeT[2] { new(582, 187), new(1570, 114) },
        new AlgoTimeT[2] { new(688, 187), new(1712, 122) },
        new AlgoTimeT[2] { new(825, 186), new(1965, 136) },
        new AlgoTimeT[2] { new(976, 185), new(2131, 150) },
        new AlgoTimeT[2] { new(1180, 186), new(2070, 175) },
        new AlgoTimeT[2] { new(1377, 185), new(1731, 202) },
        new AlgoTimeT[2] { new(1412, 185), new(1695, 202) }
    };

    private static DTableDesc HUF_getDTableDesc(uint* table)
    {
        DTableDesc dtd;
        memcpy(&dtd, table, (uint)sizeof(DTableDesc));
        return dtd;
    }

    private static nuint HUF_initFastDStream(byte* ip)
    {
        var lastByte = ip[7];
        nuint bitsConsumed = lastByte != 0 ? 8 - ZSTD_highbit32(lastByte) : 0;
        var value = MEM_readLEST(ip) | 1;
        assert(bitsConsumed <= 8);
        assert(sizeof(nuint) == 8);
        return value << (int)bitsConsumed;
    }

    /*
     * Initializes args for the fast decoding loop.
     * @returns 1 on success
     * 0 if the fallback implementation should be used.
     * Or an error code on failure.
     */
    private static nuint HUF_DecompressFastArgs_init(
        HufDecompressFastArgs* args,
        void* dst,
        nuint dstSize,
        void* src,
        nuint srcSize,
        uint* dTable
    )
    {
        void* dt = dTable + 1;
        uint dtLog = HUF_getDTableDesc(dTable).tableLog;
        var ilimit = (byte*)src + 6 + 8;
        var oend = (byte*)dst + dstSize;
        if (!BitConverter.IsLittleEndian || MEM_32bits)
            return 0;
        if (srcSize < 10)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
        if (dtLog != 11)
            return 0;
        {
            var istart = (byte*)src;
            nuint length1 = MEM_readLE16(istart);
            nuint length2 = MEM_readLE16(istart + 2);
            nuint length3 = MEM_readLE16(istart + 4);
            var length4 = srcSize - (length1 + length2 + length3 + 6);
            args->iend.e0 = istart + 6;
            args->iend.e1 = args->iend.e0 + length1;
            args->iend.e2 = args->iend.e1 + length2;
            args->iend.e3 = args->iend.e2 + length3;
            if (length1 < 16 || length2 < 8 || length3 < 8 || length4 < 8)
                return 0;
            if (length4 > srcSize)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
        }

        args->ip.e0 = args->iend.e1 - sizeof(ulong);
        args->ip.e1 = args->iend.e2 - sizeof(ulong);
        args->ip.e2 = args->iend.e3 - sizeof(ulong);
        args->ip.e3 = (byte*)src + srcSize - sizeof(ulong);
        args->op.e0 = (byte*)dst;
        args->op.e1 = args->op.e0 + (dstSize + 3) / 4;
        args->op.e2 = args->op.e1 + (dstSize + 3) / 4;
        args->op.e3 = args->op.e2 + (dstSize + 3) / 4;
        if (args->op.e3 >= oend)
            return 0;
        args->bits[0] = HUF_initFastDStream(args->ip.e0);
        args->bits[1] = HUF_initFastDStream(args->ip.e1);
        args->bits[2] = HUF_initFastDStream(args->ip.e2);
        args->bits[3] = HUF_initFastDStream(args->ip.e3);
        args->ilimit = ilimit;
        args->oend = oend;
        args->dt = dt;
        return 1;
    }

    private static nuint HUF_initRemainingDStream(
        BitDStreamT* bit,
        HufDecompressFastArgs* args,
        int stream,
        byte* segmentEnd
    )
    {
        if ((&args->op.e0)[stream] > segmentEnd)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
        if ((&args->ip.e0)[stream] < (&args->iend.e0)[stream] - 8)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
        assert(sizeof(nuint) == 8);
        bit->bitContainer = MEM_readLEST((&args->ip.e0)[stream]);
        bit->bitsConsumed = ZSTD_countTrailingZeros64(args->bits[stream]);
        bit->start = (sbyte*)args->iend.e0;
        bit->limitPtr = bit->start + sizeof(nuint);
        bit->ptr = (sbyte*)(&args->ip.e0)[stream];
        return 0;
    }

    /*
     * Packs 4 HUF_DEltX1 structs into a U64. This is used to lay down 4 entries at
     * a time.
     */
    [Inline]
    private static ulong HUF_DEltX1_set4(byte symbol, byte nbBits)
    {
        ulong d4;
        if (BitConverter.IsLittleEndian)
            d4 = (ulong)((symbol << 8) + nbBits);
        else
            d4 = (ulong)(symbol + (nbBits << 8));

        assert(d4 < 1U << 16);
        d4 *= 0x0001000100010001UL;
        return d4;
    }

    /*
     * Increase the tableLog to targetTableLog and rescales the stats.
     * If tableLog > targetTableLog this is a no-op.
     * @returns New tableLog
     */
    private static uint HUF_rescaleStats(
        byte* huffWeight,
        uint* rankVal,
        uint nbSymbols,
        uint tableLog,
        uint targetTableLog
    )
    {
        if (tableLog > targetTableLog)
            return tableLog;
        if (tableLog < targetTableLog)
        {
            var scale = targetTableLog - tableLog;
            uint s;
            for (s = 0; s < nbSymbols; ++s)
                huffWeight[s] += (byte)(huffWeight[s] == 0 ? 0 : scale);

            for (s = targetTableLog; s > scale; --s)
                rankVal[s] = rankVal[s - scale];

            for (s = scale; s > 0; --s)
                rankVal[s] = 0;
        }

        return targetTableLog;
    }

    private static nuint HUF_readDTableX1_wksp(
        uint* dTable,
        void* src,
        nuint srcSize,
        void* workSpace,
        nuint wkspSize,
        int flags
    )
    {
        uint tableLog = 0;
        uint nbSymbols = 0;
        nuint iSize;
        void* dtPtr = dTable + 1;
        var dt = (HufDEltX1*)dtPtr;
        var wksp = (HufReadDTableX1Workspace*)workSpace;
        if ((nuint)sizeof(HufReadDTableX1Workspace) > wkspSize)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorTableLogTooLarge));
        iSize = HUF_readStats_wksp(
            wksp->huffWeight,
            255 + 1,
            wksp->rankVal,
            &nbSymbols,
            &tableLog,
            src,
            srcSize,
            wksp->statsWksp,
            sizeof(uint) * 219,
            flags
        );
        if (ERR_isError(iSize))
            return iSize;
        {
            var dtd = HUF_getDTableDesc(dTable);
            var maxTableLog = (uint)(dtd.maxTableLog + 1);
            var targetTableLog = maxTableLog < 11 ? maxTableLog : 11;
            tableLog = HUF_rescaleStats(
                wksp->huffWeight,
                wksp->rankVal,
                nbSymbols,
                tableLog,
                targetTableLog
            );
            if (tableLog > (uint)(dtd.maxTableLog + 1))
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorTableLogTooLarge));
            dtd.tableType = 0;
            dtd.tableLog = (byte)tableLog;
            memcpy(dTable, &dtd, (uint)sizeof(DTableDesc));
        }

        {
            int n;
            uint nextRankStart = 0;
            const int unroll = 4;
            var nLimit = (int)nbSymbols - unroll + 1;
            for (n = 0; n < (int)tableLog + 1; n++)
            {
                var curr = nextRankStart;
                nextRankStart += wksp->rankVal[n];
                wksp->rankStart[n] = curr;
            }

            for (n = 0; n < nLimit; n += unroll)
            {
                int u;
                for (u = 0; u < unroll; ++u)
                {
                    nuint w = wksp->huffWeight[n + u];
                    wksp->symbols[wksp->rankStart[w]++] = (byte)(n + u);
                }
            }

            for (; n < (int)nbSymbols; ++n)
            {
                nuint w = wksp->huffWeight[n];
                wksp->symbols[wksp->rankStart[w]++] = (byte)n;
            }
        }

        {
            uint w;
            var symbol = (int)wksp->rankVal[0];
            var rankStart = 0;
            for (w = 1; w < tableLog + 1; ++w)
            {
                var symbolCount = (int)wksp->rankVal[w];
                var length = (1 << (int)w) >> 1;
                var uStart = rankStart;
                var nbBits = (byte)(tableLog + 1 - w);
                int s;
                // ReSharper disable once TooWideLocalVariableScope
                int u;
                switch (length)
                {
                    case 1:
                        for (s = 0; s < symbolCount; ++s)
                        {
                            HufDEltX1 d;
                            d.@byte = wksp->symbols[symbol + s];
                            d.nbBits = nbBits;
                            dt[uStart] = d;
                            uStart++;
                        }

                        break;
                    case 2:
                        for (s = 0; s < symbolCount; ++s)
                        {
                            HufDEltX1 d;
                            d.@byte = wksp->symbols[symbol + s];
                            d.nbBits = nbBits;
                            dt[uStart + 0] = d;
                            dt[uStart + 1] = d;
                            uStart += 2;
                        }

                        break;
                    case 4:
                        for (s = 0; s < symbolCount; ++s)
                        {
                            var d4 = HUF_DEltX1_set4(wksp->symbols[symbol + s], nbBits);
                            MEM_write64(dt + uStart, d4);
                            uStart += 4;
                        }

                        break;
                    case 8:
                        for (s = 0; s < symbolCount; ++s)
                        {
                            var d4 = HUF_DEltX1_set4(wksp->symbols[symbol + s], nbBits);
                            MEM_write64(dt + uStart, d4);
                            MEM_write64(dt + uStart + 4, d4);
                            uStart += 8;
                        }

                        break;
                    default:
                        for (s = 0; s < symbolCount; ++s)
                        {
                            var d4 = HUF_DEltX1_set4(wksp->symbols[symbol + s], nbBits);
                            for (u = 0; u < length; u += 16)
                            {
                                MEM_write64(dt + uStart + u + 0, d4);
                                MEM_write64(dt + uStart + u + 4, d4);
                                MEM_write64(dt + uStart + u + 8, d4);
                                MEM_write64(dt + uStart + u + 12, d4);
                            }

                            assert(u == length);
                            uStart += length;
                        }

                        break;
                }

                symbol += symbolCount;
                rankStart += symbolCount * length;
            }
        }

        return iSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Inline]
    private static byte HUF_decodeSymbolX1(BitDStreamT* dstream, HufDEltX1* dt, uint dtLog)
    {
        /* note : dtLog >= 1 */
        var val = BIT_lookBitsFast(dstream, dtLog);
        var c = dt[val].@byte;
        BIT_skipBits(dstream, dt[val].nbBits);
        return c;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint HUF_decodeStreamX1(
        byte* p,
        BitDStreamT* bitDPtr,
        byte* pEnd,
        HufDEltX1* dt,
        uint dtLog
    )
    {
        var pStart = p;
        if (pEnd - p > 3)
            while (
                BIT_reloadDStream(bitDPtr) == BitDStreamStatus.BitDStreamUnfinished
                && p < pEnd - 3
            )
            {
                if (MEM_64bits)
                    *p++ = HUF_decodeSymbolX1(bitDPtr, dt, dtLog);
                if (MEM_64bits || 12 <= 12)
                    *p++ = HUF_decodeSymbolX1(bitDPtr, dt, dtLog);
                if (MEM_64bits)
                    *p++ = HUF_decodeSymbolX1(bitDPtr, dt, dtLog);
                *p++ = HUF_decodeSymbolX1(bitDPtr, dt, dtLog);
            }
        else
            BIT_reloadDStream(bitDPtr);

        if (MEM_32bits)
            while (
                BIT_reloadDStream(bitDPtr) == BitDStreamStatus.BitDStreamUnfinished && p < pEnd
            )
                *p++ = HUF_decodeSymbolX1(bitDPtr, dt, dtLog);

        while (p < pEnd)
            *p++ = HUF_decodeSymbolX1(bitDPtr, dt, dtLog);
        return (nuint)(pEnd - pStart);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint HUF_decompress1X1_usingDTable_internal_body(
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        uint* dTable
    )
    {
        var op = (byte*)dst;
        var oend = op + dstSize;
        void* dtPtr = dTable + 1;
        var dt = (HufDEltX1*)dtPtr;
        BitDStreamT bitD;
        var dtd = HUF_getDTableDesc(dTable);
        uint dtLog = dtd.tableLog;
        {
            var varErr = BIT_initDStream(&bitD, cSrc, cSrcSize);
            if (ERR_isError(varErr))
                return varErr;
        }

        HUF_decodeStreamX1(op, &bitD, oend, dt, dtLog);
        if (BIT_endOfDStream(&bitD) == 0)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
        return dstSize;
    }

    /* HUF_decompress4X1_usingDTable_internal_body():
     * Conditions :
     * @dstSize >= 6
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint HUF_decompress4X1_usingDTable_internal_body(
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        uint* dTable
    )
    {
        if (cSrcSize < 10)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
        {
            var istart = (byte*)cSrc;
            var ostart = (byte*)dst;
            var oend = ostart + dstSize;
            var olimit = oend - 3;
            void* dtPtr = dTable + 1;
            var dt = (HufDEltX1*)dtPtr;
            /* Init */
            BitDStreamT bitD1;
            BitDStreamT bitD2;
            BitDStreamT bitD3;
            BitDStreamT bitD4;
            nuint length1 = MEM_readLE16(istart);
            nuint length2 = MEM_readLE16(istart + 2);
            nuint length3 = MEM_readLE16(istart + 4);
            var length4 = cSrcSize - (length1 + length2 + length3 + 6);
            /* jumpTable */
            var istart1 = istart + 6;
            var istart2 = istart1 + length1;
            var istart3 = istart2 + length2;
            var istart4 = istart3 + length3;
            var segmentSize = (dstSize + 3) / 4;
            var opStart2 = ostart + segmentSize;
            var opStart3 = opStart2 + segmentSize;
            var opStart4 = opStart3 + segmentSize;
            var op1 = ostart;
            var op2 = opStart2;
            var op3 = opStart3;
            var op4 = opStart4;
            var dtd = HUF_getDTableDesc(dTable);
            uint dtLog = dtd.tableLog;
            uint endSignal = 1;
            if (length4 > cSrcSize)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            if (opStart4 > oend)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            if (dstSize < 6)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            {
                var varErr = BIT_initDStream(&bitD1, istart1, length1);
                if (ERR_isError(varErr))
                    return varErr;
            }

            {
                var varErr = BIT_initDStream(&bitD2, istart2, length2);
                if (ERR_isError(varErr))
                    return varErr;
            }

            {
                var varErr = BIT_initDStream(&bitD3, istart3, length3);
                if (ERR_isError(varErr))
                    return varErr;
            }

            {
                var varErr = BIT_initDStream(&bitD4, istart4, length4);
                if (ERR_isError(varErr))
                    return varErr;
            }

            if ((nuint)(oend - op4) >= (nuint)sizeof(nuint))
                for (; (endSignal & (uint)(op4 < olimit ? 1 : 0)) != 0;)
                {
                    if (MEM_64bits)
                        *op1++ = HUF_decodeSymbolX1(&bitD1, dt, dtLog);
                    if (MEM_64bits)
                        *op2++ = HUF_decodeSymbolX1(&bitD2, dt, dtLog);
                    if (MEM_64bits)
                        *op3++ = HUF_decodeSymbolX1(&bitD3, dt, dtLog);
                    if (MEM_64bits)
                        *op4++ = HUF_decodeSymbolX1(&bitD4, dt, dtLog);
                    if (MEM_64bits || 12 <= 12)
                        *op1++ = HUF_decodeSymbolX1(&bitD1, dt, dtLog);
                    if (MEM_64bits || 12 <= 12)
                        *op2++ = HUF_decodeSymbolX1(&bitD2, dt, dtLog);
                    if (MEM_64bits || 12 <= 12)
                        *op3++ = HUF_decodeSymbolX1(&bitD3, dt, dtLog);
                    if (MEM_64bits || 12 <= 12)
                        *op4++ = HUF_decodeSymbolX1(&bitD4, dt, dtLog);
                    if (MEM_64bits)
                        *op1++ = HUF_decodeSymbolX1(&bitD1, dt, dtLog);
                    if (MEM_64bits)
                        *op2++ = HUF_decodeSymbolX1(&bitD2, dt, dtLog);
                    if (MEM_64bits)
                        *op3++ = HUF_decodeSymbolX1(&bitD3, dt, dtLog);
                    if (MEM_64bits)
                        *op4++ = HUF_decodeSymbolX1(&bitD4, dt, dtLog);
                    *op1++ = HUF_decodeSymbolX1(&bitD1, dt, dtLog);
                    *op2++ = HUF_decodeSymbolX1(&bitD2, dt, dtLog);
                    *op3++ = HUF_decodeSymbolX1(&bitD3, dt, dtLog);
                    *op4++ = HUF_decodeSymbolX1(&bitD4, dt, dtLog);
                    endSignal &=
                        BIT_reloadDStreamFast(&bitD1) == BitDStreamStatus.BitDStreamUnfinished
                            ? 1U
                            : 0U;
                    endSignal &=
                        BIT_reloadDStreamFast(&bitD2) == BitDStreamStatus.BitDStreamUnfinished
                            ? 1U
                            : 0U;
                    endSignal &=
                        BIT_reloadDStreamFast(&bitD3) == BitDStreamStatus.BitDStreamUnfinished
                            ? 1U
                            : 0U;
                    endSignal &=
                        BIT_reloadDStreamFast(&bitD4) == BitDStreamStatus.BitDStreamUnfinished
                            ? 1U
                            : 0U;
                }

            if (op1 > opStart2)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            if (op2 > opStart3)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            if (op3 > opStart4)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            HUF_decodeStreamX1(op1, &bitD1, opStart2, dt, dtLog);
            HUF_decodeStreamX1(op2, &bitD2, opStart3, dt, dtLog);
            HUF_decodeStreamX1(op3, &bitD3, opStart4, dt, dtLog);
            HUF_decodeStreamX1(op4, &bitD4, oend, dt, dtLog);
            {
                var endCheck =
                    BIT_endOfDStream(&bitD1)
                    & BIT_endOfDStream(&bitD2)
                    & BIT_endOfDStream(&bitD3)
                    & BIT_endOfDStream(&bitD4);
                if (endCheck == 0)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            }

            return dstSize;
        }
    }

    private static nuint HUF_decompress4X1_usingDTable_internal_default(
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        uint* dTable
    )
    {
        return HUF_decompress4X1_usingDTable_internal_body(dst, dstSize, cSrc, cSrcSize, dTable);
    }

    private static void HUF_decompress4X1_usingDTable_internal_fast_c_loop(
        HufDecompressFastArgs* args
    )
    {
        ulong bits0,
            bits1,
            bits2,
            bits3;
        byte* ip0,
            ip1,
            ip2,
            ip3;
        byte* op0,
            op1,
            op2,
            op3;
        var dtable = (ushort*)args->dt;
        var oend = args->oend;
        var ilimit = args->ilimit;
        bits0 = args->bits[0];
        bits1 = args->bits[1];
        bits2 = args->bits[2];
        bits3 = args->bits[3];
        ip0 = args->ip.e0;
        ip1 = args->ip.e1;
        ip2 = args->ip.e2;
        ip3 = args->ip.e3;
        op0 = args->op.e0;
        op1 = args->op.e1;
        op2 = args->op.e2;
        op3 = args->op.e3;
        assert(BitConverter.IsLittleEndian);
        assert(!MEM_32bits);
        for (;;)
        {
            byte* olimit;
            {
                assert(op0 <= op1);
                assert(ip0 >= ilimit);
            }

            {
                assert(op1 <= op2);
                assert(ip1 >= ilimit);
            }

            {
                assert(op2 <= op3);
                assert(ip2 >= ilimit);
            }

            {
                assert(op3 <= oend);
                assert(ip3 >= ilimit);
            }

            {
                /* Each iteration produces 5 output symbols per stream */
                var oiters = (nuint)(oend - op3) / 5;
                /* Each iteration consumes up to 11 bits * 5 = 55 bits < 7 bytes
                 * per stream.
                 */
                var iiters = (nuint)(ip0 - ilimit) / 7;
                /* We can safely run iters iterations before running bounds checks */
                var iters = oiters < iiters ? oiters : iiters;
                var symbols = iters * 5;
                olimit = op3 + symbols;
                if (op3 + 20 > olimit)
                    break;
                {
                    if (ip1 < ip0)
                        goto _out;
                }

                {
                    if (ip2 < ip1)
                        goto _out;
                }

                {
                    if (ip3 < ip2)
                        goto _out;
                }
            }

            {
                assert(ip1 >= ip0);
            }

            {
                assert(ip2 >= ip1);
            }

            {
                assert(ip3 >= ip2);
            }

            do
            {
                {
                    {
                        var index = (int)(bits0 >> 53);
                        int entry = dtable[index];
                        bits0 <<= entry & 63;
                        op0[0] = (byte)((entry >> 8) & 0xFF);
                    }

                    {
                        var index = (int)(bits1 >> 53);
                        int entry = dtable[index];
                        bits1 <<= entry & 63;
                        op1[0] = (byte)((entry >> 8) & 0xFF);
                    }

                    {
                        var index = (int)(bits2 >> 53);
                        int entry = dtable[index];
                        bits2 <<= entry & 63;
                        op2[0] = (byte)((entry >> 8) & 0xFF);
                    }

                    {
                        var index = (int)(bits3 >> 53);
                        int entry = dtable[index];
                        bits3 <<= entry & 63;
                        op3[0] = (byte)((entry >> 8) & 0xFF);
                    }
                }

                {
                    {
                        var index = (int)(bits0 >> 53);
                        int entry = dtable[index];
                        bits0 <<= entry & 63;
                        op0[1] = (byte)((entry >> 8) & 0xFF);
                    }

                    {
                        var index = (int)(bits1 >> 53);
                        int entry = dtable[index];
                        bits1 <<= entry & 63;
                        op1[1] = (byte)((entry >> 8) & 0xFF);
                    }

                    {
                        var index = (int)(bits2 >> 53);
                        int entry = dtable[index];
                        bits2 <<= entry & 63;
                        op2[1] = (byte)((entry >> 8) & 0xFF);
                    }

                    {
                        var index = (int)(bits3 >> 53);
                        int entry = dtable[index];
                        bits3 <<= entry & 63;
                        op3[1] = (byte)((entry >> 8) & 0xFF);
                    }
                }

                {
                    {
                        var index = (int)(bits0 >> 53);
                        int entry = dtable[index];
                        bits0 <<= entry & 63;
                        op0[2] = (byte)((entry >> 8) & 0xFF);
                    }

                    {
                        var index = (int)(bits1 >> 53);
                        int entry = dtable[index];
                        bits1 <<= entry & 63;
                        op1[2] = (byte)((entry >> 8) & 0xFF);
                    }

                    {
                        var index = (int)(bits2 >> 53);
                        int entry = dtable[index];
                        bits2 <<= entry & 63;
                        op2[2] = (byte)((entry >> 8) & 0xFF);
                    }

                    {
                        var index = (int)(bits3 >> 53);
                        int entry = dtable[index];
                        bits3 <<= entry & 63;
                        op3[2] = (byte)((entry >> 8) & 0xFF);
                    }
                }

                {
                    {
                        var index = (int)(bits0 >> 53);
                        int entry = dtable[index];
                        bits0 <<= entry & 63;
                        op0[3] = (byte)((entry >> 8) & 0xFF);
                    }

                    {
                        var index = (int)(bits1 >> 53);
                        int entry = dtable[index];
                        bits1 <<= entry & 63;
                        op1[3] = (byte)((entry >> 8) & 0xFF);
                    }

                    {
                        var index = (int)(bits2 >> 53);
                        int entry = dtable[index];
                        bits2 <<= entry & 63;
                        op2[3] = (byte)((entry >> 8) & 0xFF);
                    }

                    {
                        var index = (int)(bits3 >> 53);
                        int entry = dtable[index];
                        bits3 <<= entry & 63;
                        op3[3] = (byte)((entry >> 8) & 0xFF);
                    }
                }

                {
                    {
                        var index = (int)(bits0 >> 53);
                        int entry = dtable[index];
                        bits0 <<= entry & 63;
                        op0[4] = (byte)((entry >> 8) & 0xFF);
                    }

                    {
                        var index = (int)(bits1 >> 53);
                        int entry = dtable[index];
                        bits1 <<= entry & 63;
                        op1[4] = (byte)((entry >> 8) & 0xFF);
                    }

                    {
                        var index = (int)(bits2 >> 53);
                        int entry = dtable[index];
                        bits2 <<= entry & 63;
                        op2[4] = (byte)((entry >> 8) & 0xFF);
                    }

                    {
                        var index = (int)(bits3 >> 53);
                        int entry = dtable[index];
                        bits3 <<= entry & 63;
                        op3[4] = (byte)((entry >> 8) & 0xFF);
                    }
                }

                {
                    var ctz = (int)ZSTD_countTrailingZeros64(bits0);
                    var nbBits = ctz & 7;
                    var nbBytes = ctz >> 3;
                    op0 += 5;
                    ip0 -= nbBytes;
                    bits0 = MEM_read64(ip0) | 1;
                    bits0 <<= nbBits;
                }

                {
                    var ctz = (int)ZSTD_countTrailingZeros64(bits1);
                    var nbBits = ctz & 7;
                    var nbBytes = ctz >> 3;
                    op1 += 5;
                    ip1 -= nbBytes;
                    bits1 = MEM_read64(ip1) | 1;
                    bits1 <<= nbBits;
                }

                {
                    var ctz = (int)ZSTD_countTrailingZeros64(bits2);
                    var nbBits = ctz & 7;
                    var nbBytes = ctz >> 3;
                    op2 += 5;
                    ip2 -= nbBytes;
                    bits2 = MEM_read64(ip2) | 1;
                    bits2 <<= nbBits;
                }

                {
                    var ctz = (int)ZSTD_countTrailingZeros64(bits3);
                    var nbBits = ctz & 7;
                    var nbBytes = ctz >> 3;
                    op3 += 5;
                    ip3 -= nbBytes;
                    bits3 = MEM_read64(ip3) | 1;
                    bits3 <<= nbBits;
                }
            } while (op3 < olimit);
        }

        _out:
        args->bits[0] = bits0;
        args->bits[1] = bits1;
        args->bits[2] = bits2;
        args->bits[3] = bits3;
        args->ip.e0 = ip0;
        args->ip.e1 = ip1;
        args->ip.e2 = ip2;
        args->ip.e3 = ip3;
        args->op.e0 = op0;
        args->op.e1 = op1;
        args->op.e2 = op2;
        args->op.e3 = op3;
    }

    /*
     * @returns @p dstSize on success (>= 6)
     * 0 if the fallback implementation should be used
     * An error if an error occurred
     */
    private static nuint HUF_decompress4X1_usingDTable_internal_fast(
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        uint* dTable,
        void* loopFn
    )
    {
        void* dt = dTable + 1;
        var iend = (byte*)cSrc + 6;
        var oend = (byte*)dst + dstSize;
        HufDecompressFastArgs args;
        {
            var ret = HUF_DecompressFastArgs_init(&args, dst, dstSize, cSrc, cSrcSize, dTable);
            {
                var errCode = ret;
                if (ERR_isError(errCode))
                    return errCode;
            }

            if (ret == 0)
                return 0;
        }

        assert(args.ip.e0 >= args.ilimit);
        ((delegate* managed<HufDecompressFastArgs*, void>)loopFn)(&args);
        assert(args.ip.e0 >= iend);
        assert(args.ip.e1 >= iend);
        assert(args.ip.e2 >= iend);
        assert(args.ip.e3 >= iend);
        assert(args.op.e3 <= oend);
        {
            var segmentSize = (dstSize + 3) / 4;
            var segmentEnd = (byte*)dst;
            int i;
            for (i = 0; i < 4; ++i)
            {
                BitDStreamT bit;
                if (segmentSize <= (nuint)(oend - segmentEnd))
                    segmentEnd += segmentSize;
                else
                    segmentEnd = oend;
                {
                    var errCode = HUF_initRemainingDStream(&bit, &args, i, segmentEnd);
                    if (ERR_isError(errCode))
                        return errCode;
                }

                (&args.op.e0)[i] += HUF_decodeStreamX1(
                    (&args.op.e0)[i],
                    &bit,
                    segmentEnd,
                    (HufDEltX1*)dt,
                    11
                );
                if ((&args.op.e0)[i] != segmentEnd)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            }
        }

        assert(dstSize != 0);
        return dstSize;
    }

    private static nuint HUF_decompress1X1_usingDTable_internal(
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        uint* dTable,
        // ReSharper disable once UnusedParameter.Local
        int flags
    )
    {
        return HUF_decompress1X1_usingDTable_internal_body(dst, dstSize, cSrc, cSrcSize, dTable);
    }

    private static nuint HUF_decompress4X1_usingDTable_internal(
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        uint* dTable,
        int flags
    )
    {
        void* fallbackFn = (delegate* managed<void*, nuint, void*, nuint, uint*, nuint>)(
            &HUF_decompress4X1_usingDTable_internal_default
        );
        void* loopFn = (delegate* managed<HufDecompressFastArgs*, void>)(
            &HUF_decompress4X1_usingDTable_internal_fast_c_loop
        );
        if ((flags & (int)HufFlagsE.HufFlagsDisableFast) == 0)
        {
            var ret = HUF_decompress4X1_usingDTable_internal_fast(
                dst,
                dstSize,
                cSrc,
                cSrcSize,
                dTable,
                loopFn
            );
            if (ret != 0)
                return ret;
        }

        return ((delegate* managed<void*, nuint, void*, nuint, uint*, nuint>)fallbackFn)(
            dst,
            dstSize,
            cSrc,
            cSrcSize,
            dTable
        );
    }

    private static nuint HUF_decompress4X1_DCtx_wksp(
        uint* dctx,
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        void* workSpace,
        nuint wkspSize,
        int flags
    )
    {
        var ip = (byte*)cSrc;
        var hSize = HUF_readDTableX1_wksp(dctx, cSrc, cSrcSize, workSpace, wkspSize, flags);
        if (ERR_isError(hSize))
            return hSize;
        if (hSize >= cSrcSize)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
        ip += hSize;
        cSrcSize -= hSize;
        return HUF_decompress4X1_usingDTable_internal(dst, dstSize, ip, cSrcSize, dctx, flags);
    }

    /*
     * Constructs a HUF_DEltX2 in a U32.
     */
    [Inline]
    private static uint HUF_buildDEltX2U32(uint symbol, uint nbBits, uint baseSeq, int level)
    {
        uint seq;
        if (BitConverter.IsLittleEndian)
        {
            seq = level == 1 ? symbol : baseSeq + (symbol << 8);
            return seq + (nbBits << 16) + ((uint)level << 24);
        }

        seq = level == 1 ? symbol << 8 : (baseSeq << 8) + symbol;
        return (seq << 16) + (nbBits << 8) + (uint)level;
    }

    /*
     * Constructs a HUF_DEltX2.
     */
    [Inline]
    private static HufDEltX2 HUF_buildDEltX2(uint symbol, uint nbBits, uint baseSeq, int level)
    {
        HufDEltX2 dElt;
        var val = HUF_buildDEltX2U32(symbol, nbBits, baseSeq, level);
        memcpy(&dElt, &val, sizeof(uint));
        return dElt;
    }

    /*
     * Constructs 2 HUF_DEltX2s and packs them into a U64.
     */
    [Inline]
    private static ulong HUF_buildDEltX2U64(uint symbol, uint nbBits, ushort baseSeq, int level)
    {
        var dElt = HUF_buildDEltX2U32(symbol, nbBits, baseSeq, level);
        return dElt + ((ulong)dElt << 32);
    }

    /*
     * Fills the DTable rank with all the symbols from [begin, end) that are each
     * nbBits long.
     *
     * @param DTableRank The start of the rank in the DTable.
     * @param begin The first symbol to fill (inclusive).
     * @param end The last symbol to fill (exclusive).
     * @param nbBits Each symbol is nbBits long.
     * @param tableLog The table log.
     * @param baseSeq If level == 1 { 0 } else { the first level symbol }
     * @param level The level in the table. Must be 1 or 2.
     */
    [Inline]
    private static void HUF_fillDTableX2ForWeight(
        HufDEltX2* dTableRank,
        SortedSymbolT* begin,
        SortedSymbolT* end,
        uint nbBits,
        uint tableLog,
        ushort baseSeq,
        int level
    )
    {
        /* quiet static-analyzer */
        var length = 1U << (int)((tableLog - nbBits) & 0x1F);
        SortedSymbolT* ptr;
        assert(level is >= 1 and <= 2);
        switch (length)
        {
            case 1:
                for (ptr = begin; ptr != end; ++ptr)
                {
                    var dElt = HUF_buildDEltX2(ptr->symbol, nbBits, baseSeq, level);
                    *dTableRank++ = dElt;
                }

                break;
            case 2:
                for (ptr = begin; ptr != end; ++ptr)
                {
                    var dElt = HUF_buildDEltX2(ptr->symbol, nbBits, baseSeq, level);
                    dTableRank[0] = dElt;
                    dTableRank[1] = dElt;
                    dTableRank += 2;
                }

                break;
            case 4:
                for (ptr = begin; ptr != end; ++ptr)
                {
                    var dEltX2 = HUF_buildDEltX2U64(ptr->symbol, nbBits, baseSeq, level);
                    memcpy(dTableRank + 0, &dEltX2, sizeof(ulong));
                    memcpy(dTableRank + 2, &dEltX2, sizeof(ulong));
                    dTableRank += 4;
                }

                break;
            case 8:
                for (ptr = begin; ptr != end; ++ptr)
                {
                    var dEltX2 = HUF_buildDEltX2U64(ptr->symbol, nbBits, baseSeq, level);
                    memcpy(dTableRank + 0, &dEltX2, sizeof(ulong));
                    memcpy(dTableRank + 2, &dEltX2, sizeof(ulong));
                    memcpy(dTableRank + 4, &dEltX2, sizeof(ulong));
                    memcpy(dTableRank + 6, &dEltX2, sizeof(ulong));
                    dTableRank += 8;
                }

                break;
            default:
                for (ptr = begin; ptr != end; ++ptr)
                {
                    var dEltX2 = HUF_buildDEltX2U64(ptr->symbol, nbBits, baseSeq, level);
                    var dTableRankEnd = dTableRank + length;
                    for (; dTableRank != dTableRankEnd; dTableRank += 8)
                    {
                        memcpy(dTableRank + 0, &dEltX2, sizeof(ulong));
                        memcpy(dTableRank + 2, &dEltX2, sizeof(ulong));
                        memcpy(dTableRank + 4, &dEltX2, sizeof(ulong));
                        memcpy(dTableRank + 6, &dEltX2, sizeof(ulong));
                    }
                }

                break;
        }
    }

    /* HUF_fillDTableX2Level2() :
     * `rankValOrigin` must be a table of at least (HUF_TABLELOG_MAX + 1) U32 */
    [Inline]
    private static void HUF_fillDTableX2Level2(
        HufDEltX2* dTable,
        uint targetLog,
        uint consumedBits,
        uint* rankVal,
        int minWeight,
        int maxWeight1,
        SortedSymbolT* sortedSymbols,
        uint* rankStart,
        uint nbBitsBaseline,
        ushort baseSeq
    )
    {
        if (minWeight > 1)
        {
            /* quiet static-analyzer */
            var length = 1U << (int)((targetLog - consumedBits) & 0x1F);
            /* baseSeq */
            var dEltX2 = HUF_buildDEltX2U64(baseSeq, consumedBits, 0, 1);
            var skipSize = (int)rankVal[minWeight];
            assert(length > 1);
            assert((uint)skipSize < length);
            switch (length)
            {
                case 2:
                    assert(skipSize == 1);
                    memcpy(dTable, &dEltX2, sizeof(ulong));
                    break;
                case 4:
                    assert(skipSize <= 4);
                    memcpy(dTable + 0, &dEltX2, sizeof(ulong));
                    memcpy(dTable + 2, &dEltX2, sizeof(ulong));
                    break;
                default:
                {
                    int i;
                    for (i = 0; i < skipSize; i += 8)
                    {
                        memcpy(dTable + i + 0, &dEltX2, sizeof(ulong));
                        memcpy(dTable + i + 2, &dEltX2, sizeof(ulong));
                        memcpy(dTable + i + 4, &dEltX2, sizeof(ulong));
                        memcpy(dTable + i + 6, &dEltX2, sizeof(ulong));
                    }
                }

                    break;
            }
        }

        {
            int w;
            for (w = minWeight; w < maxWeight1; ++w)
            {
                var begin = (int)rankStart[w];
                var end = (int)rankStart[w + 1];
                var nbBits = nbBitsBaseline - (uint)w;
                var totalBits = nbBits + consumedBits;
                HUF_fillDTableX2ForWeight(
                    dTable + rankVal[w],
                    sortedSymbols + begin,
                    sortedSymbols + end,
                    totalBits,
                    targetLog,
                    baseSeq,
                    2
                );
            }
        }
    }

    private static void HUF_fillDTableX2(
        HufDEltX2* dTable,
        uint targetLog,
        SortedSymbolT* sortedList,
        uint* rankStart,
        RankValColT* rankValOrigin,
        uint maxWeight,
        uint nbBitsBaseline
    )
    {
        var rankVal = (uint*)&rankValOrigin[0];
        /* note : targetLog >= srcLog, hence scaleLog <= 1 */
        var scaleLog = (int)(nbBitsBaseline - targetLog);
        var minBits = nbBitsBaseline - maxWeight;
        int w;
        var wEnd = (int)maxWeight + 1;
        for (w = 1; w < wEnd; ++w)
        {
            var begin = (int)rankStart[w];
            var end = (int)rankStart[w + 1];
            var nbBits = nbBitsBaseline - (uint)w;
            if (targetLog - nbBits >= minBits)
            {
                /* Enough room for a second symbol. */
                var start = (int)rankVal[w];
                /* quiet static-analyzer */
                var length = 1U << (int)((targetLog - nbBits) & 0x1F);
                var minWeight = (int)(nbBits + (uint)scaleLog);
                int s;
                if (minWeight < 1)
                    minWeight = 1;
                for (s = begin; s != end; ++s)
                {
                    HUF_fillDTableX2Level2(
                        dTable + start,
                        targetLog,
                        nbBits,
                        (uint*)&rankValOrigin[nbBits],
                        minWeight,
                        wEnd,
                        sortedList,
                        rankStart,
                        nbBitsBaseline,
                        sortedList[s].symbol
                    );
                    start += (int)length;
                }
            }
            else
            {
                HUF_fillDTableX2ForWeight(
                    dTable + rankVal[w],
                    sortedList + begin,
                    sortedList + end,
                    nbBits,
                    targetLog,
                    0,
                    1
                );
            }
        }
    }

    private static nuint HUF_readDTableX2_wksp(
        uint* dTable,
        void* src,
        nuint srcSize,
        void* workSpace,
        nuint wkspSize,
        int flags
    )
    {
        uint tableLog,
            maxW,
            nbSymbols;
        var dtd = HUF_getDTableDesc(dTable);
        uint maxTableLog = dtd.maxTableLog;
        nuint iSize;
        /* force compiler to avoid strict-aliasing */
        void* dtPtr = dTable + 1;
        var dt = (HufDEltX2*)dtPtr;
        uint* rankStart;
        var wksp = (HufReadDTableX2Workspace*)workSpace;
        if ((nuint)sizeof(HufReadDTableX2Workspace) > wkspSize)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorGeneric));
        rankStart = wksp->rankStart0 + 1;
        memset(wksp->rankStats, 0, sizeof(uint) * 13);
        memset(wksp->rankStart0, 0, sizeof(uint) * 15);
        if (maxTableLog > 12)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorTableLogTooLarge));
        iSize = HUF_readStats_wksp(
            wksp->weightList,
            255 + 1,
            wksp->rankStats,
            &nbSymbols,
            &tableLog,
            src,
            srcSize,
            wksp->calleeWksp,
            sizeof(uint) * 219,
            flags
        );
        if (ERR_isError(iSize))
            return iSize;
        if (tableLog > maxTableLog)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorTableLogTooLarge));
        if (tableLog <= 11 && maxTableLog > 11)
            maxTableLog = 11;
        for (maxW = tableLog; wksp->rankStats[maxW] == 0; maxW--)
        {
        }

        {
            uint w,
                nextRankStart = 0;
            for (w = 1; w < maxW + 1; w++)
            {
                var curr = nextRankStart;
                nextRankStart += wksp->rankStats[w];
                rankStart[w] = curr;
            }

            rankStart[0] = nextRankStart;
            rankStart[maxW + 1] = nextRankStart;
        }

        {
            uint s;
            for (s = 0; s < nbSymbols; s++)
            {
                uint w = wksp->weightList[s];
                var r = rankStart[w]++;
                (&wksp->sortedSymbol.e0)[r].symbol = (byte)s;
            }

            rankStart[0] = 0;
        }

        {
            var rankVal0 = (uint*)&wksp->rankVal.e0;
            {
                /* tableLog <= maxTableLog */
                var rescale = (int)(maxTableLog - tableLog - 1);
                uint nextRankVal = 0;
                uint w;
                for (w = 1; w < maxW + 1; w++)
                {
                    var curr = nextRankVal;
                    nextRankVal += wksp->rankStats[w] << (int)(w + (uint)rescale);
                    rankVal0[w] = curr;
                }
            }

            {
                var minBits = tableLog + 1 - maxW;
                uint consumed;
                for (consumed = minBits; consumed < maxTableLog - minBits + 1; consumed++)
                {
                    var rankValPtr = (uint*)&(&wksp->rankVal.e0)[consumed];
                    uint w;
                    for (w = 1; w < maxW + 1; w++)
                        rankValPtr[w] = rankVal0[w] >> (int)consumed;
                }
            }
        }

        HUF_fillDTableX2(
            dt,
            maxTableLog,
            &wksp->sortedSymbol.e0,
            wksp->rankStart0,
            &wksp->rankVal.e0,
            maxW,
            tableLog + 1
        );
        dtd.tableLog = (byte)maxTableLog;
        dtd.tableType = 1;
        memcpy(dTable, &dtd, (uint)sizeof(DTableDesc));
        return iSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Inline]
    private static uint HUF_decodeSymbolX2(
        void* op,
        BitDStreamT* dStream,
        HufDEltX2* dt,
        uint dtLog
    )
    {
        /* note : dtLog >= 1 */
        var val = BIT_lookBitsFast(dStream, dtLog);
        memcpy(op, &dt[val].sequence, 2);
        BIT_skipBits(dStream, dt[val].nbBits);
        return dt[val].length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint HUF_decodeLastSymbolX2(
        void* op,
        BitDStreamT* dStream,
        HufDEltX2* dt,
        uint dtLog
    )
    {
        /* note : dtLog >= 1 */
        var val = BIT_lookBitsFast(dStream, dtLog);
        memcpy(op, &dt[val].sequence, 1);
        if (dt[val].length == 1)
        {
            BIT_skipBits(dStream, dt[val].nbBits);
        }
        else
        {
            if (dStream->bitsConsumed < (uint)(sizeof(nuint) * 8))
            {
                BIT_skipBits(dStream, dt[val].nbBits);
                if (dStream->bitsConsumed > (uint)(sizeof(nuint) * 8))
                    dStream->bitsConsumed = (uint)(sizeof(nuint) * 8);
            }
        }

        return 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint HUF_decodeStreamX2(
        byte* p,
        BitDStreamT* bitDPtr,
        byte* pEnd,
        HufDEltX2* dt,
        uint dtLog
    )
    {
        var pStart = p;
        if ((nuint)(pEnd - p) >= (nuint)sizeof(nuint))
        {
            if (dtLog <= 11 && MEM_64bits)
                while (
                    BIT_reloadDStream(bitDPtr) == BitDStreamStatus.BitDStreamUnfinished
                    && p < pEnd - 9
                )
                {
                    p += HUF_decodeSymbolX2(p, bitDPtr, dt, dtLog);
                    p += HUF_decodeSymbolX2(p, bitDPtr, dt, dtLog);
                    p += HUF_decodeSymbolX2(p, bitDPtr, dt, dtLog);
                    p += HUF_decodeSymbolX2(p, bitDPtr, dt, dtLog);
                    p += HUF_decodeSymbolX2(p, bitDPtr, dt, dtLog);
                }
            else
                while (
                    BIT_reloadDStream(bitDPtr) == BitDStreamStatus.BitDStreamUnfinished
                    && p < pEnd - (sizeof(nuint) - 1)
                )
                {
                    if (MEM_64bits)
                        p += HUF_decodeSymbolX2(p, bitDPtr, dt, dtLog);
                    if (MEM_64bits || 12 <= 12)
                        p += HUF_decodeSymbolX2(p, bitDPtr, dt, dtLog);
                    if (MEM_64bits)
                        p += HUF_decodeSymbolX2(p, bitDPtr, dt, dtLog);
                    p += HUF_decodeSymbolX2(p, bitDPtr, dt, dtLog);
                }
        }
        else
        {
            BIT_reloadDStream(bitDPtr);
        }

        if ((nuint)(pEnd - p) >= 2)
        {
            while (
                BIT_reloadDStream(bitDPtr) == BitDStreamStatus.BitDStreamUnfinished
                && p <= pEnd - 2
            )
                p += HUF_decodeSymbolX2(p, bitDPtr, dt, dtLog);

            while (p <= pEnd - 2)
                p += HUF_decodeSymbolX2(p, bitDPtr, dt, dtLog);
        }

        if (p < pEnd)
            p += HUF_decodeLastSymbolX2(p, bitDPtr, dt, dtLog);
        return (nuint)(p - pStart);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint HUF_decompress1X2_usingDTable_internal_body(
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        uint* dTable
    )
    {
        BitDStreamT bitD;
        {
            var varErr = BIT_initDStream(&bitD, cSrc, cSrcSize);
            if (ERR_isError(varErr))
                return varErr;
        }

        {
            var ostart = (byte*)dst;
            var oend = ostart + dstSize;
            /* force compiler to not use strict-aliasing */
            void* dtPtr = dTable + 1;
            var dt = (HufDEltX2*)dtPtr;
            var dtd = HUF_getDTableDesc(dTable);
            HUF_decodeStreamX2(ostart, &bitD, oend, dt, dtd.tableLog);
        }

        if (BIT_endOfDStream(&bitD) == 0)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
        return dstSize;
    }

    /* HUF_decompress4X2_usingDTable_internal_body():
     * Conditions:
     * @dstSize >= 6
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint HUF_decompress4X2_usingDTable_internal_body(
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        uint* dTable
    )
    {
        if (cSrcSize < 10)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
        {
            var istart = (byte*)cSrc;
            var ostart = (byte*)dst;
            var oend = ostart + dstSize;
            var olimit = oend - (sizeof(nuint) - 1);
            void* dtPtr = dTable + 1;
            var dt = (HufDEltX2*)dtPtr;
            /* Init */
            BitDStreamT bitD1;
            BitDStreamT bitD2;
            BitDStreamT bitD3;
            BitDStreamT bitD4;
            nuint length1 = MEM_readLE16(istart);
            nuint length2 = MEM_readLE16(istart + 2);
            nuint length3 = MEM_readLE16(istart + 4);
            var length4 = cSrcSize - (length1 + length2 + length3 + 6);
            /* jumpTable */
            var istart1 = istart + 6;
            var istart2 = istart1 + length1;
            var istart3 = istart2 + length2;
            var istart4 = istart3 + length3;
            var segmentSize = (dstSize + 3) / 4;
            var opStart2 = ostart + segmentSize;
            var opStart3 = opStart2 + segmentSize;
            var opStart4 = opStart3 + segmentSize;
            var op1 = ostart;
            var op2 = opStart2;
            var op3 = opStart3;
            var op4 = opStart4;
            uint endSignal = 1;
            var dtd = HUF_getDTableDesc(dTable);
            uint dtLog = dtd.tableLog;
            if (length4 > cSrcSize)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            if (opStart4 > oend)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            if (dstSize < 6)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            {
                var varErr = BIT_initDStream(&bitD1, istart1, length1);
                if (ERR_isError(varErr))
                    return varErr;
            }

            {
                var varErr = BIT_initDStream(&bitD2, istart2, length2);
                if (ERR_isError(varErr))
                    return varErr;
            }

            {
                var varErr = BIT_initDStream(&bitD3, istart3, length3);
                if (ERR_isError(varErr))
                    return varErr;
            }

            {
                var varErr = BIT_initDStream(&bitD4, istart4, length4);
                if (ERR_isError(varErr))
                    return varErr;
            }

            if ((nuint)(oend - op4) >= (nuint)sizeof(nuint))
                for (; (endSignal & (uint)(op4 < olimit ? 1 : 0)) != 0;)
                {
                    if (MEM_64bits)
                        op1 += HUF_decodeSymbolX2(op1, &bitD1, dt, dtLog);
                    if (MEM_64bits || 12 <= 12)
                        op1 += HUF_decodeSymbolX2(op1, &bitD1, dt, dtLog);
                    if (MEM_64bits)
                        op1 += HUF_decodeSymbolX2(op1, &bitD1, dt, dtLog);
                    op1 += HUF_decodeSymbolX2(op1, &bitD1, dt, dtLog);
                    if (MEM_64bits)
                        op2 += HUF_decodeSymbolX2(op2, &bitD2, dt, dtLog);
                    if (MEM_64bits || 12 <= 12)
                        op2 += HUF_decodeSymbolX2(op2, &bitD2, dt, dtLog);
                    if (MEM_64bits)
                        op2 += HUF_decodeSymbolX2(op2, &bitD2, dt, dtLog);
                    op2 += HUF_decodeSymbolX2(op2, &bitD2, dt, dtLog);
                    endSignal &=
                        BIT_reloadDStreamFast(&bitD1) == BitDStreamStatus.BitDStreamUnfinished
                            ? 1U
                            : 0U;
                    endSignal &=
                        BIT_reloadDStreamFast(&bitD2) == BitDStreamStatus.BitDStreamUnfinished
                            ? 1U
                            : 0U;
                    if (MEM_64bits)
                        op3 += HUF_decodeSymbolX2(op3, &bitD3, dt, dtLog);
                    if (MEM_64bits || 12 <= 12)
                        op3 += HUF_decodeSymbolX2(op3, &bitD3, dt, dtLog);
                    if (MEM_64bits)
                        op3 += HUF_decodeSymbolX2(op3, &bitD3, dt, dtLog);
                    op3 += HUF_decodeSymbolX2(op3, &bitD3, dt, dtLog);
                    if (MEM_64bits)
                        op4 += HUF_decodeSymbolX2(op4, &bitD4, dt, dtLog);
                    if (MEM_64bits || 12 <= 12)
                        op4 += HUF_decodeSymbolX2(op4, &bitD4, dt, dtLog);
                    if (MEM_64bits)
                        op4 += HUF_decodeSymbolX2(op4, &bitD4, dt, dtLog);
                    op4 += HUF_decodeSymbolX2(op4, &bitD4, dt, dtLog);
                    endSignal &=
                        BIT_reloadDStreamFast(&bitD3) == BitDStreamStatus.BitDStreamUnfinished
                            ? 1U
                            : 0U;
                    endSignal &=
                        BIT_reloadDStreamFast(&bitD4) == BitDStreamStatus.BitDStreamUnfinished
                            ? 1U
                            : 0U;
                }

            if (op1 > opStart2)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            if (op2 > opStart3)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            if (op3 > opStart4)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            HUF_decodeStreamX2(op1, &bitD1, opStart2, dt, dtLog);
            HUF_decodeStreamX2(op2, &bitD2, opStart3, dt, dtLog);
            HUF_decodeStreamX2(op3, &bitD3, opStart4, dt, dtLog);
            HUF_decodeStreamX2(op4, &bitD4, oend, dt, dtLog);
            {
                var endCheck =
                    BIT_endOfDStream(&bitD1)
                    & BIT_endOfDStream(&bitD2)
                    & BIT_endOfDStream(&bitD3)
                    & BIT_endOfDStream(&bitD4);
                if (endCheck == 0)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            }

            return dstSize;
        }
    }

    private static nuint HUF_decompress4X2_usingDTable_internal_default(
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        uint* dTable
    )
    {
        return HUF_decompress4X2_usingDTable_internal_body(dst, dstSize, cSrc, cSrcSize, dTable);
    }

    private static void HUF_decompress4X2_usingDTable_internal_fast_c_loop(
        HufDecompressFastArgs* args
    )
    {
        ulong bits0,
            bits1,
            bits2,
            bits3;
        byte* ip0,
            ip1,
            ip2,
            ip3;
        byte* op0,
            op1,
            op2,
            op3;
        byte* oend0,
            oend1,
            oend2,
            oend3;
        var dtable = (HufDEltX2*)args->dt;
        var ilimit = args->ilimit;
        bits0 = args->bits[0];
        bits1 = args->bits[1];
        bits2 = args->bits[2];
        bits3 = args->bits[3];
        ip0 = args->ip.e0;
        ip1 = args->ip.e1;
        ip2 = args->ip.e2;
        ip3 = args->ip.e3;
        op0 = args->op.e0;
        op1 = args->op.e1;
        op2 = args->op.e2;
        op3 = args->op.e3;
        oend0 = op1;
        oend1 = op2;
        oend2 = op3;
        oend3 = args->oend;
        assert(BitConverter.IsLittleEndian);
        assert(!MEM_32bits);
        for (;;)
        {
            byte* olimit;
            {
                assert(op0 <= oend0);
                assert(ip0 >= ilimit);
            }

            {
                assert(op1 <= oend1);
                assert(ip1 >= ilimit);
            }

            {
                assert(op2 <= oend2);
                assert(ip2 >= ilimit);
            }

            {
                assert(op3 <= oend3);
                assert(ip3 >= ilimit);
            }

            {
                /* Each loop does 5 table lookups for each of the 4 streams.
                 * Each table lookup consumes up to 11 bits of input, and produces
                 * up to 2 bytes of output.
                 */
                /* We can consume up to 7 bytes of input per iteration per stream.
                 * We also know that each input pointer is >= ip[0]. So we can run
                 * iters loops before running out of input.
                 */
                var iters = (nuint)(ip0 - ilimit) / 7;
                {
                    var oiters = (nuint)(oend0 - op0) / 10;
                    iters = iters < oiters ? iters : oiters;
                }

                {
                    var oiters = (nuint)(oend1 - op1) / 10;
                    iters = iters < oiters ? iters : oiters;
                }

                {
                    var oiters = (nuint)(oend2 - op2) / 10;
                    iters = iters < oiters ? iters : oiters;
                }

                {
                    var oiters = (nuint)(oend3 - op3) / 10;
                    iters = iters < oiters ? iters : oiters;
                }

                olimit = op3 + iters * 5;
                if (op3 + 10 > olimit)
                    break;
                {
                    if (ip1 < ip0)
                        goto _out;
                }

                {
                    if (ip2 < ip1)
                        goto _out;
                }

                {
                    if (ip3 < ip2)
                        goto _out;
                }
            }

            {
                assert(ip1 >= ip0);
            }

            {
                assert(ip2 >= ip1);
            }

            {
                assert(ip3 >= ip2);
            }

            do
            {
                {
                    {
                        var index = (int)(bits0 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op0, entry.sequence);
                        bits0 <<= entry.nbBits;
                        op0 += entry.length;
                    }

                    {
                        var index = (int)(bits1 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op1, entry.sequence);
                        bits1 <<= entry.nbBits;
                        op1 += entry.length;
                    }

                    {
                        var index = (int)(bits2 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op2, entry.sequence);
                        bits2 <<= entry.nbBits;
                        op2 += entry.length;
                    }
                }

                {
                    {
                        var index = (int)(bits0 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op0, entry.sequence);
                        bits0 <<= entry.nbBits;
                        op0 += entry.length;
                    }

                    {
                        var index = (int)(bits1 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op1, entry.sequence);
                        bits1 <<= entry.nbBits;
                        op1 += entry.length;
                    }

                    {
                        var index = (int)(bits2 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op2, entry.sequence);
                        bits2 <<= entry.nbBits;
                        op2 += entry.length;
                    }
                }

                {
                    {
                        var index = (int)(bits0 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op0, entry.sequence);
                        bits0 <<= entry.nbBits;
                        op0 += entry.length;
                    }

                    {
                        var index = (int)(bits1 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op1, entry.sequence);
                        bits1 <<= entry.nbBits;
                        op1 += entry.length;
                    }

                    {
                        var index = (int)(bits2 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op2, entry.sequence);
                        bits2 <<= entry.nbBits;
                        op2 += entry.length;
                    }
                }

                {
                    {
                        var index = (int)(bits0 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op0, entry.sequence);
                        bits0 <<= entry.nbBits;
                        op0 += entry.length;
                    }

                    {
                        var index = (int)(bits1 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op1, entry.sequence);
                        bits1 <<= entry.nbBits;
                        op1 += entry.length;
                    }

                    {
                        var index = (int)(bits2 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op2, entry.sequence);
                        bits2 <<= entry.nbBits;
                        op2 += entry.length;
                    }
                }

                {
                    {
                        var index = (int)(bits0 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op0, entry.sequence);
                        bits0 <<= entry.nbBits;
                        op0 += entry.length;
                    }

                    {
                        var index = (int)(bits1 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op1, entry.sequence);
                        bits1 <<= entry.nbBits;
                        op1 += entry.length;
                    }

                    {
                        var index = (int)(bits2 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op2, entry.sequence);
                        bits2 <<= entry.nbBits;
                        op2 += entry.length;
                    }
                }

                {
                    var index = (int)(bits3 >> 53);
                    var entry = dtable[index];
                    MEM_write16(op3, entry.sequence);
                    bits3 <<= entry.nbBits;
                    op3 += entry.length;
                }

                {
                    {
                        var index = (int)(bits3 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op3, entry.sequence);
                        bits3 <<= entry.nbBits;
                        op3 += entry.length;
                    }

                    {
                        var ctz = (int)ZSTD_countTrailingZeros64(bits0);
                        var nbBits = ctz & 7;
                        var nbBytes = ctz >> 3;
                        ip0 -= nbBytes;
                        bits0 = MEM_read64(ip0) | 1;
                        bits0 <<= nbBits;
                    }
                }

                {
                    {
                        var index = (int)(bits3 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op3, entry.sequence);
                        bits3 <<= entry.nbBits;
                        op3 += entry.length;
                    }

                    {
                        var ctz = (int)ZSTD_countTrailingZeros64(bits1);
                        var nbBits = ctz & 7;
                        var nbBytes = ctz >> 3;
                        ip1 -= nbBytes;
                        bits1 = MEM_read64(ip1) | 1;
                        bits1 <<= nbBits;
                    }
                }

                {
                    {
                        var index = (int)(bits3 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op3, entry.sequence);
                        bits3 <<= entry.nbBits;
                        op3 += entry.length;
                    }

                    {
                        var ctz = (int)ZSTD_countTrailingZeros64(bits2);
                        var nbBits = ctz & 7;
                        var nbBytes = ctz >> 3;
                        ip2 -= nbBytes;
                        bits2 = MEM_read64(ip2) | 1;
                        bits2 <<= nbBits;
                    }
                }

                {
                    {
                        var index = (int)(bits3 >> 53);
                        var entry = dtable[index];
                        MEM_write16(op3, entry.sequence);
                        bits3 <<= entry.nbBits;
                        op3 += entry.length;
                    }

                    {
                        var ctz = (int)ZSTD_countTrailingZeros64(bits3);
                        var nbBits = ctz & 7;
                        var nbBytes = ctz >> 3;
                        ip3 -= nbBytes;
                        bits3 = MEM_read64(ip3) | 1;
                        bits3 <<= nbBits;
                    }
                }
            } while (op3 < olimit);
        }

        _out:
        args->bits[0] = bits0;
        args->bits[1] = bits1;
        args->bits[2] = bits2;
        args->bits[3] = bits3;
        args->ip.e0 = ip0;
        args->ip.e1 = ip1;
        args->ip.e2 = ip2;
        args->ip.e3 = ip3;
        args->op.e0 = op0;
        args->op.e1 = op1;
        args->op.e2 = op2;
        args->op.e3 = op3;
    }

    private static nuint HUF_decompress4X2_usingDTable_internal_fast(
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        uint* dTable,
        void* loopFn
    )
    {
        void* dt = dTable + 1;
        var iend = (byte*)cSrc + 6;
        var oend = (byte*)dst + dstSize;
        HufDecompressFastArgs args;
        {
            var ret = HUF_DecompressFastArgs_init(&args, dst, dstSize, cSrc, cSrcSize, dTable);
            {
                var errCode = ret;
                if (ERR_isError(errCode))
                    return errCode;
            }

            if (ret == 0)
                return 0;
        }

        assert(args.ip.e0 >= args.ilimit);
        ((delegate* managed<HufDecompressFastArgs*, void>)loopFn)(&args);
        assert(args.ip.e0 >= iend);
        assert(args.ip.e1 >= iend);
        assert(args.ip.e2 >= iend);
        assert(args.ip.e3 >= iend);
        assert(args.op.e3 <= oend);
        {
            var segmentSize = (dstSize + 3) / 4;
            var segmentEnd = (byte*)dst;
            int i;
            for (i = 0; i < 4; ++i)
            {
                BitDStreamT bit;
                if (segmentSize <= (nuint)(oend - segmentEnd))
                    segmentEnd += segmentSize;
                else
                    segmentEnd = oend;
                {
                    var errCode = HUF_initRemainingDStream(&bit, &args, i, segmentEnd);
                    if (ERR_isError(errCode))
                        return errCode;
                }

                (&args.op.e0)[i] += HUF_decodeStreamX2(
                    (&args.op.e0)[i],
                    &bit,
                    segmentEnd,
                    (HufDEltX2*)dt,
                    11
                );
                if ((&args.op.e0)[i] != segmentEnd)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            }
        }

        return dstSize;
    }

    private static nuint HUF_decompress4X2_usingDTable_internal(
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        uint* dTable,
        int flags
    )
    {
        void* fallbackFn = (delegate* managed<void*, nuint, void*, nuint, uint*, nuint>)(
            &HUF_decompress4X2_usingDTable_internal_default
        );
        void* loopFn = (delegate* managed<HufDecompressFastArgs*, void>)(
            &HUF_decompress4X2_usingDTable_internal_fast_c_loop
        );
        if ((flags & (int)HufFlagsE.HufFlagsDisableFast) == 0)
        {
            var ret = HUF_decompress4X2_usingDTable_internal_fast(
                dst,
                dstSize,
                cSrc,
                cSrcSize,
                dTable,
                loopFn
            );
            if (ret != 0)
                return ret;
        }

        return ((delegate* managed<void*, nuint, void*, nuint, uint*, nuint>)fallbackFn)(
            dst,
            dstSize,
            cSrc,
            cSrcSize,
            dTable
        );
    }

    private static nuint HUF_decompress1X2_usingDTable_internal(
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        uint* dTable,
        // ReSharper disable once UnusedParameter.Local
        int flags
    )
    {
        return HUF_decompress1X2_usingDTable_internal_body(dst, dstSize, cSrc, cSrcSize, dTable);
    }

    private static nuint HUF_decompress1X2_DCtx_wksp(
        uint* dCtx,
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        void* workSpace,
        nuint wkspSize,
        int flags
    )
    {
        var ip = (byte*)cSrc;
        var hSize = HUF_readDTableX2_wksp(dCtx, cSrc, cSrcSize, workSpace, wkspSize, flags);
        if (ERR_isError(hSize))
            return hSize;
        if (hSize >= cSrcSize)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
        ip += hSize;
        cSrcSize -= hSize;
        return HUF_decompress1X2_usingDTable_internal(dst, dstSize, ip, cSrcSize, dCtx, flags);
    }

    private static nuint HUF_decompress4X2_DCtx_wksp(
        uint* dctx,
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        void* workSpace,
        nuint wkspSize,
        int flags
    )
    {
        var ip = (byte*)cSrc;
        var hSize = HUF_readDTableX2_wksp(dctx, cSrc, cSrcSize, workSpace, wkspSize, flags);
        if (ERR_isError(hSize))
            return hSize;
        if (hSize >= cSrcSize)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
        ip += hSize;
        cSrcSize -= hSize;
        return HUF_decompress4X2_usingDTable_internal(dst, dstSize, ip, cSrcSize, dctx, flags);
    }

    /*
     * HUF_selectDecoder() :
     * Tells which decoder is likely to decode faster,
     * based on a set of pre-computed metrics.
     * @return : 0==HUF_decompress4X1, 1==HUF_decompress4X2 .
     * Assumption : 0
     * < dstSize
     * <
     * =
     * 1
     * 2
     * 8
     * KB
     */
    private static uint HUF_selectDecoder(nuint dstSize, nuint cSrcSize)
    {
        assert(dstSize > 0);
        assert(dstSize <= 128 * 1024);
        {
            /* Q < 16 */
            var q = cSrcSize >= dstSize ? 15 : (uint)(cSrcSize * 16 / dstSize);
            var d256 = (uint)(dstSize >> 8);
            var dTime0 = AlgoTime[q][0].tableTime + AlgoTime[q][0].decode256Time * d256;
            var dTime1 = AlgoTime[q][1].tableTime + AlgoTime[q][1].decode256Time * d256;
            dTime1 += dTime1 >> 5;
            return dTime1 < dTime0 ? 1U : 0U;
        }
    }

    // ReSharper disable once UnusedMember.Local
    private static nuint HUF_decompress1X_DCtx_wksp(
        uint* dctx,
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        void* workSpace,
        nuint wkspSize,
        int flags
    )
    {
        if (dstSize == 0)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));
        if (cSrcSize > dstSize)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
        if (cSrcSize == dstSize)
        {
            memcpy(dst, cSrc, (uint)dstSize);
            return dstSize;
        }

        if (cSrcSize == 1)
        {
            memset(dst, *(byte*)cSrc, (uint)dstSize);
            return dstSize;
        }

        {
            var algoNb = HUF_selectDecoder(dstSize, cSrcSize);
            return algoNb != 0
                ? HUF_decompress1X2_DCtx_wksp(
                    dctx,
                    dst,
                    dstSize,
                    cSrc,
                    cSrcSize,
                    workSpace,
                    wkspSize,
                    flags
                )
                : HUF_decompress1X1_DCtx_wksp(
                    dctx,
                    dst,
                    dstSize,
                    cSrc,
                    cSrcSize,
                    workSpace,
                    wkspSize,
                    flags
                );
        }
    }

    /* BMI2 variants.
     * If the CPU has BMI2 support, pass bmi2=1, otherwise pass bmi2=0.
     */
    private static nuint HUF_decompress1X_usingDTable(
        void* dst,
        nuint maxDstSize,
        void* cSrc,
        nuint cSrcSize,
        uint* dTable,
        int flags
    )
    {
        var dtd = HUF_getDTableDesc(dTable);
        return dtd.tableType != 0
            ? HUF_decompress1X2_usingDTable_internal(dst, maxDstSize, cSrc, cSrcSize, dTable, flags)
            : HUF_decompress1X1_usingDTable_internal(
                dst,
                maxDstSize,
                cSrc,
                cSrcSize,
                dTable,
                flags
            );
    }

    private static nuint HUF_decompress1X1_DCtx_wksp(
        uint* dctx,
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        void* workSpace,
        nuint wkspSize,
        int flags
    )
    {
        var ip = (byte*)cSrc;
        var hSize = HUF_readDTableX1_wksp(dctx, cSrc, cSrcSize, workSpace, wkspSize, flags);
        if (ERR_isError(hSize))
            return hSize;
        if (hSize >= cSrcSize)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
        ip += hSize;
        cSrcSize -= hSize;
        return HUF_decompress1X1_usingDTable_internal(dst, dstSize, ip, cSrcSize, dctx, flags);
    }

    private static nuint HUF_decompress4X_usingDTable(
        void* dst,
        nuint maxDstSize,
        void* cSrc,
        nuint cSrcSize,
        uint* dTable,
        int flags
    )
    {
        var dtd = HUF_getDTableDesc(dTable);
        return dtd.tableType != 0
            ? HUF_decompress4X2_usingDTable_internal(dst, maxDstSize, cSrc, cSrcSize, dTable, flags)
            : HUF_decompress4X1_usingDTable_internal(
                dst,
                maxDstSize,
                cSrc,
                cSrcSize,
                dTable,
                flags
            );
    }

    private static nuint HUF_decompress4X_hufOnly_wksp(
        uint* dctx,
        void* dst,
        nuint dstSize,
        void* cSrc,
        nuint cSrcSize,
        void* workSpace,
        nuint wkspSize,
        int flags
    )
    {
        if (dstSize == 0)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));
        if (cSrcSize == 0)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
        {
            var algoNb = HUF_selectDecoder(dstSize, cSrcSize);
            return algoNb != 0
                ? HUF_decompress4X2_DCtx_wksp(
                    dctx,
                    dst,
                    dstSize,
                    cSrc,
                    cSrcSize,
                    workSpace,
                    wkspSize,
                    flags
                )
                : HUF_decompress4X1_DCtx_wksp(
                    dctx,
                    dst,
                    dstSize,
                    cSrc,
                    cSrcSize,
                    workSpace,
                    wkspSize,
                    flags
                );
        }
    }
}