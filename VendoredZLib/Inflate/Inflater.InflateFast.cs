#nullable disable
// Original code and comments Copyright (C) 1995-2017 Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

#if !NET7_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace VendoredZLib.Inflate;

internal static partial class Inflater
{
    private static void InflateFast(
        ref ZStream strm,
        uint start,
        ref byte window,
        ref Code lcode,
        ref Code dcode
    )
    {
        var state = strm.InflateState;
        var last = strm.NextInput + (strm.AvailIn - 5); // have enough input while in < last
        var beg = strm.NextOutput - (start - strm.AvailOut); // inflate()'s initial strm.NextOutput
        var end = strm.NextOutput + (strm.AvailOut - 257); // while out < end, enough space available
        var wsize = state.Wsize;
        var whave = state.Whave;
        var wnext = state.Wnext;
        var hold = state.Hold;
        var bits = state.Bits;
        var lmask = (1U << state.Lenbits) - 1;
        var dmask = (1U << state.Distbits) - 1;
        uint len; // match length, unused bytes

        ref var @in = ref
#if NET7_0_OR_GREATER
        Unsafe.Add(ref strm.InputPtr, strm.NextInput);
#else
        MemoryMarshal.GetReference(strm.Input2.Slice((int)strm.NextInput));
#endif
        ref var @out = ref
#if NET7_0_OR_GREATER
        Unsafe.Add(ref strm.OutputPtr, strm.NextOutput);
#else
        MemoryMarshal.GetReference(strm.Output2.Slice((int)strm.NextOutput));
#endif

        // decode literals and length/distances until end-of-block or not enough input data or output space
        do
        {
            if (bits < 15)
            {
                hold += (uint)@in << (int)bits;
                @in = ref Unsafe.Add(ref @in, 1U);
                bits += 8;
                hold += (uint)@in << (int)bits;
                @in = ref Unsafe.Add(ref @in, 1U);
                bits += 8;
                strm.NextInput += 2;
            }

            ref var here = ref Unsafe.Add(ref lcode, hold & lmask); // retrieved table entry
            dolen:
            uint op = here.bits; // code bits, operation, extra bits, or window position, window bytes to copy
            if (op == 0)
            {
                strm.Msg = "invalid literal/length code";
                state.Mode = InflateMode.Bad;
                break;
            }

            hold >>= (int)op;
            bits -= op;
            op = here.op;
            if (op == 0) // literal
            {
                Trace.Tracevv(
                    here.val is >= 0x20 and < 0x7f
                        ? $"inflate:         literal '{Convert.ToChar(here.val)}'\n"
                        : $"inflate:         literal 0x{here.val:X2}\n"
                );
                @out = (byte)here.val;
                @out = ref Unsafe.Add(ref @out, 1U);
                strm.NextOutput++;
            }
            else if ((op & 16) != 0) // length base
            {
                len = here.val;
                op &= 15; // number of extra bits
                if (op != 0)
                {
                    if (bits < op)
                    {
                        hold += (uint)(@in << (int)bits);
                        @in = ref Unsafe.Add(ref @in, 1U);
                        bits += 8;
                        strm.NextInput++;
                    }

                    len += hold & ((1U << (int)op) - 1);
                    hold >>= (int)op;
                    bits -= op;
                }

                Trace.Tracevv($"inflate:         length {len}\n");
                if (bits < 15)
                {
                    hold += (uint)@in << (int)bits;
                    @in = ref Unsafe.Add(ref @in, 1U);
                    bits += 8;
                    hold += (uint)@in << (int)bits;
                    @in = ref Unsafe.Add(ref @in, 1U);
                    bits += 8;
                    strm.NextInput += 2;
                }

                here = ref Unsafe.Add(ref dcode, hold & dmask);
                dodist:
                op = here.bits;
                if (op == 0)
                {
                    strm.Msg = "invalid distance code";
                    state.Mode = InflateMode.Bad;
                    break;
                }

                hold >>= (int)op;
                bits -= op;
                op = here.op;
                if ((op & 16) != 0) // distance base
                {
                    uint dist = here.val; // match distance
                    op &= 15; // number of extra bits
                    if (bits < op)
                    {
                        hold += (uint)(@in << (int)bits);
                        @in = ref Unsafe.Add(ref @in, 1U);
                        bits += 8;
                        strm.NextInput++;
                        if (bits < op)
                        {
                            hold += (uint)(@in << (int)bits);
                            @in = ref Unsafe.Add(ref @in, 1U);
                            bits += 8;
                            strm.NextInput++;
                        }
                    }

                    dist += hold & ((1U << (int)op) - 1);
                    hold >>= (int)op;
                    bits -= op;
                    Trace.Tracevv($"inflate:         distance {dist}\n");
                    op = strm.NextOutput - beg; // max distance in output
                    if (dist > op)
                    {
                        op = dist - op; // distance back in window
                        if (op > whave)
                            if (state.Sane != 0)
                            {
                                strm.Msg = "invalid distance too far back";
                                state.Mode = InflateMode.Bad;
                                break;
                            }

                        ref var from = ref window; // where to copy match from
                        if (wnext == 0) // very common case
                        {
                            from = ref Unsafe.Add(ref from, wsize - op);
                            if (op < len) // some from window
                            {
                                len -= op;
                                do
                                {
                                    @out = from;
                                    @out = ref Unsafe.Add(ref @out, 1U);
                                    from = ref Unsafe.Add(ref from, 1U);
                                    strm.NextOutput++;
                                } while (--op != 0);

                                from = ref Unsafe.Subtract(ref @out, dist); // rest from output
                            }
                        }
                        else if (wnext < op) // wrap around window
                        {
                            from = ref Unsafe.Add(ref from, wsize + wnext - op);
                            op -= wnext;
                            if (op < len) // some from end of window
                            {
                                len -= op;
                                do
                                {
                                    @out = from;
                                    @out = ref Unsafe.Add(ref @out, 1U);
                                    from = ref Unsafe.Add(ref from, 1U);
                                    strm.NextOutput++;
                                } while (--op != 0);

                                from = ref window;
                                if (wnext < len) // some from start of window
                                {
                                    op = wnext;
                                    len -= op;
                                    do
                                    {
                                        @out = from;
                                        @out = ref Unsafe.Add(ref @out, 1U);
                                        from = ref Unsafe.Add(ref from, 1U);
                                        strm.NextOutput++;
                                    } while (--op != 0);

                                    from = ref Unsafe.Subtract(ref @out, dist); // rest from output
                                }
                            }
                        }
                        else // contiguous in window
                        {
                            from = ref Unsafe.Add(ref from, wnext - op);
                            if (op < len) // some from window
                            {
                                len -= op;
                                do
                                {
                                    @out = from;
                                    @out = ref Unsafe.Add(ref @out, 1U);
                                    from = ref Unsafe.Add(ref from, 1U);
                                    strm.NextOutput++;
                                } while (--op != 0);

                                from = ref Unsafe.Subtract(ref @out, dist); // rest from output
                            }
                        }

                        while (len > 2)
                        {
                            @out = from;
                            @out = ref Unsafe.Add(ref @out, 1U);
                            from = ref Unsafe.Add(ref from, 1U);

                            @out = from;
                            @out = ref Unsafe.Add(ref @out, 1U);
                            from = ref Unsafe.Add(ref from, 1U);

                            @out = from;
                            @out = ref Unsafe.Add(ref @out, 1U);
                            from = ref Unsafe.Add(ref from, 1U);
                            strm.NextOutput += 3;
                            len -= 3;
                        }

                        if (len != 0)
                        {
                            @out = from;
                            @out = ref Unsafe.Add(ref @out, 1U);
                            from = ref Unsafe.Add(ref from, 1U);
                            strm.NextOutput++;
                            if (len > 1)
                            {
                                @out = from;
                                @out = ref Unsafe.Add(ref @out, 1U);
                                from = ref Unsafe.Add(ref from, 1U);
                                strm.NextOutput++;
                            }
                        }
                    }
                    else
                    {
                        ref var from = ref Unsafe.Subtract(ref @out, dist); // copy direct from output
                        do // minimum length is three
                        {
                            @out = from;
                            @out = ref Unsafe.Add(ref @out, 1U);
                            from = ref Unsafe.Add(ref from, 1U);

                            @out = from;
                            @out = ref Unsafe.Add(ref @out, 1U);
                            from = ref Unsafe.Add(ref from, 1U);

                            @out = from;
                            @out = ref Unsafe.Add(ref @out, 1U);
                            from = ref Unsafe.Add(ref from, 1U);

                            len -= 3;
                            strm.NextOutput += 3;
                        } while (len > 2);

                        if (len != 0)
                        {
                            @out = from;
                            @out = ref Unsafe.Add(ref @out, 1U);
                            from = ref Unsafe.Add(ref from, 1U);
                            strm.NextOutput++;
                            if (len > 1)
                            {
                                @out = from;
                                @out = ref Unsafe.Add(ref @out, 1U);
                                from = ref Unsafe.Add(ref from, 1U);
                                strm.NextOutput++;
                            }
                        }
                    }
                }
                else if ((op & 64) == 0) // 2nd level distance code
                {
                    here = ref Unsafe.Add(ref dcode, here.val + (hold & ((1U << (int)op) - 1)));
                    goto dodist;
                }
                else
                {
                    strm.Msg = "invalid distance code";
                    state.Mode = InflateMode.Bad;
                    break;
                }
            }
            else if ((op & 64) == 0) // 2nd level length code
            {
                here = ref Unsafe.Add(ref lcode, here.val + (hold & ((1U << (int)op) - 1)));
                goto dolen;
            }
            else if ((op & 32) != 0) // end-of-block
            {
                Trace.Tracevv("inflate:         end of block\n");
                state.Mode = InflateMode.Type;
                break;
            }
            else
            {
                strm.Msg = "invalid literal/length code";
                state.Mode = InflateMode.Bad;
                break;
            }
        } while (strm.NextInput < last && strm.NextOutput < end);

        // return unused bytes (on entry, bits < 8, so in won't go too far back)
        len = bits >> 3;
        @in = ref Unsafe.Subtract(ref @in, len);
        strm.NextInput -= len;
        bits -= len << 3;
        hold &= (1U << (int)bits) - 1;

        // update state and return
        strm.AvailIn =
            strm.NextInput < last ? 5 + (last - strm.NextInput) : 5 - (strm.NextInput - last);
        strm.AvailOut =
            strm.NextOutput < end ? 257 + (end - strm.NextOutput) : 257 - (strm.NextOutput - end);

        state.Hold = hold;
        state.Bits = bits;
    }
}
