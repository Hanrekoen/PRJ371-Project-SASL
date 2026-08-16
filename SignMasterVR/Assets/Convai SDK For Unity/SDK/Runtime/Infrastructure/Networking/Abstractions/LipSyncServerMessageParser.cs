using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Convai.Domain.Models.LipSync;
using Convai.Shared.Types;

namespace Convai.Infrastructure.Networking
{
    internal static class LipSyncServerMessageParser
    {
        internal const string SinglePayloadType = "neurosync-blendshapes";
        internal const string ChunkedPayloadType = "chunked-neurosync-blendshapes";

        private const string ServerMessageType = "server-message";

        private static readonly ConditionalWeakTable<IReadOnlyList<string>, ChannelLookupCacheEntry>
            ChannelLookupCache = new();

        [ThreadStatic] private static LipSyncParseContext s_parseContext;

        private static LipSyncParseContext GetParseContext() => s_parseContext ??= new LipSyncParseContext();

        public static bool MayContainLipSyncServerMessage(ReadOnlySpan<byte> payloadBytes) =>
            LipSyncMessageDetector.MayContainLipSyncServerMessage(payloadBytes);

        public static bool TryParse(
            ReadOnlyMemory<byte> payloadBytes,
            float frameRate,
            in LipSyncTransportOptions options,
            out bool handled,
            out string payloadType,
            out LipSyncPackedChunk chunk,
            out string dropReasonCode)
        {
            handled = false;
            payloadType = string.Empty;
            chunk = null;
            dropReasonCode = string.Empty;

            if (payloadBytes.IsEmpty)
            {
                dropReasonCode = "lipsync.parse.payload_null";
                return false;
            }

            LipSyncParseContext parseContext = GetParseContext();

            if (!TryParseEnvelope(
                    payloadBytes.Span,
                    options.SourceBlendshapeNames,
                    options.Format,
                    options.ChunkSize,
                    parseContext,
                    out bool isServerMessage,
                    out bool hasPayloadObject,
                    out ParsedPayload payload))
            {
                handled = true;
                dropReasonCode = "lipsync.parse.payload_invalid_json";
                return false;
            }

            if (!isServerMessage) return false;

            if (!hasPayloadObject)
            {
                handled = true;
                dropReasonCode = "lipsync.parse.payload_invalid_json";
                return false;
            }

            bool isSinglePayload = payload.PayloadKind == PayloadKind.Single;
            bool isChunkedPayload = payload.PayloadKind == PayloadKind.Chunked;
            payloadType = isSinglePayload
                ? SinglePayloadType
                : isChunkedPayload
                    ? ChunkedPayloadType
                    : string.Empty;
            if (!isSinglePayload && !isChunkedPayload) return false;

            handled = true;

            if (!LipSyncContractValidator.TryValidateTransportOptions(in options, out dropReasonCode)) return false;

            if (!ValidateFormat(in payload))
            {
                dropReasonCode = "lipsync.parse.format_mismatch";
                return false;
            }

            if (!payload.HasBlendshapes)
            {
                dropReasonCode = "lipsync.parse.missing_blendshapes";
                return false;
            }

            int channelCount = options.SourceBlendshapeNames?.Count ?? 0;
            if (channelCount <= 0)
            {
                dropReasonCode = "lipsync.parse.transport_options_invalid";
                return false;
            }

            float[][] frames;
            if (isSinglePayload)
            {
                if (payload.BlendshapesKind == BlendshapesRootKind.Array && payload.ArraySingleValid)
                    frames = new[] { payload.ArraySingleFrame };
                else if (payload.BlendshapesKind == BlendshapesRootKind.Object && payload.ObjectSingleValid)
                    frames = new[] { payload.ObjectSingleFrame };
                else
                {
                    dropReasonCode = "lipsync.parse.invalid_single_frame_payload";
                    return false;
                }
            }
            else
            {
                if (payload.BlendshapesKind == BlendshapesRootKind.Array && payload.ArrayChunkValid)
                    frames = payload.ArrayChunkFrames;
                else if (payload.BlendshapesKind == BlendshapesRootKind.Object && payload.ObjectSingleValid)
                    frames = new[] { payload.ObjectSingleFrame };
                else
                {
                    dropReasonCode = "lipsync.parse.invalid_chunk_payload";
                    return false;
                }

                if (frames == null || frames.Length == 0)
                {
                    dropReasonCode = "lipsync.parse.invalid_chunk_payload";
                    return false;
                }
            }

            chunk = new LipSyncPackedChunk(
                options.ProfileId,
                payload.Fps > 0f ? payload.Fps : frameRate,
                options.SourceBlendshapeNames,
                frames,
                payload.ResponseId,
                payload.NeuroSyncTurnId,
                payload.Epoch,
                payload.StartFrameIndex,
                payload.Sequence);

            if (!chunk.IsValid)
            {
                dropReasonCode = isSinglePayload
                    ? "lipsync.parse.invalid_single_frame_payload"
                    : "lipsync.parse.invalid_chunk_payload";
            }

            return chunk.IsValid;
        }

