﻿using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Logging;
using Google.Protobuf;
using Ipfs;
using Org.BouncyCastle.Security;
using PeerTalk.Cryptography;
using PeerTalk.Protocols;
using Semver;

namespace PeerTalk.SecureCommunication
{
    /// <summary>
    ///   Creates a secure connection with a peer.
    /// </summary>
    public class Secio1 : IEncryptionProtocol
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(Secio1));

        /// <inheritdoc />
        public string Name { get; } = "secio";

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
            await EncryptAsync(connection, cancel).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Stream> EncryptAsync(PeerConnection connection, CancellationToken cancel = default(CancellationToken))
        {
            var stream = connection.Stream;
            var localPeer = connection.LocalPeer;
            connection.RemotePeer = connection.RemotePeer ?? new Peer();
            var remotePeer = connection.RemotePeer;

            // =============================================================================
            // step 1. Propose -- propose cipher suite + send pubkey + nonce
            var rng = new SecureRandom();
            var localNonce = new byte[16];
            rng.NextBytes(localNonce);
            var localProposal = new Secio1Propose
            {
                Nonce = ByteString.CopyFrom(localNonce),
                Exchanges = "P-256,P-384,P-521",
                Ciphers = "AES-256,AES-128",
                Hashes = "SHA256,SHA512",
                PublicKey = ByteString.FromBase64(localPeer.PublicKey),
            };

            localProposal.WriteFixed32BigEndianDelimitedTo(stream);
            await stream.FlushAsync().ConfigureAwait(false);

            // =============================================================================
            // step 1.1 Identify -- get identity from their key
            var remoteProposal = Secio1Propose.Parser.ParseFixed32BigEndianDelimitedFrom(stream);
            var ridAlg = (remoteProposal.PublicKey.Length <= 48) ? "identity" : "sha2-256";
            var remoteId = MultiHash.ComputeHash(remoteProposal.PublicKey.ToByteArray(), ridAlg);
            if (remotePeer.Id == null)
            {
                remotePeer.Id = remoteId;
            }
            else if (remoteId != remotePeer.Id)
            {
                throw new Exception($"Expected peer '{remotePeer.Id}', got '{remoteId}'");
            }

            // =============================================================================
            // step 1.2 Selection -- select/agree on best encryption parameters
            // to determine order, use cmp(H(remote_pubkey||local_rand), H(local_pubkey||remote_rand)).
            //   oh1 := hashSha256(append(proposeIn.GetPubkey(), nonceOut...))
            //   oh2 := hashSha256(append(myPubKeyBytes, proposeIn.GetRand()...))
            //   order := bytes.Compare(oh1, oh2)
            byte[] oh1;
            byte[] oh2;
            using (var hasher = MultiHash.GetHashAlgorithm("sha2-256"))
            using (var ms = new MemoryStream())
            {
                ms.Write(remoteProposal.PublicKey.ToByteArray(), 0, remoteProposal.PublicKey.Length);
                ms.Write(localProposal.Nonce.ToByteArray(), 0, localProposal.Nonce.Length);
                ms.Position = 0;
                oh1 = hasher.ComputeHash(ms);
            }
            using (var hasher = MultiHash.GetHashAlgorithm("sha2-256"))
            using (var ms = new MemoryStream())
            {
                ms.Write(localProposal.PublicKey.ToByteArray(), 0, localProposal.PublicKey.Length);
                ms.Write(remoteProposal.Nonce.ToByteArray(), 0, remoteProposal.Nonce.Length);
                ms.Position = 0;
                oh2 = hasher.ComputeHash(ms);
            }
            int order = 0;
            for (int i = 0; order == 0 && i < oh1.Length; ++i)
            {
                order = oh1[i].CompareTo(oh2[i]);
            }
            if (order == 0)
            {
                throw new Exception("Same keys and nonces; talking to self");
            }

            var curveName = SelectBest(order, localProposal.Exchanges, remoteProposal.Exchanges);
            if (curveName == null)
            {
                throw new Exception("Cannot agree on a key exchange.");
            }

            var cipherName = SelectBest(order, localProposal.Ciphers, remoteProposal.Ciphers);
            if (cipherName == null)
            {
                throw new Exception("Cannot agree on a chipher.");
            }

            var hashName = SelectBest(order, localProposal.Hashes, remoteProposal.Hashes);
            if (hashName == null)
            {
                throw new Exception("Cannot agree on a hash.");
            }

            // =============================================================================
            // step 2. Exchange -- exchange (signed) ephemeral keys. verify signatures.

            // Generate EphemeralPubKey
            var localEphemeralKey = EphermalKey.Generate(curveName);
            var localEphemeralPublicKey = localEphemeralKey.PublicKeyBytes();

            // Send Exchange packet
            var localExchange = new Secio1Exchange();
            using (var ms = new MemoryStream())
            {
                localProposal.WriteTo(ms);
                remoteProposal.WriteTo(ms);
                ms.Write(localEphemeralPublicKey, 0, localEphemeralPublicKey.Length);
                localExchange.Signature = ByteString.CopyFrom(connection.LocalPeerKey.Sign(ms.ToArray()));
            }
            localExchange.EPublicKey = ByteString.CopyFrom(localEphemeralPublicKey);
            localExchange.WriteFixed32BigEndianDelimitedTo(stream);
            await stream.FlushAsync(cancel).ConfigureAwait(false);

            // Receive their Exchange packet.  If nothing, then most likely the
            // remote has closed the connection because it does not like us.
            var remoteExchange = Secio1Exchange.Parser.ParseFixed32BigEndianDelimitedFrom(stream);
            if (remoteExchange == null)
            {
                throw new Exception("Remote refuses the SECIO exchange.");
            }

            // =============================================================================
            // step 2.1. Verify -- verify their exchange packet is good.
            var remotePeerKey = Key.CreatePublicKeyFromIpfs(remoteProposal.PublicKey.ToByteArray());
            using (var ms = new MemoryStream())
            {
                remoteProposal.WriteTo(ms);
                localProposal.WriteTo(ms);
                ms.Write(remoteExchange.EPublicKey.ToByteArray(), 0, remoteExchange.EPublicKey.Length);
                remotePeerKey.Verify(ms.ToArray(), remoteExchange.Signature.ToByteArray());
            }
            var remoteEphemeralKey = EphermalKey.CreatePublicKeyFromIpfs(curveName, remoteExchange.EPublicKey.ToByteArray());

            // =============================================================================
            // step 2.2. Keys -- generate keys for mac + encryption
            var sharedSecret = localEphemeralKey.GenerateSharedSecret(remoteEphemeralKey);
            StretchedKey.Generate(cipherName, hashName, sharedSecret, out StretchedKey k1, out StretchedKey k2);
            if (order < 0)
            {
                StretchedKey tmp = k1;
                k1 = k2;
                k2 = tmp;
            }

            // =============================================================================
            // step 2.3. MAC + Cipher -- prepare MAC + cipher
            var secureStream = new Secio1Stream(stream, cipherName, hashName, k1, k2);

            // =============================================================================
            // step 3. Finish -- send expected message to verify encryption works (send local nonce)

            // Send thier nonce,
            await secureStream.WriteAsync(remoteProposal.Nonce.ToByteArray(), 0, remoteProposal.Nonce.Length, cancel).ConfigureAwait(false);
            await secureStream.FlushAsync(cancel).ConfigureAwait(false);

            // Receive our nonce.
            var verification = new byte[localNonce.Length];
            await secureStream.ReadAsync(verification, 0, verification.Length, cancel);
            if (!localNonce.SequenceEqual(verification))
            {
                throw new Exception($"SECIO verification message failure.");
            }

            log.Debug($"Secure session with {remotePeer}");

            // Fill in the remote peer
            remotePeer.PublicKey = Convert.ToBase64String(remoteProposal.PublicKey.ToByteArray());

            // Set secure task done
            connection.Stream = secureStream;
            connection.SecurityEstablished.SetResult(true);
            return secureStream;
        }

        private string SelectBest(int order, string local, string remote)
        {
            var first = order < 0 ? remote.Split(',') : local.Split(',');
            string[] second = order < 0 ? local.Split(',') : remote.Split(',');
            return first.FirstOrDefault(f => second.Contains(f));
        }
    }
}
