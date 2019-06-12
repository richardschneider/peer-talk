using System;
using System.Collections.Generic;
using System.IO;
using Ipfs;

namespace PeerTalk.PubSub
{
    /// <summary>
    ///  A published messaged for a topic(s).
    /// </summary>
    /// <seealso ref="https://github.com/libp2p/specs/blob/master/pubsub/README.md"/>
    /// <remarks>
    ///   TODO: Sender should really be called Author.
    ///   
    /// </remarks>
    public partial class PublishedMessage : IPublishedMessage
    {
        private Peer _sender;
        /// <inheritdoc />
        public Peer Sender
        {
            get
            {
                if (_sender == null)
                {
                    _sender = new Peer { Id = new MultiHash(From.ToByteArray()) };
                }

                return _sender;
            }
            set => _sender = value;
        }

        /// <summary>
        ///   Who sent the the message.
        /// </summary>
        public Peer Forwarder { get; set; }

        private string _messageId;
        /// <summary>
        ///   A universally unique id for the message.
        /// </summary>
        /// <value>
        ///   The sender's ID concatenated with the <see cref="SequenceNumber"/>.
        /// </value>
        public string MessageId
        {
            get
            {
                if (_messageId == null)
                {
                    _messageId = Sender.Id.ToBase58() + SequenceNumber.ToHexString();
                }

                return _messageId;
            }
        }

        /// <summary>>
        ///   NOT SUPPORTED, use <see cref="MessageId"/>.
        /// </summary>
        /// <exception cref="NotSupportedException">
        ///   A published message does not have a content id.
        /// </exception>
        public Cid Id => throw new NotSupportedException();

        /// <inheritdoc />
        public Stream DataStream => new MemoryStream(DataBytes, false);

        /// <inheritdoc />
        public long Size => DataBytes.Length;

        /// <inheritdoc />
        public byte[] SequenceNumber => SequenceNumberProto.ToByteArray();

        /// <inheritdoc />
        public byte[] DataBytes => DataBytesProto.ToByteArray();

        /// <inheritdoc />
        public IEnumerable<string> Topics => TopicsProto;
    }
}
