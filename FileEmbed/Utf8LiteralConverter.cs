using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace FileEmbed;

internal static class Utf8LiteralConverter
{
    //stripped down version of an internal method from Rosyln
    internal static bool TryConvertToUtf8String(StringBuilder? builder, ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length;)
        {
            if (Rune.DecodeFromUtf8(bytes.Slice(i), out Rune rune, out int bytesConsumed) != OperationStatus.Done)
            {
                return false;
            }

            i += bytesConsumed;

            if (builder is not null)
            {
                if (rune.TryGetEscapeCharacter(out char escapeChar))
                {
                    builder.Append('\\');
                    builder.Append(escapeChar);
                }
                else
                {
                    rune.AppendTo(builder);
                }
            }
        }
        return true;
    }

    //Rune does not exist in .net standard 2.0. This is a copy of the BCL type, with everything that is not needed for its use here stripped out
    //original source: https://github.com/dotnet/runtime/blob/508fef51e841aa16ffed1aae32bf4793a2cea363/src/libraries/System.Private.CoreLib/src/System/Text/Rune.cs
    private readonly struct Rune
    {
        private readonly uint _value;
        
        private Rune(uint scalarValue)
        {
            _value = scalarValue;
        }
        
        private bool IsBmp => _value <= 0xFFFFu;
        
        private static Rune ReplacementChar => new(0xFFFD);
        
        public static OperationStatus DecodeFromUtf8(ReadOnlySpan<byte> source, out Rune result, out int bytesConsumed)
        {
            int index = 0;
            if (source.IsEmpty)
            {
                goto NeedsMoreData;
            }

            uint tempValue = source[0];
            if (IsAsciiCodePoint(tempValue))
            {
                bytesConsumed = 1;
                result = new Rune(tempValue);
                return OperationStatus.Done;
            }

            index = 1;
            if (!IsInRangeInclusive(tempValue, 0xC2, 0xF4))
            {
                goto Invalid;
            }

            tempValue = (tempValue - 0xC2) << 6;

            if (source.Length <= 1)
            {
                goto NeedsMoreData;
            }

            int thisByteSignExtended = (sbyte)source[1];
            if (thisByteSignExtended >= -64)
            {
                goto Invalid;
            }

            tempValue += (uint)thisByteSignExtended;
            tempValue += 0x80; 
            tempValue += (0xC2 - 0xC0) << 6; 

            if (tempValue < 0x0800)
            {
                goto Finish;
            }
            
            if (!IsInRangeInclusive(tempValue, ((0xE0 - 0xC0) << 6) + (0xA0 - 0x80), ((0xF4 - 0xC0) << 6) + (0x8F - 0x80)))
            {
                goto Invalid;
            }

            if (IsInRangeInclusive(tempValue, ((0xED - 0xC0) << 6) + (0xA0 - 0x80), ((0xED - 0xC0) << 6) + (0xBF - 0x80)))
            {
                goto Invalid;
            }

            if (IsInRangeInclusive(tempValue, ((0xF0 - 0xC0) << 6) + (0x80 - 0x80), ((0xF0 - 0xC0) << 6) + (0x8F - 0x80)))
            {
                goto Invalid;
            }

            index = 2;
            if (source.Length <= 2)
            {
                goto NeedsMoreData;
            }

            thisByteSignExtended = (sbyte)source[2];
            if (thisByteSignExtended >= -64)
            {
                goto Invalid; 
            }

            tempValue <<= 6;
            tempValue += (uint)thisByteSignExtended;
            tempValue += 0x80; 
            tempValue -= (0xE0 - 0xC0) << 12;

            if (tempValue <= 0xFFFF)
            {
                goto Finish; 
            }

            index = 3;
            if (source.Length <= 3)
            {
                goto NeedsMoreData;
            }

            thisByteSignExtended = (sbyte)source[3];
            if (thisByteSignExtended >= -64)
            {
                goto Invalid; 
            }

            tempValue <<= 6;
            tempValue += (uint)thisByteSignExtended;
            tempValue += 0x80; 
            tempValue -= (0xF0 - 0xE0) << 18; 

        Finish:

            bytesConsumed = index + 1;
            result = new Rune(tempValue);
            return OperationStatus.Done;

        NeedsMoreData:

            bytesConsumed = index;
            result = ReplacementChar;
            return OperationStatus.NeedMoreData;

        Invalid:

            bytesConsumed = index;
            result = ReplacementChar;
            return OperationStatus.InvalidData;
        }

        public bool TryGetEscapeCharacter(out char escapedChar)
        {
            switch (_value)
            {
                case '"': escapedChar = '"'; return true;
                case '\\': escapedChar = '\\'; return true;
                case '\0': escapedChar = '0'; return true;
                case '\a': escapedChar = 'a'; return true;
                case '\b': escapedChar = 'b'; return true;
                case '\f': escapedChar = 'f'; return true;
                case '\n': escapedChar = 'n'; return true;
                case '\r': escapedChar = 'r'; return true;
                case '\t': escapedChar = 't'; return true;
                case '\v': escapedChar = 'v'; return true;
            }

            escapedChar = default;
            return false;
        }

        public void AppendTo(StringBuilder builder)
        {
            if (IsBmp)
            {
                builder.Append((char)_value);
                return;
            }
            GetUtf16SurrogatesFromSupplementaryPlaneScalar(_value, out char highSurrogate, out char lowSurrogate);
            builder.Append(highSurrogate);
            builder.Append(lowSurrogate);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GetUtf16SurrogatesFromSupplementaryPlaneScalar(uint value, out char highSurrogateCodePoint, out char lowSurrogateCodePoint)
        {
            highSurrogateCodePoint = (char)((value + ((0xD800u - 0x40u) << 10)) >> 10);
            lowSurrogateCodePoint = (char)((value & 0x3FFu) + 0xDC00u);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAsciiCodePoint(uint value) => value <= 0x7Fu;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsInRangeInclusive(uint value, uint lowerBound, uint upperBound) => value - lowerBound <= upperBound - lowerBound;
    }
}