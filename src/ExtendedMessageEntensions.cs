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
            var lengthBuffer = new byte[4];
            input.Read(lengthBuffer, 0, lengthBuffer.Length);
            if (BitConverter.IsLittleEndian)
            {
                Span<byte> lengthSpan = lengthBuffer;
                lengthSpan.Reverse();
            }

            var length = (int)BitConverter.ToUInt32(lengthBuffer, 0);
            var dataBuffer = new byte[length];
            input.Read(dataBuffer, 0, length);
            return parser.ParseFrom(dataBuffer, 0, length);
        }
    }
}
