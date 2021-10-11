using System;

namespace PeerTalk.Multiplex
{
    internal struct SubstreamId
    {
        public bool Initiator; // Whether we initiated the stream, id's can be used by both the near and remote end as seperated streams
        public UInt64 Id; // Max 2^60-1

        public SubstreamId(bool initiator, UInt64 id)
        {
            Initiator = initiator;
            Id = id;
        }

        public override string ToString()
        {
            return (Initiator ? "l" : "r") + Id.ToString();
        }
    }
}