        public static LipSyncParseResult Parse(
            ReadOnlyMemory<byte> payloadBytes,
            float frameRate,
            in LipSyncTransportOptions options)
        {
            bool parsed = TryParse(
                payloadBytes,
                frameRate,
                in options,
                out bool handled,
                out string payloadType,
                out LipSyncPackedChunk chunk,
                out string dropReasonCode);

            return new LipSyncParseResult(
                handled,
                parsed,
                payloadType,
                chunk,
                dropReasonCode);
        }

        private static bool TryParseEnvelope(
            ReadOnlySpan<byte> json,
            IReadOnlyList<string> channelNames,
            string expectedFormat,
            int expectedChunkSize,
            LipSyncParseContext parseContext,
            out bool isServerMessage,
            out bool hasPayloadObject,
            out ParsedPayload payload)
        {
            isServerMessage = false;
            hasPayloadObject = false;
            payload = default;
            Dictionary<string, int> channelLookup = BuildChannelLookup(channelNames);

            JsonByteReader reader = new(json);
            if (!reader.TryConsume((byte)'{')) return false;

            bool hasPayloadField = false;
            while (true)
            {
                if (reader.TryConsume((byte)'}')) return true;

                if (!reader.TryReadStringToken(out ReadOnlySpan<byte> propertyName)) return false;

                if (!reader.TryConsume((byte)':')) return false;

                if (Utf8EqualsAscii(propertyName, "type"))
                {
                    if (!TryConsumeStringValue(ref reader, out bool isString, out ReadOnlySpan<byte> value))
                        return false;

                    isServerMessage = isString && Utf8EqualsAscii(value, ServerMessageType);
                }
                else if (Utf8EqualsAscii(propertyName, "payload") ||
                         (Utf8EqualsAscii(propertyName, "data") && !hasPayloadField))
                {
                    hasPayloadField = true;
                    if (!reader.TryConsume((byte)'{'))
                    {
                        hasPayloadObject = false;
                        if (!reader.TrySkipValue()) return false;
                    }
                    else
                    {
                        if (!TryParsePayloadObject(
                                ref reader,
                                channelNames,
                                channelLookup,
                                expectedFormat,
                                expectedChunkSize,
                                parseContext,
                                out ParsedPayload parsedPayload))
                            return false;

                        payload = parsedPayload;
                        hasPayloadObject = true;
                    }
                }
                else
                {
                    if (!reader.TrySkipValue()) return false;
                }

                if (reader.TryConsume((byte)',')) continue;

                if (reader.TryConsume((byte)'}')) return true;

                return false;
            }
        }

