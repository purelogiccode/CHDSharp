namespace VendoredLZMA.LZ;

/// <summary>Binary-tree match finder for the LZMA encoder, ported from the LZMA SDK (public domain).</summary>
internal class BinTree : InWindow, IMatchFinder
{
    private const uint KHash2Size = 1 << 10;
    private const uint KHash3Size = 1 << 16;
    private const uint KBt2HashSize = 1 << 16;
    private const uint KStartMaxLen = 1;
    private const uint KHash3Offset = KHash2Size;
    private const uint KEmptyHashValue = 0;
    private const uint KMaxValForNormalize = ((uint)1 << 31) - 1;

    private uint _cutValue = 0xFF;
    private uint _cyclicBufferPos;
    private uint _cyclicBufferSize;
    private uint _fixHashSize = KHash2Size + KHash3Size;
    private uint[] _hash = [];

    private bool _hashArray = true;
    private uint _hashMask;
    private uint _hashSizeSum;
    private uint _matchMaxLen;
    private uint _minMatchCheck = 4;

    private uint _numHashDirectBytes;

    private uint[] _son = [];

    public new void SetStream(Stream stream)
    {
        base.SetStream(stream);
    }

    public new void ReleaseStream()
    {
        base.ReleaseStream();
    }

    public new void Init()
    {
        base.Init();
        for (uint i = 0; i < _hashSizeSum; i++)
            _hash[i] = KEmptyHashValue;

        _cyclicBufferPos = 0;
        ReduceOffsets(-1);
    }

    public new byte GetIndexByte(int index)
    {
        return base.GetIndexByte(index);
    }

    public new uint GetMatchLen(int index, uint distance, uint limit)
    {
        return base.GetMatchLen(index, distance, limit);
    }

    public new uint GetNumAvailableBytes()
    {
        return base.GetNumAvailableBytes();
    }

    public void Create(
        uint historySize,
        uint keepAddBufferBefore,
        uint matchMaxLen,
        uint keepAddBufferAfter
    )
    {
        Create(historySize, keepAddBufferBefore, matchMaxLen, keepAddBufferAfter, null);
    }

