using System.Runtime.CompilerServices;
using static VendoredZSTD.UnsafeHelper;

namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
    /*===   Version   ===*/
    private static uint FSE_versionNumber()
    {
        return 0 * 100 * 100 + 9 * 100 + 0;
    }

    /*===   Error Management   ===*/
    private static bool FSE_isError(nuint code)
    {
        return ERR_isError(code);
    }

    private static string FSE_getErrorName(nuint code)
    {
        return ERR_getErrorName(code);
    }

    /* Error Management */
    private static bool HUF_isError(nuint code)
    {
        return ERR_isError(code);
    }

    private static string HUF_getErrorName(nuint code)
    {
        return ERR_getErrorName(code);
    }

    /*-**************************************************************
     *  FSE NCount encoding-decoding
     ****************************************************************/
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint FSE_readNCount_body(short* normalizedCounter, uint* maxSvPtr, uint* tableLogPtr, void* headerBuffer, nuint hbSize)
    {
        var istart = (byte*)headerBuffer;
        var iend = istart + hbSize;
        var ip = istart;
        uint charnum = 0;
        var maxSv1 = *maxSvPtr + 1;
        var previous0 = 0;
        if (hbSize < 8)
        {
            var buffer = stackalloc sbyte[8];
            /* This function only works when hbSize >= 8 */
            memset(buffer, 0, sizeof(sbyte) * 8);
            memcpy(buffer, headerBuffer, (uint)hbSize);
            {
                var countSize = FSE_readNCount(normalizedCounter, maxSvPtr, tableLogPtr, buffer, sizeof(sbyte) * 8);
                if (FSE_isError(countSize))
                    return countSize;
                if (countSize > hbSize)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

                return countSize;
            }
        }

        assert(hbSize >= 8);
        memset(normalizedCounter, 0, (*maxSvPtr + 1) * sizeof(short));
        var bitStream = MEM_readLE32(ip);
        var nbBits = (int)((bitStream & 0xF) + 5);
        if (nbBits > 15)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorTableLogTooLarge));

        bitStream >>= 4;
        var bitCount = 4;
        *tableLogPtr = (uint)nbBits;
        var remaining = (1 << nbBits) + 1;
        var threshold = 1 << nbBits;
        nbBits++;
        for (;;)
        {
            if (previous0 != 0)
            {
                /* Count the number of repeats. Each time the
                 * 2-bit repeat code is 0b11 there is another
                 * repeat.
                 * Avoid UB by setting the high bit to 1.
                 */
                var repeats = (int)(ZSTD_countTrailingZeros32(~bitStream | 0x80000000) >> 1);
                while (repeats >= 12)
                {
                    charnum += 3 * 12;
                    if (ip <= iend - 7)
                    {
                        ip += 3;
                    }
                    else
                    {
                        bitCount -= (int)(8 * (iend - 7 - ip));
                        bitCount &= 31;
                        ip = iend - 4;
                    }

                    bitStream = MEM_readLE32(ip) >> bitCount;
                    repeats = (int)(ZSTD_countTrailingZeros32(~bitStream | 0x80000000) >> 1);
                }

                charnum += (uint)(3 * repeats);
                bitStream >>= 2 * repeats;
                bitCount += 2 * repeats;
                assert((bitStream & 3) < 3);
                charnum += bitStream & 3;
                bitCount += 2;
                if (charnum >= maxSv1)
                    break;

                if (ip <= iend - 7 || ip + (bitCount >> 3) <= iend - 4)
                {
                    assert(bitCount >> 3 <= 3);
                    ip += bitCount >> 3;
                    bitCount &= 7;
                }
                else
                {
                    bitCount -= (int)(8 * (iend - 4 - ip));
                    bitCount &= 31;
                    ip = iend - 4;
                }

                bitStream = MEM_readLE32(ip) >> bitCount;
            }

            {
                var max = 2 * threshold - 1 - remaining;
                int count;
                if ((bitStream & (uint)(threshold - 1)) < (uint)max)
                {
                    count = (int)(bitStream & (uint)(threshold - 1));
                    bitCount += nbBits - 1;
                }
                else
                {
                    count = (int)(bitStream & (uint)(2 * threshold - 1));
                    if (count >= threshold)
                    {
                        count -= max;
                    }

                    bitCount += nbBits;
                }

                count--;
                if (count >= 0)
                {
                    remaining -= count;
                }
                else
                {
                    assert(count == -1);
                    remaining += count;
                }

                normalizedCounter[charnum++] = (short)count;
                previous0 = count == 0 ? 1 : 0;
                assert(threshold > 1);
                if (remaining < threshold)
                {
                    if (remaining <= 1)
                        break;

                    nbBits = (int)(ZSTD_highbit32((uint)remaining) + 1);
                    threshold = 1 << (nbBits - 1);
                }

                if (charnum >= maxSv1)
                    break;

                if (ip <= iend - 7 || ip + (bitCount >> 3) <= iend - 4)
                {
                    ip += bitCount >> 3;
                    bitCount &= 7;
                }
                else
                {
                    bitCount -= (int)(8 * (iend - 4 - ip));
                    bitCount &= 31;
                    ip = iend - 4;
                }

                bitStream = MEM_readLE32(ip) >> bitCount;
            }
        }

        if (remaining != 1)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));
        if (charnum > maxSv1)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMaxSymbolValueTooSmall));
        if (bitCount > 32)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

        *maxSvPtr = charnum - 1;
        ip += (bitCount + 7) >> 3;
        return (nuint)(ip - istart);
    }

    /* Avoids the FORCE_INLINE of the _body() function. */
    private static nuint FSE_readNCount_body_default(short* normalizedCounter, uint* maxSvPtr, uint* tableLogPtr, void* headerBuffer, nuint hbSize)
    {
        return FSE_readNCount_body(normalizedCounter, maxSvPtr, tableLogPtr, headerBuffer, hbSize);
    }

    /*! FSE_readNCount_bmi2():
     * Same as FSE_readNCount() but pass bmi2=1 when your CPU supports BMI2 and 0 otherwise.
     */
    private static nuint FSE_readNCount_bmi2(short* normalizedCounter, uint* maxSvPtr, uint* tableLogPtr,
        void* headerBuffer, nuint hbSize, int bmi2)
    {
        // ReSharper disable once UnusedParameter
        return FSE_readNCount_body_default(normalizedCounter, maxSvPtr, tableLogPtr, headerBuffer, hbSize);
    }

    /*! FSE_readNCount():
    Read compactly saved 'normalizedCounter' from 'rBuffer'.
    @return : size read from 'rBuffer',
    or an errorCode, which can be tested using FSE_isError().
    maxSymbolValuePtr[0] and tableLogPtr[0] will also be updated with their respective values */
    private static nuint FSE_readNCount(short* normalizedCounter, uint* maxSvPtr, uint* tableLogPtr, void* headerBuffer, nuint hbSize)
    {
        return FSE_readNCount_bmi2(normalizedCounter, maxSvPtr, tableLogPtr, headerBuffer, hbSize, 0);
    }

    /*! HUF_readStats() :
    Read compact Huffman tree, saved by HUF_writeCTable().
    `huffWeight` is destination buffer.
    `rankStats` is assumed to be a table of at least HUF_TABLELOG_MAX U32.
    @return : size read from `src` , or an error Code .
    Note : Needed by HUF_readCTable() and HUF_readDTableX?() .
     */
    private static nuint HUF_readStats(byte* huffWeight, nuint hwSize, uint* rankStats, uint* nbSymbolsPtr, uint* tableLogPtr, void* src, nuint srcSize)
    {
        var wksp = stackalloc uint[219];
        return HUF_readStats_wksp(huffWeight, hwSize, rankStats, nbSymbolsPtr, tableLogPtr, src, srcSize, wksp, sizeof(uint) * 219, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint HUF_readStats_body(byte* huffWeight, nuint hwSize, uint* rankStats, uint* nbSymbolsPtr, uint* tableLogPtr, void* src, nuint srcSize, void* workSpace, nuint wkspSize, int bmi2)
    {
        var ip = (byte*)src;
        nuint oSize;
        if (srcSize == 0)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));

        nuint iSize = ip[0];
        if (iSize >= 128)
        {
            oSize = iSize - 127;
            iSize = (oSize + 1) / 2;
            if (iSize + 1 > srcSize)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));
            if (oSize >= hwSize)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            ip += 1;
            {
                uint n;
                for (n = 0; n < oSize; n += 2)
                {
                    huffWeight[n] = (byte)(ip[n / 2] >> 4);
                    huffWeight[n + 1] = (byte)(ip[n / 2] & 15);
                }
            }
        }
        else
        {
            if (iSize + 1 > srcSize)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorSrcSizeWrong));

            oSize = FSE_decompress_wksp_bmi2(huffWeight, hwSize - 1, ip + 1, iSize, 6, workSpace, wkspSize, bmi2);
            if (FSE_isError(oSize))
                return oSize;
        }

        memset(rankStats, 0, (12 + 1) * sizeof(uint));
        uint weightTotal = 0;
        {
            uint n;
            for (n = 0; n < oSize; n++)
            {
                if (huffWeight[n] > 12)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

                rankStats[huffWeight[n]]++;
                weightTotal += (uint)((1 << huffWeight[n]) >> 1);
            }
        }

        if (weightTotal == 0)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

        {
            var tableLog = ZSTD_highbit32(weightTotal) + 1;
            if (tableLog > 12)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

            *tableLogPtr = tableLog;
            {
                var total = (uint)(1 << (int)tableLog);
                var rest = total - weightTotal;
                var verif = (uint)(1 << (int)ZSTD_highbit32(rest));
                var lastWeight = ZSTD_highbit32(rest) + 1;
                if (verif != rest)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

                huffWeight[oSize] = (byte)lastWeight;
                rankStats[lastWeight]++;
            }
        }

        if (rankStats[1] < 2 || (rankStats[1] & 1) != 0)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorCorruptionDetected));

        *nbSymbolsPtr = (uint)(oSize + 1);
        return iSize + 1;
    }

    /* Avoids the FORCE_INLINE of the _body() function. */
    private static nuint HUF_readStats_body_default(byte* huffWeight, nuint hwSize, uint* rankStats, uint* nbSymbolsPtr, uint* tableLogPtr, void* src, nuint srcSize, void* workSpace, nuint wkspSize)
    {
        return HUF_readStats_body(huffWeight, hwSize, rankStats, nbSymbolsPtr, tableLogPtr, src, srcSize, workSpace, wkspSize, 0);
    }

    private static nuint HUF_readStats_wksp(byte* huffWeight, nuint hwSize, uint* rankStats, uint* nbSymbolsPtr, uint* tableLogPtr,
        void* src, nuint srcSize, void* workSpace, nuint wkspSize, int flags)
    {
        // ReSharper disable once UnusedParameter
        return HUF_readStats_body_default(huffWeight, hwSize, rankStats, nbSymbolsPtr, tableLogPtr, src, srcSize, workSpace, wkspSize);
    }
}