        private static bool TryParsePayloadObject(
            ref JsonByteReader reader,
            IReadOnlyList<string> channelNames,
            Dictionary<string, int> channelLookup,
            string expectedFormat,
            int expectedChunkSize,
            LipSyncParseContext parseContext,
            out ParsedPayload payload)
        {
            payload = default;
            int channelCount = channelNames?.Count ?? 0;

            while (true)
            {
                if (reader.TryConsume((byte)'}')) return true;

                if (!reader.TryReadStringToken(out ReadOnlySpan<byte> propertyName)) return false;

                if (!reader.TryConsume((byte)':')) return false;

                if (Utf8EqualsAscii(propertyName, "type"))
                {
                    if (!TryConsumeStringValue(ref reader, out bool isString, out ReadOnlySpan<byte> value))
                        return false;

                    if (!isString)
                        payload.PayloadKind = PayloadKind.None;
                    else if (Utf8EqualsAscii(value, SinglePayloadType))
                        payload.PayloadKind = PayloadKind.Single;
                    else if (Utf8EqualsAscii(value, ChunkedPayloadType))
                        payload.PayloadKind = PayloadKind.Chunked;
                    else
                        payload.PayloadKind = PayloadKind.Other;
                }
                else if (Utf8EqualsAscii(propertyName, "format"))
                {
                    if (!TryConsumeStringValue(ref reader, out bool isString, out ReadOnlySpan<byte> value))
                        return false;

                    payload.PayloadFormatState = isString
                        ? ClassifyFormatToken(value, expectedFormat)
                        : FormatState.Unknown;
                }
                else if (Utf8EqualsAscii(propertyName, "config"))
                {
                    if (!TryConsumeObjectFormatHint(ref reader, expectedFormat, out payload.ConfigFormatState))
                        return false;
                }
                else if (Utf8EqualsAscii(propertyName, "blendshape_config"))
                {
                    if (!TryConsumeObjectFormatHint(ref reader, expectedFormat,
                            out payload.BlendshapeConfigFormatState)) return false;
                }
                else if (Utf8EqualsAscii(propertyName, "blendshapes"))
                {
                    payload.HasBlendshapes = true;
                    if (reader.TryConsume((byte)'['))
                    {
                        payload.BlendshapesKind = BlendshapesRootKind.Array;
                        if (!TryParseBlendshapesArray(
                                ref reader,
                                channelNames,
                                channelLookup,
                                channelCount,
                                expectedChunkSize,
                                parseContext,
                                out payload.ArraySingleValid,
                                out payload.ArraySingleFrame,
                                out payload.ArrayChunkValid,
                                out payload.ArrayChunkFrames))
                            return false;
                    }
                    else if (reader.TryConsume((byte)'{'))
                    {
                        payload.BlendshapesKind = BlendshapesRootKind.Object;
                        payload.ObjectSingleValid = TryParseNamedFrameObject(
                            ref reader,
                            channelNames,
                            channelLookup,
                            channelCount,
                            parseContext,
                            out payload.ObjectSingleFrame);
                    }
                    else
                    {
                        payload.BlendshapesKind = BlendshapesRootKind.Other;
                        if (!reader.TrySkipValue()) return false;
                    }
                }
                else if (Utf8EqualsAscii(propertyName, "response_id"))
                {
                    if (!TryConsumeStringValue(ref reader, out bool isString, out ReadOnlySpan<byte> value))
                        return false;

                    payload.ResponseId = isString ? Encoding.UTF8.GetString(value) : string.Empty;
                }
                else if (Utf8EqualsAscii(propertyName, "neurosync_turn_id") ||
                         (Utf8EqualsAscii(propertyName, "turn_id") && !payload.NeuroSyncTurnId.HasValue))
                {
                    if (!TryConsumeValueAsInt(ref reader, out bool parsedInt, out int value)) return false;
                    if (parsedInt) payload.NeuroSyncTurnId = value;
                }
                else if (Utf8EqualsAscii(propertyName, "epoch"))
                {
                    if (!TryConsumeValueAsInt(ref reader, out bool parsedInt, out int value)) return false;
                    if (parsedInt) payload.Epoch = value;
                }
                else if (Utf8EqualsAscii(propertyName, "sequence"))
                {
                    if (!TryConsumeValueAsInt(ref reader, out bool parsedInt, out int value)) return false;
                    if (parsedInt) payload.Sequence = value;
                }
                else if (Utf8EqualsAscii(propertyName, "start_frame_index"))
                {
                    if (!TryConsumeValueAsInt(ref reader, out bool parsedInt, out int value)) return false;
                    if (parsedInt && value >= 0) payload.StartFrameIndex = value;
                }
                else if (Utf8EqualsAscii(propertyName, "fps"))
                {
                    if (!TryConsumeValueAsFloat(ref reader, out bool parsedFloat, out float value)) return false;
                    if (parsedFloat && value > 0f) payload.Fps = value;
                }
                else
                {
                    if (!reader.TrySkipValue()) return false;
                }

                if (reader.TryConsume((byte)',')) continue;

                if (reader.TryConsume((byte)'}')) return true;

                return false;
            }
        }

        private static bool TryConsumeObjectFormatHint(ref JsonByteReader reader, string expectedFormat,
            out FormatState formatState)
        {
            formatState = FormatState.Unknown;
            if (!reader.TryConsume((byte)'{')) return reader.TrySkipValue();

            while (true)
            {
                if (reader.TryConsume((byte)'}')) return true;

                if (!reader.TryReadStringToken(out ReadOnlySpan<byte> propertyName)) return false;

                if (!reader.TryConsume((byte)':')) return false;

                if (Utf8EqualsAscii(propertyName, "format"))
                {
                    if (!TryConsumeStringValue(ref reader, out bool isString, out ReadOnlySpan<byte> value))
                        return false;

                    formatState = isString
                        ? ClassifyFormatToken(value, expectedFormat)
                        : FormatState.Unknown;
                }
                else if (!reader.TrySkipValue()) return false;

                if (reader.TryConsume((byte)',')) continue;

                if (reader.TryConsume((byte)'}')) return true;

                return false;
            }
        }

