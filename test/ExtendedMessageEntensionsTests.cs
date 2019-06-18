using System.IO;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PeerTalk.Cryptography;

namespace PeerTalk
{
    [TestClass]
    public class ExtendedMessageEntensionsTests
    {
        [TestMethod]
        public void TestWriteAndParseFixed32BigEndianDelimitedTo()
        {
            var msg = new PublicKeyMessage
            {
                Type = KeyType.Rsa,
                Data = ByteString.CopyFromUtf8("I am test data"),
            };

            using (var ms = new MemoryStream())
            {
                msg.WriteFixed32BigEndianDelimitedTo(ms);
                ms.Position = 0;
                var parsed = PublicKeyMessage.Parser.ParseFixed32BigEndianDelimitedFrom(ms);

                parsed.Type.Should().Be(msg.Type);
                parsed.Data.ToBase64().Should().Be(msg.Data.ToBase64());
            }
        }
    }
}
