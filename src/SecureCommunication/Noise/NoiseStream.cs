

using Common.Logging;
using Noise;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PeerTalk.SecureCommunication.Noise
{
    /// <summary>
 ///   Stream wrapper that communicates using Noise
 /// </summary>
    public class NoiseStream : Stream
    {
        static ILog log = LogManager.GetLogger(typeof(NoiseStream));

        private Transport transport;
        private Stream stream;

        private byte[] inBlock;
        private int inBlockOffset;
        MemoryStream outStream = new MemoryStream();

        /// <summary>
        ///   Stream wrapper that communicates using Noise
        /// </summary>
        /// <param name="transport">
        ///   Noise transport state
        /// </param>
        /// <param name="stream">
        ///   The backing stream this communicates on
        /// </param>
        public NoiseStream(Transport transport, Stream stream)
        {
            this.transport = transport;
            this.stream = stream;
        }

        /// <inheritdoc />
        public override bool CanRead => stream.CanRead;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => stream.CanRead;

        /// <inheritdoc />
        public override bool CanTimeout => false;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
#pragma warning disable VSTHRD002 
            return ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002 
        }

        /// <inheritdoc />
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int total = 0;
            while (count > 0)
            {
                // Does the current packet have some unread data?
                if (inBlock != null && inBlockOffset < inBlock.Length)
                {
                    var n = Math.Min(inBlock.Length - inBlockOffset, count);
                    Array.Copy(inBlock, inBlockOffset, buffer, offset, n);
                    total += n;
                    count -= n;
                    offset += n;
                    inBlockOffset += n;
                }
                // Otherwise, wait for a new block of data.
                else
                {
                    inBlock = await ReadPacketAsync(cancellationToken);
                    inBlockOffset = 0;
                }
            }

            return total;
        }

        /// <summary>
        ///   Read an encrypted and signed packet.
        /// </summary>
        /// <returns>
        ///   The plain text as an array of bytes.
        /// </returns>
        /// <remarks>
        ///   A packet consists of a [uint32 length of packet | encrypted body | hmac signature of encrypted body].
        /// </remarks>
        async Task<byte[]> ReadPacketAsync(CancellationToken cancel)
        {
            var prefix = new byte[2];

            if (await stream.ReadAsync(prefix, 0, 1, cancel) == 0)
            {
                throw new Exception("Connection Closed");
            }
            if (await stream.ReadAsync(prefix, 1, 1, cancel) == 0)
            {
                throw new Exception("Connection Closed");
            }

            //We Want Big Endian
            if (BitConverter.IsLittleEndian)
                Array.Reverse(prefix);
            var messageSize = BitConverter.ToUInt16(prefix, 0);

            var buffer = new byte[messageSize];

            var readTotal = 0;
            while (messageSize > readTotal)
            {
                var read = await stream.ReadAsync(buffer, readTotal, messageSize - readTotal, cancel);
                if (read == 0)
                {
                    throw new Exception("Connection Closed Mid-read");
                }
                readTotal += read;
            }

            var plaintextbuffer = new byte[messageSize];

            //Do that pesky decrypt thing
            var plaintextBytes = transport.ReadMessage(buffer, plaintextbuffer);


            log.Debug($"Recieved via Noise {messageSize} bytes");

            //This is really inefficient but gets the job done
            return new ReadOnlySpan<byte>(plaintextbuffer, 0, plaintextBytes).ToArray();
        }

        /// <inheritdoc />
        public override void Flush()
        {
#pragma warning disable VSTHRD002 
            FlushAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002 
        }

        /// <inheritdoc />
        public override async Task FlushAsync(CancellationToken cancel)
        {
            if (outStream.Length == 0)
                return;

            var data = outStream.ToArray();  // plain text
            outStream.SetLength(0);
            var dataOffset = 0;

            // We can't send more than 2^16 (64k) bytes in one message so we have to chunk.
            while (dataOffset < data.Length)
            {
                const int TagSize = 16;
                var chunksize = Math.Min(data.Length - dataOffset, Protocol.MaxMessageLength - TagSize);

                var buffer = new byte[chunksize+TagSize];
                var cipherBytes = transport.WriteMessage(new ReadOnlySpan<byte>(data, dataOffset, chunksize), buffer);

                var prefix = BitConverter.GetBytes((UInt16)cipherBytes);
                //We Want Big Endian
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(prefix);

                await stream.WriteAsync(prefix, 0, 2);

                await stream.WriteAsync(buffer, 0, cipherBytes, cancel);

                dataOffset += chunksize;
            }

            await stream.FlushAsync(cancel).ConfigureAwait(false);

            log.Debug($"Sent via Noise {data.Length} bytes");
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            outStream.Write(buffer, offset, count);
        }

        /// <inheritdoc />
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return outStream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        /// <inheritdoc />
        public override void WriteByte(byte value)
        {
            outStream.WriteByte(value);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                transport.Dispose();
                stream.Dispose();
                outStream.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