        private static bool TryParseBlendshapesArray(
            ref JsonByteReader reader,
            IReadOnlyList<string> channelNames,
            Dictionary<string, int> channelLookup,
            int channelCount,
            int expectedChunkSize,
            LipSyncParseContext parseContext,
            out bool singleValid,
            out float[] singleFrame,
            out bool chunkValid,
            out float[][] chunkFrames)
        {
            singleValid = false;
            singleFrame = null;
            chunkValid = false;
            chunkFrames = null;

            int singleValueCount = 0;
            bool hasComplexChildren = false;
            float[] singleFrameBuffer = null;
            float[][] parsedChunkFrames = null;
            int parsedChunkFrameCount = 0;
            int initialChunkCapacity = Math.Max(expectedChunkSize, 1);

            while (true)
            {
                if (reader.TryConsume((byte)']')) break;

                if (reader.TryConsume((byte)'['))
                {
                    hasComplexChildren = true;
                    if (!TryParseNumericFrameArray(
                            ref reader,
                            channelCount,
                            parseContext,
                            out bool frameValid,
                            out float[] numericFrame))
                        return false;

                    if (frameValid)
                    {
                        AppendChunkFrame(
                            ref parsedChunkFrames,
                            ref parsedChunkFrameCount,
                            initialChunkCapacity,
                            numericFrame,
                            parseContext);
                    }
                }
                else if (reader.TryConsume((byte)'{'))
                {
                    hasComplexChildren = true;
                    if (TryParseNamedFrameObject(
                            ref reader,
                            channelNames,
                            channelLookup,
                            channelCount,
                            parseContext,
                            out float[] namedFrame))
                    {
                        AppendChunkFrame(
                            ref parsedChunkFrames,
                            ref parsedChunkFrameCount,
                            initialChunkCapacity,
                            namedFrame,
                            parseContext);
                    }
                }
                else
                {
                    if (!TryConsumeValueAsFloat(ref reader, out bool parsedFloat, out float value)) return false;

                    if (singleFrameBuffer == null && channelCount > 0)
                        singleFrameBuffer = parseContext.RentSingleFrameBuffer(channelCount);

                    if (singleFrameBuffer != null && singleValueCount < channelCount)
                    {
                        singleFrameBuffer[singleValueCount] = parsedFloat
                            ? value
                            : 0f;
                    }

                    singleValueCount++;
                }

                if (reader.TryConsume((byte)',')) continue;

                if (reader.TryConsume((byte)']')) break;

                return false;
            }

            singleValid = !hasComplexChildren && singleValueCount > 0;
            if (!singleValid)
                singleFrame = null;
            else if (singleFrameBuffer == null)
                singleFrame = Array.Empty<float>();
            else
                singleFrame = parseContext.CopyOwnedFrame(singleFrameBuffer, channelCount);

            chunkValid = parsedChunkFrameCount > 0;
            if (chunkValid)
            {
                chunkFrames = new float[parsedChunkFrameCount][];
                Array.Copy(parsedChunkFrames, 0, chunkFrames, 0, parsedChunkFrameCount);
            }
            else
                chunkFrames = null;

            if (parsedChunkFrames != null && parsedChunkFrameCount > 0)
                Array.Clear(parsedChunkFrames, 0, parsedChunkFrameCount);

            return true;
        }

        private static bool TryParseNumericFrameArray(
            ref JsonByteReader reader,
            int channelCount,
            LipSyncParseContext parseContext,
            out bool validFrame,
            out float[] values)
        {
            validFrame = false;
            values = null;
            float[] frameBuffer = parseContext.RentFrameBuffer(Math.Max(channelCount, 0));
            int valueCount = 0;
            bool hasComplexChildren = false;

            while (true)
            {
                if (reader.TryConsume((byte)']'))
                {
                    validFrame = !hasComplexChildren && valueCount > 0;
                    if (validFrame) values = parseContext.CopyOwnedFrame(frameBuffer, channelCount);

                    return true;
                }

                if (reader.TryConsume((byte)'['))
                {
                    hasComplexChildren = true;
                    if (!reader.TrySkipStartedArray()) return false;
                }
                else if (reader.TryConsume((byte)'{'))
                {
                    hasComplexChildren = true;
                    if (!reader.TrySkipStartedObject()) return false;
                }
                else
                {
                    if (!TryConsumeValueAsFloat(ref reader, out bool parsedFloat, out float value)) return false;

                    if (valueCount < channelCount) frameBuffer[valueCount] = parsedFloat ? value : 0f;

                    valueCount++;
                }

                if (reader.TryConsume((byte)',')) continue;

                if (reader.TryConsume((byte)']'))
                {
                    validFrame = !hasComplexChildren && valueCount > 0;
                    if (validFrame) values = parseContext.CopyOwnedFrame(frameBuffer, channelCount);

                    return true;
                }

                return false;
            }
        }

