using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using static VendoredZSTD.UnsafeHelper;

namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
#if NET7_0_OR_GREATER
    private static ReadOnlySpan<uint> SpanBitMask => new uint[]
    {
        0,
        1,
        3,
        7,
        0xF,
        0x1F,
        0x3F,
        0x7F,
        0xFF,
        0x1FF,
        0x3FF,
        0x7FF,
        0xFFF,
        0x1FFF,
        0x3FFF,
        0x7FFF,
        0xFFFF,
        0x1FFFF,
        0x3FFFF,
        0x7FFFF,
        0xFFFFF,
        0x1FFFFF,
        0x3FFFFF,
        0x7FFFFF,
        0xFFFFFF,
        0x1FFFFFF,
        0x3FFFFFF,
        0x7FFFFFF,
        0xFFFFFFF,
        0x1FFFFFFF,
        0x3FFFFFFF,
        0x7FFFFFFF
    };

    private static uint* BitMask => (uint*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref MemoryMarshal.GetReference(SpanBitMask));
#else

        private static readonly uint* BIT_mask = GetArrayPointer(new uint[32] { 0, 1, 3, 7, 0xF, 0x1F, 0x3F, 0x7F, 0xFF, 0x1FF, 0x3FF, 0x7FF, 0xFFF, 0x1FFF, 0x3FFF, 0x7FFF, 0xFFFF, 0x1FFFF, 0x3FFFF, 0x7FFFF, 0xFFFFF, 0x1FFFFF, 0x3FFFFF, 0x7FFFFF, 0xFFFFFF, 0x1FFFFFF, 0x3FFFFFF, 0x7FFFFFF, 0xFFFFFFF, 0x1FFFFFFF, 0x3FFFFFFF, 0x7FFFFFFF });
