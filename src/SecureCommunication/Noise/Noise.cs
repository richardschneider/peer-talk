using Common.Logging;
using Ipfs;
using Ipfs.Registry;
using Noise;
using Org.BouncyCastle.Security;
using PeerTalk.Cryptography;
using PeerTalk.Protocols;
using ProtoBuf;
using Semver;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PeerTalk.SecureCommunication
{
    /// <summary>
    ///   Creates a secure connection with a peer.
    /// </summary>
    public class Noise : IEncryptionProtocol
    {
        static ILog log = LogManager.GetLogger(typeof(Noise));

        /// <inheritdoc />
        public string Name { get; } = "noise";

        /// <inheritdoc />
        public SemVersion Version { get; } = new SemVersion(1, 0);

        /// <inheritdoc />
        public override string ToString()
        {
            return $"/{Name}";
        }

        //private static string payloadSigPrefix = "noise-libp2p-static-key:";

        /// <inheritdoc />
        public async Task ProcessMessageAsync(PeerConnection connection, Stream stream, CancellationToken cancel = default(CancellationToken))
        {
            await EncryptAsync(connection, cancel).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Stream> EncryptAsync(PeerConnection connection, CancellationToken cancel = default(CancellationToken))
        {
            if (connection.IsIncoming)
            {
                throw new NotImplementedException("Incoming Noise Encryption Handshakes not implemented");
            }

            log.Info($"Setting up noise protocol for {connection.RemotePeer.Id}");

            var stream = connection.Stream;
            var localPeer = connection.LocalPeer;
            connection.RemotePeer = connection.RemotePeer ?? new Peer();
            var remotePeer = connection.RemotePeer;

            var protocol = new Protocol(
              HandshakePattern.XX,
              CipherFunction.ChaChaPoly,
              HashFunction.Sha256,
              PatternModifiers.None
            );

            var psk = new byte[32];

            // Generate a random 32-byte pre-shared secret key.
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(psk);
            }

            try
            {
                using (var kp = KeyPair.Generate())
                {
                    var buffer = new byte[Protocol.MaxMessageLength];
                    var readbuffer = new byte[Protocol.MaxMessageLength];

                    using (var state = protocol.Create(initiator: !connection.IsIncoming, s: kp.PrivateKey))
                    {
                        if (!connection.IsIncoming)
                        {

                            log.Info($"Sending Noise Handshake {connection.RemotePeer.Id}");

                            var (bytesWritten, _, _) = state.WriteMessage(null, buffer);
                            await WriteStreamMessageAsync(stream, buffer, bytesWritten);

                            log.Info($"Waiting For Noise Handshake {connection.RemotePeer.Id}");

                            // Receive the second handshake message from the server.
                            var received = await ReadStreamMessageAsync(stream, buffer, Protocol.MaxMessageLength);
                            var (_, _, transport) = state.ReadMessage(new ReadOnlySpan<byte>(buffer, 0, received), readbuffer);

                            log.Info($"Noise Handshake Done {connection.RemotePeer.Id}");
                        }
                    }
                }
            } catch (Exception e)
            {
                log.Error($"Something failed {e.Message}", e);
            }

            await Task.Yield();

            return new System.IO.MemoryStream();//This should be the encrypted stream;
        }

        /**
         * Writes a message to the stream and prefixes it with the message length
         */
        private static async Task WriteStreamMessageAsync(Stream stream, byte[] buffer, int bytes)
        {
            if (bytes > UInt16.MaxValue)
            {
                throw new ArgumentOutOfRangeException("Message size exceeds uint16 bytes");
            }

            var prefix = BitConverter.GetBytes((UInt16)bytes);
            //We Want Big Endian
            if (BitConverter.IsLittleEndian)
                Array.Reverse(prefix);

            await stream.WriteAsync(prefix, 0, 2);

            await stream.WriteAsync(buffer, 0, bytes);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        /**
         * Reads a message from the stream which is prefixed with the message length
         */
        private static async Task<int> ReadStreamMessageAsync(Stream stream, byte[] buffer, UInt16 bufferSize)
        {
            var prefix = new byte[2];

            if (await stream.ReadAsync(prefix, 0, 1) == 0)
            {
                throw new Exception("Connection Closed");
            }
            if (await stream.ReadAsync(prefix, 1, 1) == 0)
            {
                throw new Exception("Connection Closed");
            }

            //We Want Big Endian
            if (BitConverter.IsLittleEndian)
                Array.Reverse(prefix);
            var messageSize = BitConverter.ToUInt16(prefix, 0);

            if (bufferSize < messageSize)
            {
                throw new Exception($"Recieved message ({messageSize}) larger than buffer ({bufferSize})");
            }

            var readTotal = 0;
            while( messageSize > readTotal )
            {
                var read = await stream.ReadAsync(buffer, readTotal, messageSize - readTotal);
                if (read == 0)
                {
                    throw new Exception("Connection Closed Mid-read");
                }
                readTotal += read;
            }

            return readTotal;
        }
    }
}