        private static bool TryParseNamedFrameObject(
            ref JsonByteReader reader,
            IReadOnlyList<string> channelNames,
            Dictionary<string, int> channelLookup,
            int channelCount,
            LipSyncParseContext parseContext,
            out float[] values)
        {
            values = null;
            float[] frameBuffer = parseContext.RentFrameBuffer(Math.Max(channelCount, 0));
            bool hasAnyProperty = false;
            bool hasAtLeastOneMappedValue = false;

            while (true)
            {
                if (reader.TryConsume((byte)'}'))
                {
                    if (hasAnyProperty && hasAtLeastOneMappedValue)
                    {
                        values = parseContext.CopyOwnedFrame(frameBuffer, channelCount);
                        return true;
                    }

                    return hasAnyProperty && hasAtLeastOneMappedValue;
                }

                if (!reader.TryReadStringToken(out ReadOnlySpan<byte> propertyName)) return false;

                if (!reader.TryConsume((byte)':')) return false;

                if (!TryConsumeValueAsFloat(ref reader, out bool parsedFloat, out float value)) return false;

                hasAnyProperty = true;
                if (parsedFloat)
                {
                    int channelIndex = FindChannelIndex(propertyName, channelNames, channelLookup);
                    if (channelIndex >= 0 && channelIndex < channelCount)
                    {
                        frameBuffer[channelIndex] = value;
                        hasAtLeastOneMappedValue = true;
                    }
                }

                if (reader.TryConsume((byte)',')) continue;

                if (reader.TryConsume((byte)'}'))
                {
                    if (hasAnyProperty && hasAtLeastOneMappedValue)
                    {
                        values = parseContext.CopyOwnedFrame(frameBuffer, channelCount);
                        return true;
                    }

                    return hasAnyProperty && hasAtLeastOneMappedValue;
                }

                return false;
            }
        }

        private static void AppendChunkFrame(
            ref float[][] frames,
            ref int count,
            int initialCapacity,
            float[] frame,
            LipSyncParseContext parseContext)
        {
            if (frame == null) return;

            if (frames == null)
                frames = parseContext.RentChunkFrameBuffer(Math.Max(initialCapacity, 1));
            else if (count >= frames.Length)
            {
                int newCapacity = Math.Max(frames.Length * 2, count + 1);
                Array.Resize(ref frames, newCapacity);
            }

            frames[count++] = frame;
        }

        private static int FindChannelIndex(
            ReadOnlySpan<byte> propertyName,
            IReadOnlyList<string> channelNames,
            Dictionary<string, int> channelLookup)
        {
            if (channelNames == null || channelNames.Count == 0) return -1;

            if (channelLookup != null)
            {
                foreach (KeyValuePair<string, int> pair in channelLookup)
                {
                    if (Utf8EqualsAsciiIgnoreCase(propertyName, pair.Key))
                        return pair.Value;
                }
            }

            for (int i = 0; i < channelNames.Count; i++)
            {
                if (Utf8EqualsAsciiIgnoreCase(propertyName, channelNames[i]))
                    return i;
            }

            return -1;
        }

        private static Dictionary<string, int> BuildChannelLookup(IReadOnlyList<string> channelNames)
        {
            if (channelNames == null || channelNames.Count == 0) return null;

            ChannelLookupCacheEntry entry = ChannelLookupCache.GetValue(channelNames, BuildChannelLookupEntry);
            return entry.Lookup;
        }

        private static ChannelLookupCacheEntry BuildChannelLookupEntry(IReadOnlyList<string> channelNames)
        {
            Dictionary<string, int> lookup = new(channelNames.Count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < channelNames.Count; i++)
            {
                string channel = channelNames[i];
                if (string.IsNullOrEmpty(channel)) continue;

                if (!lookup.ContainsKey(channel)) lookup.Add(channel, i);
            }

            return new ChannelLookupCacheEntry(lookup);
        }

