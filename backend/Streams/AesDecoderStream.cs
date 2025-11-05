using System.Security.Cryptography;
using NzbWebDAV.Models;

namespace NzbWebDAV.Streams
{
    internal sealed class AesDecoderStream : Stream
    {
        private readonly Stream _mStream;
        private ICryptoTransform _mDecoder;
        private readonly byte[] _mBuffer;
        private long _mWritten;
        private readonly long _mLimit;
        private int _mOffset;
        private int _mEnding;
        private int _mUnderflow;
        private bool _isDisposed;
        private long? pendingSeekPosition = null;

        // store for reinitializing on Seek
        private readonly byte[] _mKey;
        private readonly byte[] _mBaseIv;

        public AesDecoderStream(Stream input, AesParams aesParams)
        {
            _mStream = input;
            _mLimit = aesParams.DecodedSize;
            _mKey = aesParams.Key;
            _mBaseIv = aesParams.Iv;

            if (((uint)input.Length & 15) != 0)
            {
                throw new NotSupportedException("AES decoder does not support padding.");
            }

            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                _mDecoder = aes.CreateDecryptor(_mKey, _mBaseIv);
            }

            _mBuffer = new byte[4 << 10];
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (_isDisposed)
                    return;

                _isDisposed = true;
                if (disposing)
                {
                    _mStream.Dispose();
                    _mDecoder.Dispose();
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        public override void Flush() => throw new NotImplementedException();

        public override long Position
        {
            get => pendingSeekPosition ?? _mWritten;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override bool CanWrite => false;
        public override long Length => _mLimit;
        public override bool CanRead => true;
        public override bool CanSeek => true;

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            // perform any pending seeks
            if (pendingSeekPosition != null)
            {
                if (!await SeekAsync(pendingSeekPosition.Value, ct)) return 0;
                pendingSeekPosition = null;
            }

            if (count == 0 || _mWritten == _mLimit)
                return 0;

            if (_mUnderflow > 0)
                return HandleUnderflow(buffer, offset, count);

            // Need at least 16 bytes
            if (_mEnding - _mOffset < 16)
            {
                Buffer.BlockCopy(_mBuffer, _mOffset, _mBuffer, 0, _mEnding - _mOffset);
                _mEnding -= _mOffset;
                _mOffset = 0;

                do
                {
                    var read = await _mStream
                        .ReadAsync(_mBuffer.AsMemory(_mEnding, _mBuffer.Length - _mEnding), ct)
                        .ConfigureAwait(false);
                    if (read == 0) return 0;
                    _mEnding += read;
                } while (_mEnding - _mOffset < 16);
            }

            if (count > _mLimit - _mWritten)
                count = (int)(_mLimit - _mWritten);

            if (count < 16)
                return HandleUnderflow(buffer, offset, count);

            if (count > _mEnding - _mOffset)
                count = _mEnding - _mOffset;

            var processed = _mDecoder.TransformBlock(_mBuffer, _mOffset, count & ~15, buffer, offset);
            _mOffset += processed;
            _mWritten += processed;
            return processed;
        }

        public override void SetLength(long value) => throw new NotImplementedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    target = offset;
                    break;
                case SeekOrigin.Current:
                    target = _mWritten + offset;
                    break;
                case SeekOrigin.End:
                    target = _mLimit + offset;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(origin), origin, null);
            }

            if (target < 0 || target > _mLimit)
                throw new ArgumentOutOfRangeException(nameof(offset), "Seek position outside stream limits");

            pendingSeekPosition = target;
            return target;
        }

        private async Task<bool> SeekAsync(long offset, CancellationToken ct)
        {
            // Align to AES block size (16)
            var blockSize = 16;
            var blockIndex = offset / blockSize;
            var blockOffset = offset % blockSize;

            // We need previous block to get proper IV
            var cipherPos = blockIndex > 0 ? (blockIndex - 1) * blockSize : 0;

            // Seek underlying stream
            _mStream.Seek(cipherPos, SeekOrigin.Begin);

            // Read previous block as IV (or original IV for block 0)
            var iv = new byte[16];
            if (blockIndex > 0)
            {
                try
                {
                    await _mStream.ReadExactlyAsync(iv.AsMemory(0, 16), ct);
                }
                catch (Exception e)
                {
                    throw new EndOfStreamException("Unable to read previous block for IV", e);
                }
            }
            else
            {
                Buffer.BlockCopy(_mBaseIv, 0, iv, 0, 16);
            }

            // Create a new decryptor starting at that IV
            _mDecoder.Dispose();
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                _mDecoder = aes.CreateDecryptor(_mKey, iv);
            }

            // Reset internal buffers
            _mOffset = 0;
            _mEnding = 0;
            _mUnderflow = 0;
            _mWritten = offset - blockOffset; // position of start of current block

            // Preload and decrypt up to the block containing target
            if (blockOffset > 0)
            {
                var tempCipher = new byte[blockSize];
                await _mStream.ReadExactlyAsync(tempCipher.AsMemory(0, blockSize), ct);

                var tempPlain = new byte[blockSize];
                var decrypted = _mDecoder.TransformBlock(tempCipher, 0, blockSize, tempPlain, 0);

                // cache the remainder into mBuffer for next reads
                Buffer.BlockCopy(tempPlain, (int)blockOffset, _mBuffer, 0, decrypted - (int)blockOffset);
                _mOffset = 0;
                _mEnding = decrypted - (int)blockOffset;
                _mWritten += blockOffset;
            }

            return true;
        }

        private int HandleUnderflow(byte[] buffer, int offset, int count)
        {
            if (_mUnderflow == 0)
            {
                var blockSize = (_mEnding - _mOffset) & ~15;
                _mUnderflow = _mDecoder.TransformBlock(_mBuffer, _mOffset, blockSize, _mBuffer, _mOffset);
            }

            if (count > _mUnderflow)
                count = _mUnderflow;

            Buffer.BlockCopy(_mBuffer, _mOffset, buffer, offset, count);
            _mWritten += count;
            _mOffset += count;
            _mUnderflow -= count;
            return count;
        }
    }
}