    public uint GetMatches(uint[] distances)
    {
        uint lenLimit;
        if (Pos + _matchMaxLen <= StreamPos)
        {
            lenLimit = _matchMaxLen;
        }
        else
        {
            lenLimit = StreamPos - Pos;
            if (lenLimit < _minMatchCheck)
            {
                MovePos();
                return 0;
            }
        }

        uint offset = 0;
        var matchMinPos = Pos > _cyclicBufferSize ? Pos - _cyclicBufferSize : 0;
        var cur = BufferOffset + Pos;
        var maxLen = _hashArray ? 3u : KStartMaxLen;
        uint hashValue,
            hash2Value = 0,
            hash3Value = 0;

        if (_hashArray)
        {
            var temp = Crc.Table[BufferBase[cur]] ^ BufferBase[cur + 1];
            hash2Value = temp & (KHash2Size - 1);
            temp ^= (uint)BufferBase[cur + 2] << 8;
            hash3Value = temp & (KHash3Size - 1);
            hashValue = (temp ^ (Crc.Table[BufferBase[cur + 3]] << 5)) & _hashMask;
        }
        else
        {
            hashValue = BufferBase[cur] ^ ((uint)BufferBase[cur + 1] << 8);
        }

        var curMatch = _hash[_fixHashSize + hashValue];
        if (_hashArray)
        {
            var d2 = Pos - _hash[hash2Value];
            var d3 = Pos - _hash[KHash3Offset + hash3Value];

            // C reference updates ALL hash tables before d2/d3 checks and SkipMatchesSpec.
            // The main hash must be updated here so that Skip(1) from the fast-path sees the
            // correct entry when the next position hashes to the same bucket.
            _hash[hash2Value] = Pos;
            _hash[KHash3Offset + hash3Value] = Pos;
            _hash[_fixHashSize + hashValue] = Pos;

            var mmm = Pos < _cyclicBufferSize ? Pos : _cyclicBufferSize;

            if (d2 < mmm && BufferBase[cur - (int)d2] == BufferBase[cur])
            {
                distances[offset++] = 2;
                distances[offset++] = d2 - 1;
                var update = true;
                if (BufferBase[cur - (int)d2 + 2] == BufferBase[cur + 2])
                {
                }
                else if (d3 < mmm && BufferBase[cur - (int)d3] == BufferBase[cur])
                {
                    d2 = d3;
                    distances[offset++] = 0;
                    distances[offset++] = d3 - 1;
                }
                else
                {
                    update = false;
                }

                if (update)
                {
                    var cIdx = cur + (int)maxLen;
                    var limIdx = cur + (int)lenLimit;
                    while (cIdx != limIdx)
                    {
                        if (BufferBase[cIdx - (int)d2] != BufferBase[cIdx])
                            break;

                        cIdx++;
                    }

                    maxLen = (uint)(cIdx - cur);
                    distances[offset - 2] = maxLen;
                    if (maxLen == lenLimit)
                    {
                        // C reference: SkipMatchesSpec(MF_PARAMS(p)) with the PRE-update hash
                        // head, then MOVE_POS. Skip(1) would re-read the head we just overwrote
                        // (== Pos), walk the position against itself, and splice empty links
                        // into the tree.
                        SkipMatchesSpec(lenLimit, curMatch, Pos, cur, _cyclicBufferPos);
                        MovePos();
                        return offset;
                    }
                }
            }
            else if (d3 < mmm && BufferBase[cur - (int)d3] == BufferBase[cur])
            {
                d2 = d3;
                distances[offset++] = 0;
                distances[offset++] = d3 - 1;

                var cIdx = cur + (int)maxLen;
                var limIdx = cur + (int)lenLimit;
                while (cIdx != limIdx)
                {
                    if (BufferBase[cIdx - (int)d2] != BufferBase[cIdx])
                        break;

                    cIdx++;
                }

                maxLen = (uint)(cIdx - cur);
                distances[offset - 2] = maxLen;
                if (maxLen == lenLimit)
                {
                    // C reference: SkipMatchesSpec(MF_PARAMS(p)) with the PRE-update hash head.
                    SkipMatchesSpec(lenLimit, curMatch, Pos, cur, _cyclicBufferPos);
                    MovePos();
                    return offset;
                }
            }
        }
        else
        {
            _hash[hashValue] = Pos;
        }

        var ptr0 = (_cyclicBufferPos << 1) + 1;
        var ptr1 = _cyclicBufferPos << 1;

        uint len1;
        var len0 = len1 = _numHashDirectBytes;

        if (_numHashDirectBytes != 0)
            if (curMatch > matchMinPos)
                if (
                    BufferBase[BufferOffset + curMatch + _numHashDirectBytes]
                    != BufferBase[cur + _numHashDirectBytes]
                )
                {
                    distances[offset++] = maxLen = _numHashDirectBytes;
                    distances[offset++] = Pos - curMatch - 1;
                }

        var cmCheck = Pos > _cyclicBufferSize ? Pos - _cyclicBufferSize : 0u;

        if (cmCheck < curMatch)
        {
            var cutValue = _cutValue;
            do
            {
                var delta = Pos - curMatch;
                var cyclicPos =
                (
                    delta <= _cyclicBufferPos
                        ? _cyclicBufferPos - delta
                        : _cyclicBufferPos - delta + _cyclicBufferSize
                ) << 1;

                var pby1 = BufferOffset + curMatch;
                var len = Math.Min(len0, len1);
                if (BufferBase[pby1 + len] == BufferBase[cur + len])
                {
                    if (++len != lenLimit && BufferBase[pby1 + len] == BufferBase[cur + len])
                        while (++len != lenLimit)
                            if (BufferBase[pby1 + len] != BufferBase[cur + len])
                                break;

                    if (maxLen < len)
                    {
                        maxLen = len;
                        distances[offset++] = maxLen;
                        distances[offset++] = delta - 1;
                        if (len == lenLimit)
                        {
                            _son[ptr1] = _son[cyclicPos];
                            _son[ptr0] = _son[cyclicPos + 1];
                            MovePos();
                            return offset;
                        }
                    }
                }

                if (BufferBase[pby1 + len] < BufferBase[cur + len])
                {
                    _son[ptr1] = curMatch;
                    ptr1 = cyclicPos + 1;
                    curMatch = _son[ptr1];
                    len1 = len;
                }
                else
                {
                    _son[ptr0] = curMatch;
                    ptr0 = cyclicPos;
                    curMatch = _son[ptr0];
                    len0 = len;
                }
            } while (--cutValue != 0 && cmCheck < curMatch);
        }

        _son[ptr0] = _son[ptr1] = KEmptyHashValue;

        MovePos();
        return offset;
    }

