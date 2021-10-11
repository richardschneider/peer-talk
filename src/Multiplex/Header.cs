using Ipfs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PeerTalk.Multiplex
{
    /// <summary>
    ///   The header of a multiplex message.
    /// </summary>
    /// <remarks>
    ///   The header of a multiplex message contains the <see cref="StreamId"/> and
    ///   <see cref="PacketType"/> encoded as a <see cref="Varint">variable integer</see>.
    /// </remarks>
    /// <seealso href="https://github.com/libp2p/mplex"/>
    public struct Header
    {
        /// <summary>
        ///   The largest possible value of a <see cref="StreamId"/>.
        /// </summary>
        /// <value>
        ///  2^60 -1
        /// </value>
        public const UInt64 MaxStreamId = ((UInt64)1073741824 * (UInt64)1073741824) - 1;

        /// <summary>
        ///   The smallest possible value of a <see cref="StreamId"/>.
        /// </summary>
        /// <value>
        ///   Zero.
        /// </value>
        public const UInt64 MinStreamId = 0;

        /// <summary>
        ///   The stream identifier.
        /// </summary>
        public UInt64 StreamId;

        /// <summary>
        ///   The purpose of the multiplex message.
        /// </summary>
        /// <value>
        ///   One of the <see cref="PacketType"/> enumeration values.
        /// </value>
        public PacketType PacketType;

        /// <summary>
        ///   Writes the header to the specified <see cref="Stream"/>.
        /// </summary>
        /// <param name="stream">
        ///   The destination <see cref="Stream"/> for the header.
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///   A task that represents the asynchronous operation.
        /// </returns>
        public async Task WriteAsync(Stream stream, CancellationToken cancel = default(CancellationToken))
        {
            var header = (StreamId << 3) | (UInt64)PacketType;
            await Varint.WriteVarintAsync(stream, unchecked((Int64)header /*Varint should really be taking uints...*/), cancel).ConfigureAwait(false);
        }

        /// <summary>
        ///   Reads the header from the specified <see cref="Stream"/>.
        /// </summary>
        /// <param name="stream">
        ///   The source <see cref="Stream"/> for the header.
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///   A task that represents the asynchronous operation.  The task's result
        ///   is the decoded <see cref="Header"/>.
        /// </returns>
        public static async Task<Header> ReadAsync(Stream stream, CancellationToken cancel = default(CancellationToken))
        {
            var varint = unchecked( (UInt64)await Varint.ReadVarint64Async(stream, cancel).ConfigureAwait(false)  /*Varint should really be returning uints...*/);
            return new Header
            {
                StreamId = varint >> 3,
                PacketType = (PacketType)((byte)varint & 0x7)
            };
        }
    }
}
