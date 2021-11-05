using ProtoBuf;

/*
syntax = "proto3";
package pb;

message NoiseHandshakePayload {
	bytes identity_key = 1;
	bytes identity_sig = 2;
	bytes data = 3;
}
*/

namespace PeerTalk.SecureCommunication.Noise
{
    [ProtoContract]
    class NoiseHandshakePayload
    {
        #pragma warning disable 0649
        [ProtoMember(1)]
        public byte[] IdentityKey;

        [ProtoMember(2)]
        public byte[] IdentitySig;

        [ProtoMember(3)]
        public byte[] Data;
        #pragma warning restore 0649
    }
}