    public void Skip(uint num)
    {
        do
        {
            uint lenLimit;
            if (Pos + _matchMaxLen <= StreamPos)
            {
                lenLimit = _matchMaxLen;
            }
            else
            {
                lenLimit = StreamPos - Pos;
                if (lenLimit < _minMatchCheck)
                {
                    MovePos();
                    continue;
                }
            }

            // ReSharper disable once UnusedVariable
            var matchMinPos = Pos > _cyclicBufferSize ? Pos - _cyclicBufferSize : 0;
            var cur = BufferOffset + Pos;

            uint hashValue;

            if (_hashArray)
            {
                var temp = Crc.Table[BufferBase[cur]] ^ BufferBase[cur + 1];
                var hash2Value = temp & (KHash2Size - 1);
                _hash[hash2Value] = Pos;
                temp ^= (uint)BufferBase[cur + 2] << 8;
                var hash3Value = temp & (KHash3Size - 1);
                _hash[KHash3Offset + hash3Value] = Pos;
                hashValue = (temp ^ (Crc.Table[BufferBase[cur + 3]] << 5)) & _hashMask;
            }
            else
            {
                hashValue = BufferBase[cur] ^ ((uint)BufferBase[cur + 1] << 8);
            }

            var curMatch = _hash[_fixHashSize + hashValue];
            _hash[_fixHashSize + hashValue] = Pos;

            var ptr0 = (_cyclicBufferPos << 1) + 1;
            var ptr1 = _cyclicBufferPos << 1;

            uint len1;
            var len0 = len1 = _numHashDirectBytes;

            var cmCheck = Pos > _cyclicBufferSize ? Pos - _cyclicBufferSize : 0u;

            if (cmCheck < curMatch)
            {
                var cutValue = _cutValue;
                do
                {
                    var delta = Pos - curMatch;
                    var cyclicPos =
                    (
                        delta <= _cyclicBufferPos
                            ? _cyclicBufferPos - delta
                            : _cyclicBufferPos - delta + _cyclicBufferSize
                    ) << 1;

                    var pby1 = BufferOffset + curMatch;
                    var len = Math.Min(len0, len1);
                    if (BufferBase[pby1 + len] == BufferBase[cur + len])
                    {
                        while (++len != lenLimit)
                            if (BufferBase[pby1 + len] != BufferBase[cur + len])
                                break;

                        if (len == lenLimit)
                        {
                            _son[ptr1] = _son[cyclicPos];
                            _son[ptr0] = _son[cyclicPos + 1];
                            goto skipDone;
                        }
                    }

                    if (BufferBase[pby1 + len] < BufferBase[cur + len])
                    {
                        _son[ptr1] = curMatch;
                        ptr1 = cyclicPos + 1;
                        curMatch = _son[ptr1];
                        len1 = len;
                    }
                    else
                    {
                        _son[ptr0] = curMatch;
                        ptr0 = cyclicPos;
                        curMatch = _son[ptr0];
                        len0 = len;
                    }
                } while (--cutValue != 0 && cmCheck < curMatch);
            }

            _son[ptr0] = _son[ptr1] = KEmptyHashValue;
            skipDone:

            MovePos();
        } while (--num != 0);
    }