        private static bool TryConsumeStringValue(ref JsonByteReader reader, out bool isString,
            out ReadOnlySpan<byte> value)
        {
            isString = false;
            value = default;

            if (!reader.TryReadStringToken(out ReadOnlySpan<byte> token)) return reader.TrySkipValue();

            value = token;
            isString = true;
            return true;
        }

        private static bool TryConsumeValueAsFloat(ref JsonByteReader reader, out bool parsedFloat, out float value)
        {
            parsedFloat = false;
            value = 0f;

            if (reader.TryReadStringToken(out ReadOnlySpan<byte> stringToken))
            {
                ReadOnlySpan<byte> trimmed = TrimAsciiWhitespace(stringToken);
                parsedFloat = TryParseFloatUtf8(trimmed, out value);
                return true;
            }

            if (reader.TryReadNumberToken(out ReadOnlySpan<byte> numberToken))
            {
                parsedFloat = TryParseFloatUtf8(numberToken, out value);
                return true;
            }

            if (reader.TryReadLiteral(out _)) return true;

            if (reader.TryConsume((byte)'{')) return reader.TrySkipStartedObject();

            if (reader.TryConsume((byte)'[')) return reader.TrySkipStartedArray();

            return false;
        }

        private static bool TryConsumeValueAsInt(ref JsonByteReader reader, out bool parsedInt, out int value)
        {
            parsedInt = false;
            value = 0;

            if (!TryConsumeValueAsFloat(ref reader, out bool parsedFloat, out float floatValue)) return false;
            if (!parsedFloat) return true;

            int intValue = (int)floatValue;
            if (Math.Abs(floatValue - intValue) > 0.0001f) return true;

            parsedInt = true;
            value = intValue;
            return true;
        }

        private static bool TryParseFloatUtf8(ReadOnlySpan<byte> token, out float value)
        {
            value = 0f;
            if (token.IsEmpty) return false;

            if (Utf8Parser.TryParse(token, out float parsedFloat, out int consumedFloat) &&
                consumedFloat == token.Length)
            {
                value = parsedFloat;
                return true;
            }

            if (Utf8Parser.TryParse(token, out double parsedDouble, out int consumedDouble) &&
                consumedDouble == token.Length)
            {
                value = (float)parsedDouble;
                return true;
            }

            return false;
        }

        private static bool ValidateFormat(in ParsedPayload payload)
        {
            FormatState selectedState = payload.PayloadFormatState != FormatState.Unknown
                ? payload.PayloadFormatState
                : payload.ConfigFormatState != FormatState.Unknown
                    ? payload.ConfigFormatState
                    : payload.BlendshapeConfigFormatState;

            return selectedState == FormatState.Unknown || selectedState == FormatState.Match;
        }

        private static FormatState ClassifyFormatToken(ReadOnlySpan<byte> token, string expectedFormat)
        {
            ReadOnlySpan<byte> trimmed = TrimAsciiWhitespace(token);
            if (trimmed.IsEmpty || string.IsNullOrWhiteSpace(expectedFormat)) return FormatState.Unknown;

            return Utf8EqualsAsciiIgnoreCaseTrimmed(trimmed, expectedFormat)
                ? FormatState.Match
                : FormatState.Mismatch;
        }

        private static bool Utf8EqualsAsciiIgnoreCaseTrimmed(ReadOnlySpan<byte> utf8, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;

            int start = 0;
            int end = value.Length - 1;
            while (start <= end && char.IsWhiteSpace(value[start])) start++;

            while (end >= start && char.IsWhiteSpace(value[end])) end--;

            int length = end - start + 1;
            if (length <= 0 || utf8.Length != length) return false;

            for (int i = 0; i < utf8.Length; i++)
            {
                byte left = ToLowerAscii(utf8[i]);
                char rightChar = value[start + i];
                if (rightChar > 127) return false;

                byte right = ToLowerAscii((byte)rightChar);
                if (left != right) return false;
            }

            return true;
        }

        private static bool Utf8EqualsAscii(ReadOnlySpan<byte> utf8, string value)
        {
            if (string.IsNullOrEmpty(value) || utf8.Length != value.Length) return false;

            for (int i = 0; i < utf8.Length; i++)
            {
                if (utf8[i] != (byte)value[i])
                    return false;
            }

            return true;
        }

