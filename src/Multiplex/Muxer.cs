using Common.Logging;
using Ipfs;
using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PeerTalk.Multiplex
{
    /// <summary>
    ///   Supports multiple protocols over a single channel (stream).
    /// </summary>
    /// <remarks>
    ///   See <see href="https://github.com/libp2p/mplex"/> for the spec.
    /// </remarks>
    public class Muxer
    {
        static ILog log = LogManager.GetLogger(typeof(Muxer));

        /// <summary>
        ///   The next stream ID to create.
        /// </summary>
        /// <value>
        ///   The session initiator allocates even IDs and the session receiver allocates odd IDs.
        /// </value>
        private UInt64 NextStreamId { get; set; } = 0;

        /// <summary>
        ///   The signle channel to exchange protocol messages.
        /// </summary>
        /// <value>
        ///   A <see cref="Stream"/> to exchange protocol messages.
        /// </value>
        internal Stream Channel { get; set; }

        /// <summary>
        ///   The peer connection.
        /// </summary>
        /// <value>
        ///   The peer connection that owns this muxer.
        /// </value>
        private PeerConnection Connection { get; set; }

        /// <summary>
        ///   Raised when the remote end creates a new stream.
        /// </summary>
        public event EventHandler<Substream> SubstreamCreated;

        /// <summary>
        ///   Raised when the remote end closes a stream.
        /// </summary>
        private event EventHandler<Substream> SubstreamClosed;

        private readonly AsyncLock ChannelWriteLock = new AsyncLock();

        /// <summary>
        ///   The substreams that are open.
        /// </summary>
        /// <value>
        ///   The key is stream ID and the value is a <see cref="Substream"/>.
        /// </value>
        private readonly ConcurrentDictionary<SubstreamId, Substream> Substreams = new ConcurrentDictionary<SubstreamId, Substream>();
        
        /// <summary>
        ///   Multiplexes the provided stream
        /// </summary>
        public Muxer(Stream channel, PeerConnection connection)
        {
            Channel = channel;
            Connection = connection;
        }

        /// <summary>
        ///   Creates a new stream with the specified name.
        /// </summary>
        /// <param name="name">
        ///   A name for the stream.
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///   A duplex stream.
        /// </returns>
        public async Task<Substream> CreateStreamAsync(string name = "", CancellationToken cancel = default(CancellationToken))
        {
            var streamId = NextStreamId;
            NextStreamId += 1;
            var substream = new Substream
            {
                Id = streamId,
                Name = name,
                Muxer = this,
                SentMessageType = PacketType.MessageInitiator,
            };

            var substreamId = new SubstreamId(true, streamId);

            log.Debug($"I want to create stream #{substreamId} w/ {Connection.RemotePeer.Id}");
            Substreams.TryAdd(substreamId, substream);

            // Tell the other side about the new stream.
            using (await AcquireWriteAccessAsync().ConfigureAwait(false))
            {
                var header = new Header { StreamId = streamId, PacketType = PacketType.NewStream };
                var wireName = Encoding.UTF8.GetBytes(name);
                await header.WriteAsync(Channel, cancel).ConfigureAwait(false);
                await Channel.WriteVarintAsync(wireName.Length, cancel).ConfigureAwait(false);
                await Channel.WriteAsync(wireName, 0, wireName.Length).ConfigureAwait(false);
                await Channel.FlushAsync().ConfigureAwait(false);
            }
            return substream;
        }

        /// <summary>
        ///   Remove the stream.
        /// </summary>
        /// <remarks>
        ///   Internal method called by Substream.Dispose().
        /// </remarks>
        public async Task RemoveStreamAsync(Substream stream, CancellationToken cancel = default(CancellationToken))
        {
            //FIXME: Initiator is not known here
            log.Error("FIXME: RemoveStreamAsync");

            //var substreamId = new SubstreamId(?, stream.Id);
            //if (Substreams.TryRemove(substreamId, out Substream _))
            //{
            //    // Tell the other side.
            //    using (await AcquireWriteAccessAsync().ConfigureAwait(false))
            //    {
            //        var header = new Header
            //        {
            //            StreamId = stream.Id,
            //            PacketType = PacketType.CloseInitiator
            //        };
            //        await header.WriteAsync(Channel, cancel).ConfigureAwait(false);
            //        Channel.WriteByte(0); // length
            //        await Channel.FlushAsync().ConfigureAwait(false);
            //    }
            //}

            await Task.Yield();
        }

        /// <summary>
        ///   Read the multiplex packets.
        /// </summary>
        /// <param name="cancel"></param>
        /// <returns></returns>
        /// <remarks>
        ///   A background task that reads and processes the multiplex packets while
        ///   the <see cref="Channel"/> is open and not <paramref name="cancel">cancelled</paramref>.
        ///   <para>
        ///   Any encountered errors will close the <see cref="Channel"/>.
        ///   </para>
        /// </remarks>
        public async Task ProcessRequestsAsync(CancellationToken cancel = default(CancellationToken))
        {
            try
            {
                while (Channel.CanRead && !cancel.IsCancellationRequested)
                {
                    // Read the packet prefix.
                    var header = await Header.ReadAsync(Channel, cancel).ConfigureAwait(false);
                    var length = await Varint.ReadVarint32Async(Channel, cancel).ConfigureAwait(false);
                    if (log.IsTraceEnabled)
                        log.TraceFormat("received '{0}', stream={1}, length={2}", header.PacketType, header.StreamId, length);

                    // Read the payload.
                    var payload = new byte[length];
                    await Channel.ReadExactAsync(payload, 0, length, cancel).ConfigureAwait(false);

                    var iAmInitiator = ((byte)header.PacketType & 0x01) == 0x01;
                    var substreamId = new SubstreamId(iAmInitiator, header.StreamId);

                    // Process the packet
                    Substreams.TryGetValue(substreamId, out Substream substream);
                    switch (header.PacketType)
                    {
                        case PacketType.NewStream:
                            if (substream != null)
                            {
                                log.Warn($"Stream {substreamId} already exists");
                                continue;
                            }
                            substream = new Substream
                            {
                                Id = header.StreamId,
                                Name = Encoding.UTF8.GetString(payload),
                                Muxer = this
                            };
                            log.Debug($"Asked to create stream #{substreamId} w/ {Connection.RemotePeer.Id}");
                            if (!Substreams.TryAdd(substreamId, substream))
                            {
                                // Should not happen.
                                throw new Exception($"Stream {substream.Id} already exists");
                            }
                            SubstreamCreated?.Invoke(this, substream);
                            break;

                        case PacketType.MessageInitiator:
                        case PacketType.MessageReceiver:
                            if (substream == null)
                            {
                                log.Warn($"Message to unknown stream #{substreamId}");
                                continue;
                            }
                            substream.AddData(payload);
                            break;

                        // Closing is one sided, it means the other side is done sending and the stream should EOF after consuming existing content. You can still send the other way
                        case PacketType.CloseInitiator:
                        case PacketType.CloseReceiver:
                            log.Debug($"Asked to close stream #{substreamId} w/ {Connection.RemotePeer.Id}");
                            if (substream == null)
                            {
                                log.Warn($"Close of unknown stream #{header.StreamId} w/ {Connection.RemotePeer.Id} due to {header.PacketType}");
                                continue;
                            }
                            substream.NoMoreData();
                            Substreams.TryRemove(substreamId, out Substream _);
                            SubstreamClosed?.Invoke(this, substream);
                            break;
                        // Hard cut, existing unread data should be discarded and sending/receiving is an error.
                        case PacketType.ResetInitiator:
                        case PacketType.ResetReceiver:
                            log.Warn($"Asked to reset stream #{substreamId}  w/ {Connection.RemotePeer.Id}, this is usually in response to a protocol error");
                            if (substream == null)
                            {
                                log.Warn($"Reset of unknown stream #{substreamId} w/ {Connection.RemotePeer.Id} due to {header.PacketType}");
                                continue;
                            }
                            substream.NoMoreData();
                            Substreams.TryRemove(substreamId, out Substream _);
                            SubstreamClosed?.Invoke(this, substream);
                            break;

                        default:
                            throw new InvalidDataException($"Unknown Muxer packet type '{header.PacketType}' from {Connection.RemotePeer.Id}.");
                    }
                }
            }
            catch (EndOfStreamException)
            {
                // eat it
            }
            catch (IOException)
            {
                // eat it
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset)
            {
                // eat it
            }
            catch (Exception) when (cancel.IsCancellationRequested)
            {
                // eat it
            }
            catch (Exception e)
            {
                // Log error if the channel is not closed.
                if (Channel.CanRead || Channel.CanWrite)
                {
                    log.Error("failed", e);
                }
            }

            // Some of the tests do not pass a connection.
            if (Connection != null)
                Connection.Dispose();
            else if (Channel != null)
                Channel.Dispose();

            // Dispose of all the substreams.
            var streams = Substreams.Values.ToArray();
            Substreams.Clear();
            foreach (var stream in streams)
            {
                stream.Dispose();
            }
        }

        /// <summary>
        ///   Acquire permission to write to the Channel.
        /// </summary>
        /// <returns>
        ///   A task that represents the asynchronous get operation. The task's value
        ///   is an <see cref="IDisposable"/> that releases the lock.
        /// </returns>
        public Task<IDisposable> AcquireWriteAccessAsync()
        {
            return ChannelWriteLock.LockAsync();
        }
    }
}