    /// <summary>
    ///     Inserts the current position into the binary tree without recording distances,
    ///     walking the chain from <paramref name="curMatch" /> (the caller-captured pre-update
    ///     hash head). Transliterated from LZMA SDK 23.01 LzFind.c SkipMatchesSpec.
    /// </summary>
    private void SkipMatchesSpec(uint lenLimit, uint curMatch, uint pos, uint cur, uint cyclicBufferPos)
    {
        var ptr0 = (cyclicBufferPos << 1) + 1;
        var ptr1 = cyclicBufferPos << 1;
        uint len1;
        var len0 = len1 = 0;

        var cmCheck = Pos > _cyclicBufferSize ? Pos - _cyclicBufferSize : 0u;

        if (cmCheck < curMatch)
        {
            var cutValue = _cutValue;
            do
            {
                var delta = pos - curMatch;
                var cyclicPos =
                (
                    delta <= cyclicBufferPos
                        ? cyclicBufferPos - delta
                        : cyclicBufferPos - delta + _cyclicBufferSize
                ) << 1;

                var pby1 = BufferOffset + curMatch;
                var len = Math.Min(len0, len1);
                if (BufferBase[pby1 + len] == BufferBase[cur + len])
                {
                    while (++len != lenLimit)
                        if (BufferBase[pby1 + len] != BufferBase[cur + len])
                            break;

                    if (len == lenLimit)
                    {
                        _son[ptr1] = _son[cyclicPos];
                        _son[ptr0] = _son[cyclicPos + 1];
                        return;
                    }
                }

                if (BufferBase[pby1 + len] < BufferBase[cur + len])
                {
                    _son[ptr1] = curMatch;
                    ptr1 = cyclicPos + 1;
                    curMatch = _son[ptr1];
                    len1 = len;
                }
                else
                {
                    _son[ptr0] = curMatch;
                    ptr0 = cyclicPos;
                    curMatch = _son[ptr0];
                    len0 = len;
                }
            } while (--cutValue != 0 && cmCheck < curMatch);
        }

        _son[ptr0] = _son[ptr1] = KEmptyHashValue;
    }

    public void Create(
        uint historySize,
        uint keepAddBufferBefore,
        uint matchMaxLen,
        uint keepAddBufferAfter,
        uint? cutValue
    )
    {
        if (historySize > KMaxValForNormalize - 256)
            throw new Exception();

        // LzmaEnc overrides the SDK default with props.mc = 16 + (numFastBytes >> 1)
        // (LzmaEncProps_Normalize); the old SDK default derived it from matchMaxLen instead,
        // producing a deeper tree walk and different match lists than MAME's chdman.
        _cutValue = cutValue ?? 16 + (matchMaxLen >> 1);

        var windowReservSize =
            (historySize + keepAddBufferBefore + matchMaxLen + keepAddBufferAfter) / 2 + 256;

        base.Create(
            historySize + keepAddBufferBefore,
            matchMaxLen + keepAddBufferAfter,
            windowReservSize
        );

        _matchMaxLen = matchMaxLen;

        var cyclicBufferSize = historySize + 1;
        if (_cyclicBufferSize != cyclicBufferSize)
            _son = new uint[(_cyclicBufferSize = cyclicBufferSize) * 2];

        var hs = KBt2HashSize;

        if (_hashArray)
        {
            hs = historySize - 1;
            hs |= hs >> 1;
            hs |= hs >> 2;
            hs |= hs >> 4;
            hs |= hs >> 8;
            hs >>= 1;
            hs |= 0xFFFF;
            if (hs > 1 << 24)
                hs >>= 1;

            _hashMask = hs;
            hs++;
            hs += _fixHashSize;
        }

        if (hs != _hashSizeSum)
            _hash = new uint[_hashSizeSum = hs];
    }

    internal void SetType(int numHashBytes)
    {
        _hashArray = numHashBytes > 2;
        if (_hashArray)
        {
            _numHashDirectBytes = 0;
            _minMatchCheck = 4;
            _fixHashSize = KHash2Size + KHash3Size;
        }
        else
        {
            _numHashDirectBytes = 2;
            _minMatchCheck = 2 + 1;
            _fixHashSize = 0;
        }
    }

    internal new void MovePos()
    {
        if (++_cyclicBufferPos >= _cyclicBufferSize)
            _cyclicBufferPos = 0;

        base.MovePos();
        if (Pos == KMaxValForNormalize)
            Normalize();
    }

    private static void NormalizeLinks(uint[] items, uint numItems, uint subValue)
    {
        for (uint i = 0; i < numItems; i++)
        {
            var value = items[i];
            value = value <= subValue ? KEmptyHashValue : value - subValue;
            items[i] = value;
        }
    }

    private void Normalize()
    {
        var subValue = Pos - _cyclicBufferSize;
        NormalizeLinks(_son, _cyclicBufferSize * 2, subValue);
        NormalizeLinks(_hash, _hashSizeSum, subValue);
        ReduceOffsets((int)subValue);
    }

    internal void SetCutValue(uint cutValue)
    {
        _cutValue = cutValue;
    }
}