        private static bool Utf8EqualsAsciiIgnoreCase(ReadOnlySpan<byte> utf8, string value)
        {
            if (string.IsNullOrEmpty(value) || utf8.Length != value.Length) return false;

            for (int i = 0; i < utf8.Length; i++)
            {
                byte left = ToLowerAscii(utf8[i]);
                char rightChar = value[i];
                if (rightChar > 127) return false;

                byte right = ToLowerAscii((byte)rightChar);
                if (left != right) return false;
            }

            return true;
        }

        private static byte ToLowerAscii(byte value) =>
            value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value;

        private static ReadOnlySpan<byte> TrimAsciiWhitespace(ReadOnlySpan<byte> value)
        {
            int start = 0;
            int end = value.Length - 1;

            while (start <= end && IsAsciiWhitespace(value[start])) start++;

            while (end >= start && IsAsciiWhitespace(value[end])) end--;

            return start > end ? ReadOnlySpan<byte>.Empty : value.Slice(start, end - start + 1);
        }

        private static bool IsAsciiWhitespace(byte value) =>
            value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

        private enum BlendshapesRootKind
        {
            None,
            Array,
            Object,
            Other
        }

        private enum PayloadKind
        {
            None,
            Single,
            Chunked,
            Other
        }

        private enum FormatState
        {
            Unknown,
            Match,
            Mismatch
        }

        private sealed class ChannelLookupCacheEntry
        {
            public ChannelLookupCacheEntry(Dictionary<string, int> lookup)
            {
                Lookup = lookup;
            }

            public Dictionary<string, int> Lookup { get; }
        }

        private sealed class LipSyncParseContext
        {
            private float[][] _chunkFrameBuffer = Array.Empty<float[]>();
            private float[] _frameBuffer = Array.Empty<float>();
            private float[] _singleFrameBuffer = Array.Empty<float>();

            public float[] RentSingleFrameBuffer(int channelCount)
            {
                EnsureBufferSize(ref _singleFrameBuffer, channelCount);
                if (channelCount > 0) Array.Clear(_singleFrameBuffer, 0, channelCount);

                return _singleFrameBuffer;
            }

            public float[] RentFrameBuffer(int channelCount)
            {
                EnsureBufferSize(ref _frameBuffer, channelCount);
                if (channelCount > 0) Array.Clear(_frameBuffer, 0, channelCount);

                return _frameBuffer;
            }

            public float[][] RentChunkFrameBuffer(int minimumCapacity)
            {
                int capacity = Math.Max(minimumCapacity, 1);
                if (_chunkFrameBuffer.Length < capacity) _chunkFrameBuffer = new float[capacity][];

                return _chunkFrameBuffer;
            }

            public float[] CopyOwnedFrame(float[] source, int channelCount)
            {
                if (channelCount <= 0) return Array.Empty<float>();

                float[] owned = new float[channelCount];
                Array.Copy(source, 0, owned, 0, channelCount);
                return owned;
            }

            private static void EnsureBufferSize(ref float[] buffer, int required)
            {
                if (buffer.Length < required) buffer = new float[required];
            }
        }

        private struct ParsedPayload
        {
            public PayloadKind PayloadKind;
            public FormatState PayloadFormatState;
            public FormatState ConfigFormatState;
            public FormatState BlendshapeConfigFormatState;
            public bool HasBlendshapes;
            public BlendshapesRootKind BlendshapesKind;

            public bool ArraySingleValid;
            public float[] ArraySingleFrame;
            public bool ArrayChunkValid;
            public float[][] ArrayChunkFrames;

            public bool ObjectSingleValid;
            public float[] ObjectSingleFrame;
            public string ResponseId;
            public int? NeuroSyncTurnId;
            public int? Epoch;
            public int? Sequence;
            public int? StartFrameIndex;
            public float Fps;
        }