#endif
    /*-**************************************************************
     *  bitStream encoding
     ****************************************************************/
    /*! BIT_initCStream() :
     *  `dstCapacity` must be > sizeof(size_t)
     *  @return : 0 if success,
     *            otherwise an error code (can be tested using ERR_isError()) */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint BIT_initCStream(ref BitCStreamT bitC, void* startPtr, nuint dstCapacity)
    {
        bitC.bitContainer = 0;
        bitC.bitPos = 0;
        bitC.startPtr = (sbyte*)startPtr;
        bitC.ptr = bitC.startPtr;
        bitC.endPtr = bitC.startPtr + dstCapacity - sizeof(nuint);
        if (dstCapacity <= (nuint)sizeof(nuint))
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));

        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint BIT_getLowerBits(nuint bitContainer, uint nbBits)
    {
        assert(nbBits < sizeof(uint) * 32 / sizeof(uint));
#if NETCOREAPP3_1_OR_GREATER
        if (Bmi2.X64.IsSupported)
        {
            return (nuint)Bmi2.X64.ZeroHighBits(bitContainer, nbBits);
        }

        if (Bmi2.IsSupported)
        {
            return Bmi2.ZeroHighBits((uint)bitContainer, nbBits);
        }
#endif

        return bitContainer & BitMask[nbBits];
    }

    /*! BIT_addBits() :
     *  can add up to 31 bits into `bitC`.
     *  Note : does not check for register overflow ! */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BIT_addBits(ref nuint bitCBitContainer, ref uint bitCBitPos, nuint value, uint nbBits)
    {
        assert(nbBits < sizeof(uint) * 32 / sizeof(uint));
        assert(nbBits + bitCBitPos < (uint)(sizeof(nuint) * 8));
        bitCBitContainer |= BIT_getLowerBits(value, nbBits) << (int)bitCBitPos;
        bitCBitPos += nbBits;
    }

    /*! BIT_addBitsFast() :
     *  works only if `value` is _clean_,
     *  meaning all high bits above nbBits are 0 */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BIT_addBitsFast(ref nuint bitCBitContainer, ref uint bitCBitPos, nuint value, uint nbBits)
    {
        assert(value >> (int)nbBits == 0);
        assert(nbBits + bitCBitPos < (uint)(sizeof(nuint) * 8));
        bitCBitContainer |= value << (int)bitCBitPos;
        bitCBitPos += nbBits;
    }

    /*! BIT_flushBitsFast() :
     *  assumption : bitContainer has not overflowed
     *  unsafe version; does not check buffer overflow */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BIT_flushBitsFast(ref nuint bitCBitContainer, ref uint bitCBitPos, ref sbyte* bitCPtr, sbyte* bitCEndPtr)
    {
        nuint nbBytes = bitCBitPos >> 3;
        assert(bitCBitPos < (uint)(sizeof(nuint) * 8));
        assert(bitCPtr <= bitCEndPtr);
        MEM_writeLEST(bitCPtr, bitCBitContainer);
        bitCPtr += nbBytes;
        bitCBitPos &= 7;
        bitCBitContainer >>= (int)(nbBytes * 8);
    }

    /*! BIT_flushBits() :
     *  assumption : bitContainer has not overflowed
     *  safe version; check for buffer overflow, and prevents it.
     *  note : does not signal buffer overflow.
     *  overflow will be revealed later on using BIT_closeCStream() */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BIT_flushBits(ref nuint bitCBitContainer, ref uint bitCBitPos, ref sbyte* bitCPtr, sbyte* bitCEndPtr)
    {
        nuint nbBytes = bitCBitPos >> 3;
        assert(bitCBitPos < (uint)(sizeof(nuint) * 8));
        assert(bitCPtr <= bitCEndPtr);
        MEM_writeLEST(bitCPtr, bitCBitContainer);
        bitCPtr += nbBytes;
        if (bitCPtr > bitCEndPtr)
        {
            bitCPtr = bitCEndPtr;
        }

        bitCBitPos &= 7;
        bitCBitContainer >>= (int)(nbBytes * 8);
    }

    /*! BIT_closeCStream() :
     *  @return : size of CStream, in bytes,
     *            or 0 if it could not fit into dstBuffer */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint BIT_closeCStream(ref nuint bitCBitContainer, ref uint bitCBitPos, sbyte* bitCPtr, sbyte* bitCEndPtr, sbyte* bitCStartPtr)
    {
        BIT_addBitsFast(ref bitCBitContainer, ref bitCBitPos, 1, 1);
        BIT_flushBits(ref bitCBitContainer, ref bitCBitPos, ref bitCPtr, bitCEndPtr);
        if (bitCPtr >= bitCEndPtr)
            return 0;

        return (nuint)(bitCPtr - bitCStartPtr) + (nuint)(bitCBitPos > 0 ? 1 : 0);
    }

    /*-********************************************************
     *  bitStream decoding
     **********************************************************/
    /*! BIT_initDStream() :
     *  Initialize a BIT_DStream_t.
     * `bitD` : a pointer to an already allocated BIT_DStream_t structure.
     * `srcSize` must be the *exact* size of the bitStream, in bytes.
     * @return : size of stream (== srcSize), or an errorCode if a problem is detected
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint BIT_initDStream(BitDStreamT* bitD, void* srcBuffer, nuint srcSize)
    {
        if (srcSize < 1)
        {
            *bitD = new BitDStreamT();
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
        }

        bitD->start = (sbyte*)srcBuffer;
        bitD->limitPtr = bitD->start + sizeof(nuint);
        if (srcSize >= (nuint)sizeof(nuint))
        {
            bitD->ptr = (sbyte*)srcBuffer + srcSize - sizeof(nuint);
            bitD->bitContainer = MEM_readLEST(bitD->ptr);
            {
                var lastByte = ((byte*)srcBuffer)[srcSize - 1];
                bitD->bitsConsumed = lastByte != 0 ? 8 - ZSTD_highbit32(lastByte) : 0;
                if (lastByte == 0)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorGeneric));
            }
        }
        else
        {
            bitD->ptr = bitD->start;
            bitD->bitContainer = *(byte*)bitD->start;
            switch (srcSize)
            {
                case 7:
                    bitD->bitContainer += (nuint)((byte*)srcBuffer)[6] << (sizeof(nuint) * 8 - 16);
                    goto case 6;
                case 6:
                    bitD->bitContainer += (nuint)((byte*)srcBuffer)[5] << (sizeof(nuint) * 8 - 24);
                    goto case 5;
                case 5:
                    bitD->bitContainer += (nuint)((byte*)srcBuffer)[4] << (sizeof(nuint) * 8 - 32);
                    goto case 4;
                case 4:
                    bitD->bitContainer += (nuint)((byte*)srcBuffer)[3] << 24;
                    goto case 3;
                case 3:
                    bitD->bitContainer += (nuint)((byte*)srcBuffer)[2] << 16;
                    goto case 2;
                case 2:
                    bitD->bitContainer += (nuint)((byte*)srcBuffer)[1] << 8;
                    goto default;
                default:
                    break;
            }

            {
                var lastByte = ((byte*)srcBuffer)[srcSize - 1];
                bitD->bitsConsumed = lastByte != 0 ? 8 - ZSTD_highbit32(lastByte) : 0;
                if (lastByte == 0)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            }

            bitD->bitsConsumed += (uint)((nuint)sizeof(nuint) - srcSize) * 8;
        }

        return srcSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint BIT_getUpperBits(nuint bitContainer, uint start)
    {
        return bitContainer >> (int)start;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint BIT_getMiddleBits(nuint bitContainer, uint start, uint nbBits)
    {
        var regMask = (uint)(sizeof(nuint) * 8 - 1);
        assert(nbBits < sizeof(uint) * 32 / sizeof(uint));
#if NETCOREAPP3_1_OR_GREATER
        if (Bmi2.X64.IsSupported)
        {
            return (nuint)Bmi2.X64.ZeroHighBits(bitContainer >> (int)(start & regMask), nbBits);
        }

        if (Bmi2.IsSupported)
        {
            return Bmi2.ZeroHighBits((uint)(bitContainer >> (int)(start & regMask)), nbBits);
        }
#endif

        return (nuint)((bitContainer >> (int)(start & regMask)) & (((ulong)1 << (int)nbBits) - 1));
    }

    /*! BIT_lookBits() :
     *  Provides next n bits from local register.
     *  local register is not modified.
     *  On 32-bits, maxNbBits==24.
     *  On 64-bits, maxNbBits==56.
     * @return : value extracted */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint BIT_lookBits(BitDStreamT* bitD, uint nbBits)
    {
        return BIT_getMiddleBits(bitD->bitContainer, (uint)(sizeof(nuint) * 8) - bitD->bitsConsumed - nbBits, nbBits);
    }

    /*! BIT_lookBitsFast() :
     *  unsafe version; only works if nbBits >= 1 */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [InlineMethod.Inline]
    private static nuint BIT_lookBitsFast(BitDStreamT* bitD, uint nbBits)
    {
        var regMask = (uint)(sizeof(nuint) * 8 - 1);
        assert(nbBits >= 1);
        return (bitD->bitContainer << (int)(bitD->bitsConsumed & regMask)) >> (int)((regMask + 1 - nbBits) & regMask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [InlineMethod.Inline]
    private static void BIT_skipBits(BitDStreamT* bitD, uint nbBits)
    {
        bitD->bitsConsumed += nbBits;
    }

    /*! BIT_readBits() :
     *  Read (consume) next n bits from local register and update.
     *  Pay attention to not read more than nbBits contained into local register.
     * @return : extracted value. */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint BIT_readBits(BitDStreamT* bitD, uint nbBits)
    {
        var value = BIT_lookBits(bitD, nbBits);
        BIT_skipBits(bitD, nbBits);
        return value;
    }

    /*! BIT_readBitsFast() :
     *  unsafe version; only works if nbBits >= 1 */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint BIT_readBitsFast(BitDStreamT* bitD, uint nbBits)
    {
        var value = BIT_lookBitsFast(bitD, nbBits);
        assert(nbBits >= 1);
        BIT_skipBits(bitD, nbBits);
        return value;
    }

    /*! BIT_reloadDStream_internal() :
     *  Simple variant of BIT_reloadDStream(), with two conditions:
     *  1. bitstream is valid : bitsConsumed <= sizeof(bitD->bitContainer)*8
     *  2. look window is valid after shifted down : bitD->ptr >= bitD->start
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BitDStreamStatus BIT_reloadDStream_internal(BitDStreamT* bitD)
    {
        assert(bitD->bitsConsumed <= (uint)(sizeof(nuint) * 8));
        bitD->ptr -= bitD->bitsConsumed >> 3;
        assert(bitD->ptr >= bitD->start);
        bitD->bitsConsumed &= 7;
        bitD->bitContainer = MEM_readLEST(bitD->ptr);
        return BitDStreamStatus.BitDStreamUnfinished;
    }

    /*! BIT_reloadDStreamFast() :
     *  Similar to BIT_reloadDStream(), but with two differences:
     *  1. bitsConsumed <= sizeof(bitD->bitContainer)*8 must hold!
     *  2. Returns BIT_DStream_overflow when bitD->ptr < bitD->limitPtr, at this
     *     point you must use BIT_reloadDStream() to reload.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [InlineMethod.Inline]
    private static BitDStreamStatus BIT_reloadDStreamFast(BitDStreamT* bitD)
    {
        if (bitD->ptr < bitD->limitPtr)
            return BitDStreamStatus.BitDStreamOverflow;

        return BIT_reloadDStream_internal(bitD);
    }

#if NET7_0_OR_GREATER
    private static ReadOnlySpan<byte> SpanStaticZeroFilled => "\0\0\0\0\0\0\0\0"u8;

    private static nuint* StaticZeroFilled => (nuint*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref MemoryMarshal.GetReference(SpanStaticZeroFilled));
#else

        private static readonly nuint* static_zeroFilled = (nuint*)GetArrayPointer(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });
#endif
    /*! BIT_reloadDStream() :
     *  Refill `bitD` from buffer previously set in BIT_initDStream() .
     *  This function is safe, it guarantees it will not never beyond src buffer.
     * @return : status of `BIT_DStream_t` internal register.
     *           when status == BIT_DStream_unfinished, internal register is filled with at least 25 or 57 bits */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BitDStreamStatus BIT_reloadDStream(BitDStreamT* bitD)
    {
        if (bitD->bitsConsumed > (uint)(sizeof(nuint) * 8))
        {
            bitD->ptr = (sbyte*)&StaticZeroFilled[0];
            return BitDStreamStatus.BitDStreamOverflow;
        }

        assert(bitD->ptr >= bitD->start);
        if (bitD->ptr >= bitD->limitPtr)
        {
            return BIT_reloadDStream_internal(bitD);
        }

        if (bitD->ptr == bitD->start)
        {
            if (bitD->bitsConsumed < (uint)(sizeof(nuint) * 8))
                return BitDStreamStatus.BitDStreamEndOfBuffer;

            return BitDStreamStatus.BitDStreamCompleted;
        }

        {
            var nbBytes = bitD->bitsConsumed >> 3;
            var result = BitDStreamStatus.BitDStreamUnfinished;
            if (bitD->ptr - nbBytes < bitD->start)
            {
                nbBytes = (uint)(bitD->ptr - bitD->start);
                result = BitDStreamStatus.BitDStreamEndOfBuffer;
            }

            bitD->ptr -= nbBytes;
            bitD->bitsConsumed -= nbBytes * 8;
            bitD->bitContainer = MEM_readLEST(bitD->ptr);
            return result;
        }
    }

    /*! BIT_endOfDStream() :
     * @return : 1 if DStream has _exactly_ reached its end (all bits consumed).
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint BIT_endOfDStream(BitDStreamT* dStream)
    {
        return dStream->ptr == dStream->start && dStream->bitsConsumed == (uint)(sizeof(nuint) * 8) ? 1U : 0U;
    }

    /*-********************************************************
     *  bitStream decoding
     **********************************************************/
    /*! BIT_initDStream() :
     *  Initialize a BIT_DStream_t.
     * `bitD` : a pointer to an already allocated BIT_DStream_t structure.
     * `srcSize` must be the *exact* size of the bitStream, in bytes.
     * @return : size of stream (== srcSize), or an errorCode if a problem is detected
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint BIT_initDStream(ref BitDStreamT bitD, void* srcBuffer, nuint srcSize)
    {
        if (srcSize < 1)
        {
            bitD = new BitDStreamT();
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
        }

        bitD.start = (sbyte*)srcBuffer;
        bitD.limitPtr = bitD.start + sizeof(nuint);
        if (srcSize >= (nuint)sizeof(nuint))
        {
            bitD.ptr = (sbyte*)srcBuffer + srcSize - sizeof(nuint);
            bitD.bitContainer = MEM_readLEST(bitD.ptr);
            {
                var lastByte = ((byte*)srcBuffer)[srcSize - 1];
                bitD.bitsConsumed = lastByte != 0 ? 8 - ZSTD_highbit32(lastByte) : 0;
                if (lastByte == 0)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorGeneric));
            }
        }
        else
        {
            bitD.ptr = bitD.start;
            bitD.bitContainer = *(byte*)bitD.start;
            switch (srcSize)
            {
                case 7:
                    bitD.bitContainer += (nuint)((byte*)srcBuffer)[6] << (sizeof(nuint) * 8 - 16);
                    goto case 6;
                case 6:
                    bitD.bitContainer += (nuint)((byte*)srcBuffer)[5] << (sizeof(nuint) * 8 - 24);
                    goto case 5;
                case 5:
                    bitD.bitContainer += (nuint)((byte*)srcBuffer)[4] << (sizeof(nuint) * 8 - 32);
                    goto case 4;
                case 4:
                    bitD.bitContainer += (nuint)((byte*)srcBuffer)[3] << 24;
                    goto case 3;
                case 3:
                    bitD.bitContainer += (nuint)((byte*)srcBuffer)[2] << 16;
                    goto case 2;
                case 2:
                    bitD.bitContainer += (nuint)((byte*)srcBuffer)[1] << 8;
                    goto default;
                default:
                    break;
            }

            {
                var lastByte = ((byte*)srcBuffer)[srcSize - 1];
                bitD.bitsConsumed = lastByte != 0 ? 8 - ZSTD_highbit32(lastByte) : 0;
                if (lastByte == 0)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
            }

            bitD.bitsConsumed += (uint)((nuint)sizeof(nuint) - srcSize) * 8;
        }

        return srcSize;
    }

    /*! BIT_lookBits() :
     *  Provides next n bits from local register.
     *  local register is not modified.
     *  On 32-bits, maxNbBits==24.
     *  On 64-bits, maxNbBits==56.
     * @return : value extracted */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint BIT_lookBits(nuint bitDBitContainer, uint bitDBitsConsumed, uint nbBits)
    {
        return BIT_getMiddleBits(bitDBitContainer, (uint)(sizeof(nuint) * 8) - bitDBitsConsumed - nbBits, nbBits);
    }

    /*! BIT_lookBitsFast() :
     *  unsafe version; only works if nbBits >= 1 */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [InlineMethod.Inline]
    private static nuint BIT_lookBitsFast(nuint bitDBitContainer, uint bitDBitsConsumed, uint nbBits)
    {
        var regMask = (uint)(sizeof(nuint) * 8 - 1);
        assert(nbBits >= 1);
        return (bitDBitContainer << (int)(bitDBitsConsumed & regMask)) >> (int)((regMask + 1 - nbBits) & regMask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [InlineMethod.Inline]
    private static void BIT_skipBits(ref uint bitDBitsConsumed, uint nbBits)
    {
        bitDBitsConsumed += nbBits;
    }

    /*! BIT_readBits() :
     *  Read (consume) next n bits from local register and update.
     *  Pay attention to not read more than nbBits contained into local register.
     * @return : extracted value. */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint BIT_readBits(nuint bitDBitContainer, ref uint bitDBitsConsumed, uint nbBits)
    {
        var value = BIT_lookBits(bitDBitContainer, bitDBitsConsumed, nbBits);
        BIT_skipBits(ref bitDBitsConsumed, nbBits);
        return value;
    }

    /*! BIT_readBitsFast() :
     *  unsafe version; only works if nbBits >= 1 */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint BIT_readBitsFast(nuint bitDBitContainer, ref uint bitDBitsConsumed, uint nbBits)
    {
        var value = BIT_lookBitsFast(bitDBitContainer, bitDBitsConsumed, nbBits);
        assert(nbBits >= 1);
        BIT_skipBits(ref bitDBitsConsumed, nbBits);
        return value;
    }

    /*! BIT_reloadDStreamFast() :
     *  Similar to BIT_reloadDStream(), but with two differences:
     *  1. bitsConsumed <= sizeof(bitD->bitContainer)*8 must hold!
     *  2. Returns BIT_DStream_overflow when bitD->ptr < bitD->limitPtr, at this
     *     point you must use BIT_reloadDStream() to reload.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [InlineMethod.Inline]
    private static BitDStreamStatus BIT_reloadDStreamFast(ref nuint bitDBitContainer, ref uint bitDBitsConsumed, ref sbyte* bitDPtr, sbyte* bitDStart, sbyte* bitDLimitPtr)
    {
        if (bitDPtr < bitDLimitPtr)
            return BitDStreamStatus.BitDStreamOverflow;

        return BIT_reloadDStream_internal(ref bitDBitContainer, ref bitDBitsConsumed, ref bitDPtr, bitDStart);
    }

    /*! BIT_reloadDStream() :
     *  Refill `bitD` from buffer previously set in BIT_initDStream() .
     *  This function is safe, it guarantees it will not never beyond src buffer.
     * @return : status of `BIT_DStream_t` internal register.
     *           when status == BIT_DStream_unfinished, internal register is filled with at least 25 or 57 bits */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BitDStreamStatus BIT_reloadDStream(ref nuint bitDBitContainer, ref uint bitDBitsConsumed, ref sbyte* bitDPtr, sbyte* bitDStart, sbyte* bitDLimitPtr)
    {
        if (bitDBitsConsumed > (uint)(sizeof(nuint) * 8))
        {
            bitDPtr = (sbyte*)&StaticZeroFilled[0];
            return BitDStreamStatus.BitDStreamOverflow;
        }

        assert(bitDPtr >= bitDStart);
        if (bitDPtr >= bitDLimitPtr)
        {
            return BIT_reloadDStream_internal(ref bitDBitContainer, ref bitDBitsConsumed, ref bitDPtr, bitDStart);
        }

        if (bitDPtr == bitDStart)
        {
            if (bitDBitsConsumed < (uint)(sizeof(nuint) * 8))
                return BitDStreamStatus.BitDStreamEndOfBuffer;

            return BitDStreamStatus.BitDStreamCompleted;
        }

        {
            var nbBytes = bitDBitsConsumed >> 3;
            var result = BitDStreamStatus.BitDStreamUnfinished;
            if (bitDPtr - nbBytes < bitDStart)
            {
                nbBytes = (uint)(bitDPtr - bitDStart);
                result = BitDStreamStatus.BitDStreamEndOfBuffer;
            }

            bitDPtr -= nbBytes;
            bitDBitsConsumed -= nbBytes * 8;
            bitDBitContainer = MEM_readLEST(bitDPtr);
            return result;
        }
    }

    /*! BIT_reloadDStream_internal() :
     *  Simple variant of BIT_reloadDStream(), with two conditions:
     *  1. bitstream is valid : bitsConsumed <= sizeof(bitD->bitContainer)*8
     *  2. look window is valid after shifted down : bitD->ptr >= bitD->start
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BitDStreamStatus BIT_reloadDStream_internal(ref nuint bitDBitContainer, ref uint bitDBitsConsumed, ref sbyte* bitDPtr, sbyte* bitDStart)
    {
        assert(bitDBitsConsumed <= (uint)(sizeof(nuint) * 8));
        bitDPtr -= bitDBitsConsumed >> 3;
        assert(bitDPtr >= bitDStart);
        bitDBitsConsumed &= 7;
        bitDBitContainer = MEM_readLEST(bitDPtr);
        return BitDStreamStatus.BitDStreamUnfinished;
    }

    /*! BIT_endOfDStream() :
     * @return : 1 if DStream has _exactly_ reached its end (all bits consumed).
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint BIT_endOfDStream(uint dStreamBitsConsumed, sbyte* dStreamPtr, sbyte* dStreamStart)
    {
        return dStreamPtr == dStreamStart && dStreamBitsConsumed == (uint)(sizeof(nuint) * 8) ? 1U : 0U;
    }
}