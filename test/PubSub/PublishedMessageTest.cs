using System;
using System.IO;
using System.Linq;
using Ipfs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Google.Protobuf;

namespace PeerTalk.PubSub
{

    [TestClass]
    public class PublishedMessageTest
    {
        private readonly Peer self = new Peer { Id = "QmXK9VBxaXFuuT29AaPUTgW3jBWZ9JgLVZYdMYTHC6LLAH" };
        private readonly Peer other = new Peer { Id = "QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ" };

        [TestMethod]
        public void RoundTrip()
        {
            var a = new PublishedMessage
            {
                Sender = self,
                SequenceNumberProto = ByteString.CopyFrom(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }),
                DataBytesProto = ByteString.CopyFrom(new byte[] { 0, 1, 0xfe, 0xff }),
            };

            a.TopicsProto.Add("topic");

            var ms = new MemoryStream();
            a.WriteDelimitedTo(ms);
            ms.Position = 0;
            var b = PublishedMessage.Parser.ParseDelimitedFrom(ms);

            CollectionAssert.AreEqual(a.Topics.ToArray(), b.Topics.ToArray());
            Assert.AreEqual(a.Sender, b.Sender);
            CollectionAssert.AreEqual(a.SequenceNumber, b.SequenceNumber);
            CollectionAssert.AreEqual(a.DataBytes, b.DataBytes);
            Assert.AreEqual(a.DataBytes.Length, a.Size);
            Assert.AreEqual(b.DataBytes.Length, b.Size);
        }

        [TestMethod]
        public void MessageID_Is_Unique()
        {
            var a = new PublishedMessage
            {
                Sender = self,
                SequenceNumberProto = ByteString.CopyFrom(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }),
                DataBytesProto = ByteString.CopyFrom(new byte[] { 0, 1, 0xfe, 0xff }),
            };

            a.TopicsProto.Add("topic");

            var b = new PublishedMessage
            {
                Sender = other,
                SequenceNumberProto = ByteString.CopyFrom(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }),
                DataBytesProto = ByteString.CopyFrom(new byte[] { 0, 1, 0xfe, 0xff }),
            };

            b.TopicsProto.Add("topic");

            Assert.AreNotEqual(a.MessageId, b.MessageId);
        }

        [TestMethod]
        [ExpectedException(typeof(NotSupportedException))]
        public void CidNotSupported()
        {
            var _ = new PublishedMessage().Id;
        }

        [TestMethod]
        public void DataStream()
        {
            var msg = new PublishedMessage
            {
                DataBytesProto = ByteString.CopyFrom(new byte[] { 1 }),
            };

            Assert.AreEqual(1, msg.DataStream.ReadByte());
        }
    }
}