        private ref struct JsonByteReader
        {
            private readonly ReadOnlySpan<byte> _source;
            private int _index;

            public JsonByteReader(ReadOnlySpan<byte> source)
            {
                _source = source;
                _index = 0;
            }

            public bool TryConsume(byte value)
            {
                SkipWhitespace();
                if (_index >= _source.Length || _source[_index] != value) return false;

                _index++;
                return true;
            }

            public bool TryReadStringToken(out ReadOnlySpan<byte> token)
            {
                token = default;
                SkipWhitespace();
                if (_index >= _source.Length || _source[_index] != (byte)'"') return false;

                _index++;
                int start = _index;
                while (_index < _source.Length)
                {
                    byte current = _source[_index++];
                    if (current == (byte)'\\')
                    {
                        if (_index >= _source.Length) return false;

                        byte escaped = _source[_index++];
                        if (escaped == (byte)'u')
                        {
                            if (_index + 4 > _source.Length) return false;

                            _index += 4;
                        }

                        continue;
                    }

                    if (current == (byte)'"')
                    {
                        int end = _index - 1;
                        token = _source.Slice(start, end - start);
                        return true;
                    }
                }

                return false;
            }

            public bool TryReadNumberToken(out ReadOnlySpan<byte> token)
            {
                token = default;
                SkipWhitespace();
                if (_index >= _source.Length) return false;

                int start = _index;

                if (_source[_index] == (byte)'-')
                {
                    _index++;
                    if (_index >= _source.Length) return false;
                }

                if (_source[_index] == (byte)'0')
                    _index++;
                else
                {
                    if (!IsDigit(_source[_index])) return false;

                    while (_index < _source.Length && IsDigit(_source[_index])) _index++;
                }

                if (_index < _source.Length && _source[_index] == (byte)'.')
                {
                    _index++;
                    if (_index >= _source.Length || !IsDigit(_source[_index])) return false;

                    while (_index < _source.Length && IsDigit(_source[_index])) _index++;
                }

                if (_index < _source.Length && (_source[_index] == (byte)'e' || _source[_index] == (byte)'E'))
                {
                    _index++;
                    if (_index < _source.Length &&
                        (_source[_index] == (byte)'+' || _source[_index] == (byte)'-')) _index++;

                    if (_index >= _source.Length || !IsDigit(_source[_index])) return false;

                    while (_index < _source.Length && IsDigit(_source[_index])) _index++;
                }

                token = _source.Slice(start, _index - start);
                return token.Length > 0;
            }

            public bool TryReadLiteral(out ReadOnlySpan<byte> literal)
            {
                literal = default;
                SkipWhitespace();
                if (_index >= _source.Length) return false;

                if (TryConsumeLiteral("true", out literal) ||
                    TryConsumeLiteral("false", out literal) ||
                    TryConsumeLiteral("null", out literal))
                    return true;

                return false;
            }

            public bool TrySkipValue()
            {
                SkipWhitespace();
                if (_index >= _source.Length) return false;

                byte current = _source[_index];
                if (current == (byte)'{')
                {
                    _index++;
                    return TrySkipStartedObject();
                }

                if (current == (byte)'[')
                {
                    _index++;
                    return TrySkipStartedArray();
                }

                if (current == (byte)'"') return TryReadStringToken(out _);

                if (current == (byte)'-' || IsDigit(current)) return TryReadNumberToken(out _);

                return TryReadLiteral(out _);
            }

            public bool TrySkipStartedObject()
            {
                while (true)
                {
                    SkipWhitespace();
                    if (_index >= _source.Length) return false;

                    if (_source[_index] == (byte)'}')
                    {
                        _index++;
                        return true;
                    }

                    if (!TryReadStringToken(out _)) return false;

                    if (!TryConsume((byte)':')) return false;

                    if (!TrySkipValue()) return false;

                    if (TryConsume((byte)',')) continue;

                    SkipWhitespace();
                    if (_index < _source.Length && _source[_index] == (byte)'}')
                    {
                        _index++;
                        return true;
                    }

                    return false;
                }
            }

            public bool TrySkipStartedArray()
            {
                while (true)
                {
                    SkipWhitespace();
                    if (_index >= _source.Length) return false;

                    if (_source[_index] == (byte)']')
                    {
                        _index++;
                        return true;
                    }

                    if (!TrySkipValue()) return false;

                    if (TryConsume((byte)',')) continue;

                    SkipWhitespace();
                    if (_index < _source.Length && _source[_index] == (byte)']')
                    {
                        _index++;
                        return true;
                    }

                    return false;
                }
            }

            private bool TryConsumeLiteral(string literal, out ReadOnlySpan<byte> consumed)
            {
                consumed = default;
                int length = literal.Length;
                if (_index + length > _source.Length) return false;

                for (int i = 0; i < length; i++)
                {
                    if (_source[_index + i] != (byte)literal[i])
                        return false;
                }

                consumed = _source.Slice(_index, length);
                _index += length;
                return true;
            }

            private void SkipWhitespace()
            {
                while (_index < _source.Length && IsAsciiWhitespace(_source[_index])) _index++;
            }

            private static bool IsDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';
        }
    }
}
