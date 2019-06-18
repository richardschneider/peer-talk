using System;
using System.IO;
using System.Linq;
using Google.Protobuf;

namespace PeerTalk
{
    /// <summary>
    /// Extend MessageEntensions to support Fixed32BigEndian length
    /// </summary>
    public static class ExtendedMessageEntensions
    {
        /// <summary>
        /// Writes the fixed32-big-endian length and then data of the given message to a stream.
        /// </summary>
        /// <param name="message">The message to write to the stream.</param>
        /// <param name="output">The stream to write to.</param>
        public static void WriteFixed32BigEndianDelimitedTo(this IMessage message, Stream output)
        {
            ProtoPreconditions.CheckNotNull(message, nameof(message));
            ProtoPreconditions.CheckNotNull(output, nameof(output));
            CodedOutputStream codedOutput = new CodedOutputStream(output);
            var bigEndianBytes = BitConverter.GetBytes((uint)message.CalculateSize());
            if (BitConverter.IsLittleEndian)
            {
                Span<byte> span = bigEndianBytes;
                span.Reverse();
            }

            output.Write(bigEndianBytes, 0, bigEndianBytes.Length);
            message.WriteTo(codedOutput);
            codedOutput.Flush();
        }

        /// <summary>
        /// Parses a fixed32-big-endian-length-delimited message from the given stream.
        /// The stream is expected to contain a length and then the data. Only the amount of data specified by the length will be consumed.
        /// </summary>
        /// <typeparam name="T">Message type.</typeparam>
        /// <param name="parser">Message parser.</param>
        /// <param name="input">The stream to parse.</param>
        /// <returns>The parsed message.</returns>
        public static T ParseFixed32BigEndianDelimitedFrom<T>(this MessageParser<T> parser, Stream input) where T : IMessage<T>
        {
            using (var ms = new MemoryStream())
            {
                input.CopyTo(ms);
                ms.Position = 0;

                var bytes = ms.ToArray();
                Span<byte> span = bytes;
                var lenghSlice = span.Slice(0, 4);
                if (BitConverter.IsLittleEndian)
                {
                    lenghSlice.Reverse();
                }

                var length = (int)BitConverter.ToUInt32(bytes, 0);

                return parser.ParseFrom(bytes, 4, length);
            }
        }
    }
}
