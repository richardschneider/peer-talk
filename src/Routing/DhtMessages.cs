using System.Linq;
using Google.Protobuf;
using Ipfs;

namespace PeerTalk.Routing
{
    /// <summary>
    /// DhtPeerMessage
    /// </summary>
    public partial class DhtPeerMessage
    {
        /// <summary>
        /// Construct <see cref="DhtPeerMessage"/>. from <see cref="Peer"/>.
        /// </summary>
        /// <param name="peer">peer</param>
        /// <returns></returns>
        public static DhtPeerMessage FromPeer(Peer peer)
        {
            var proto = new DhtPeerMessage
            {
                Id = ByteString.CopyFrom(peer.Id.ToArray()),
            };

            if (peer.Addresses != null)
            {
                foreach (var address in peer.Addresses)
                {
                    proto.Addresses.Add(ByteString.CopyFrom(address.WithoutPeerId().ToArray()));
                }
            }

            return proto;
        }

        /// <summary>
        /// Construct <see cref="DhtPeerMessage"/>. from <see cref="Swarm"/>.
        /// </summary>
        /// <param name="swarm">swarm</param>
        /// <returns></returns>
        public static DhtPeerMessage FromSwarm(Swarm swarm)
        {
            var proto = new DhtPeerMessage
            {
                Id = ByteString.CopyFrom(swarm.LocalPeer.Id.ToArray()),
            };

            if (swarm.LocalPeer?.Addresses != null)
            {
                foreach (var address in swarm.LocalPeer.Addresses)
                {
                    proto.Addresses.Add(ByteString.CopyFrom(address.WithoutPeerId().ToArray()));
                }
            }

            return proto;
        }

        /// <summary>
        ///   Convert the message into a <see cref="Peer"/>.
        /// </summary>
        /// <param name="peer"></param>
        /// <returns></returns>
        public bool TryToPeer(out Peer peer)
        {
            peer = null;

            // Sanity checks.
            if (Id == null || Id.Length == 0)
            {
                return false;
            }

            MultiHash id = new MultiHash(Id.ToByteArray());
            peer = new Peer
            {
                Id = id,
            };

            if (Addresses != null)
            {
                MultiAddress x = new MultiAddress($"/ipfs/{id}");
                peer.Addresses = Addresses
                    .Select(bytes =>
                    {
                        try
                        {
                            MultiAddress ma = new MultiAddress(bytes.ToByteArray());
                            ma.Protocols.AddRange(x.Protocols);
                            return ma;
                        }
                        catch
                        {
                            return null;
                        }
                    })
                    .Where(a => a != null)
                    .ToArray();
            }

            return true;
        }
    }
}
