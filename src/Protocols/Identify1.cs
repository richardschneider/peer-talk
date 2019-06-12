using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Logging;
using Google.Protobuf;
using Ipfs;
using Semver;

namespace PeerTalk.Protocols
{
    /// <summary>
    ///   Identifies the peer.
    /// </summary>
    public class Identify1 : IPeerProtocol
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(Identify1));

        /// <inheritdoc />
        public string Name { get; } = "ipfs/id";

        /// <inheritdoc />
        public SemVersion Version { get; } = new SemVersion(1, 0);

        /// <inheritdoc />
        public override string ToString()
        {
            return $"/{Name}/{Version}";
        }

        /// <inheritdoc />
        public async Task ProcessMessageAsync(PeerConnection connection, Stream stream, CancellationToken cancel = default(CancellationToken))
        {

            // Send our identity.
            log.Debug("Sending identity to " + connection.RemoteAddress);
            var peer = connection.LocalPeer;
            var res = new Identify
            {
                ProtocolVersion = peer.ProtocolVersion ?? string.Empty,
                AgentVersion = peer.AgentVersion ?? string.Empty,
            };

            if (connection.RemoteAddress != null)
            {
                res.ObservedAddress = ByteString.CopyFrom(connection.RemoteAddress.ToArray());
            }

            if (peer?.Addresses != null)
            {
                foreach (var address in peer?.Addresses)
                {
                    res.ListenAddresses.Add(ByteString.CopyFrom(address.WithoutPeerId().ToArray()));
                }
            }

            if (peer?.PublicKey != null)
            {
                res.PublicKey = ByteString.FromBase64(peer.PublicKey);
            }

            res.WriteDelimitedTo(stream);

            await stream.FlushAsync().ConfigureAwait(false);
        }

        /// <summary>
        ///   Gets the identity information of the remote peer.
        /// </summary>
        /// <param name="connection">
        ///   The currenty connection to the remote peer.
        /// </param>
        /// <param name="cancel"></param>
        /// <returns></returns>
        public async Task<Peer> GetRemotePeer(PeerConnection connection, CancellationToken cancel)
        {
            var muxer = await connection.MuxerEstablished.Task.ConfigureAwait(false);
            log.Debug("Get remote identity");
            Peer remote = connection.RemotePeer;
            using (var stream = await muxer.CreateStreamAsync("id", cancel).ConfigureAwait(false))
            {
                await connection.EstablishProtocolAsync("/multistream/", stream, cancel).ConfigureAwait(false);
                await connection.EstablishProtocolAsync("/ipfs/id/", stream, cancel).ConfigureAwait(false);

                var info = Identify.Parser.ParseDelimitedFrom(stream);
                if (remote == null)
                {
                    remote = new Peer();
                    connection.RemotePeer = remote;
                }

                remote.AgentVersion = info.AgentVersion;
                remote.ProtocolVersion = info.ProtocolVersion;
                if (info.PublicKey == null || info.PublicKey.Length == 0)
                {
                    throw new InvalidDataException("Public key is missing.");
                }
                remote.PublicKey = info.PublicKey.ToBase64();
                if (remote.Id == null)
                {
                    remote.Id = MultiHash.ComputeHash(info.PublicKey.ToByteArray());
                }

                if (info.ListenAddresses != null)
                {
                    remote.Addresses = info.ListenAddresses
                        .Select(b => MultiAddress.TryCreate(b.ToByteArray()))
                        .Where(a => a != null)
                        .ToList();
                }
                if (remote.Addresses.Count() == 0)
                {
                    log.Warn($"No listen address for {remote}");
                }
            }

            // TODO: Verify the Peer ID

            connection.IdentityEstablished.TrySetResult(remote);

            log.Debug($"Peer id '{remote}' of {connection.RemoteAddress}");
            return remote;
        }
    }
}
