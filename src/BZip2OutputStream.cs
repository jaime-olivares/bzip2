// Bzip2 library for .net
// By Jaime Olivares
// Location: http://github.com/jaime-olivares/bzip2
// Ported from the Java implementation by Matthew Francis: https://github.com/MateuszBartosiewicz/bzip2

using System;
using System.IO;

namespace Bzip2
{
    /// <summary>An OutputStream wrapper that compresses BZip2 data</summary>
    /// <remarks>Instances of this class are not threadsafe</remarks>
    public class BZip2OutputStream : Stream 
	{
        #region Private fields
        // The stream to which compressed BZip2 data is written
        private Stream outputStream;

        // An OutputStream wrapper that provides bit-level writes
        private readonly BZip2BitOutputStream bitOutputStream;

        // (@code true} if the compressed stream has been finished, otherwise false
        private bool streamFinished;

        // The declared maximum block size of the stream (before final run-length decoding)
        private readonly int streamBlockSize;

        // The merged CRC of all blocks compressed so far
        private uint streamCRC;

        // The compressor for the current block
        private BZip2BlockCompressor blockCompressor;

        // True if the underlying stream will be closed with the current Stream
        private bool isOwner;
        #endregion

        #region Public fields
        /// <summary>The first 2 bytes of a Bzip2 marker</summary> 
        public const uint STREAM_START_MARKER_1 = 0x425a;

        /// <summary>The 'h' that distinguishes BZip from BZip2</summary> 
        public const uint STREAM_START_MARKER_2 = 0x68;

        /// <summary>First three bytes of the end of stream marker</summary> 
        public const uint STREAM_END_MARKER_1 = 0x177245;

        /// <summary>Last three bytes of the end of stream marker</summary> 
        public const uint STREAM_END_MARKER_2 = 0x385090;
        #endregion

        #region Public methods
        /// <summary>Public constructor</summary>
        /// <param name="outputStream">The output stream to write to</param>
        /// <param name="blockSizeMultiplier">The BZip2 block size as a multiple of 100,000 bytes (minimum 1, maximum 9)</param>
        /// <param name="isOwner">True if the underlying stream will be closed with the current Stream</param>
        /// <exception>On any I/O error writing to the output stream</exception>
        /// <remarks>Larger block sizes require more memory for both compression and decompression,
        /// but give better compression ratios. 9 will usually be the best value to use</remarks>
        public BZip2OutputStream(Stream outputStream, bool isOwner = true, int blockSizeMultiplier = 9)
        {
            if (outputStream == null)
                throw new ArgumentException("Null output stream");

            if ((blockSizeMultiplier < 1) || (blockSizeMultiplier > 9))
                throw new ArgumentException("Invalid BZip2 block size" + blockSizeMultiplier);

            this.streamBlockSize = blockSizeMultiplier * 100000;
            this.outputStream = outputStream;
            this.bitOutputStream = new BZip2BitOutputStream(this.outputStream);
            this.isOwner = isOwner;

            this.bitOutputStream.WriteBits(16, STREAM_START_MARKER_1);
            this.bitOutputStream.WriteBits(8, STREAM_START_MARKER_2);
            this.bitOutputStream.WriteBits(8, (uint)('0' + blockSizeMultiplier));

            this.InitialiseNextBlock();
        }
        #endregion

        #region Implementation of abstract members of Stream
        #pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        public override void Flush ()
		{
			throw new NotImplementedException ();
		}

		public override int Read (byte[] buffer, int offset, int count)
		{
			throw new NotImplementedException ();
		}

		public override long Seek (long offset, SeekOrigin origin)
		{
			throw new NotImplementedException ();
		}

		public override void SetLength (long value)
		{
			throw new NotImplementedException ();
		}

		public override bool CanRead {
			get {
				return false;
			}
		}

		public override bool CanSeek {
			get {
				return false;
			}
		}

		public override bool CanWrite {
			get {
				return true;
			}
		}

		public override long Length {
			get {
				throw new NotImplementedException ();
			}
		}

		public override long Position {
			get {
				throw new NotImplementedException ();
			}
			set {
				throw new NotImplementedException ();
			}
		}

        public override void WriteByte(byte value)
        {
            if (this.outputStream == null)
                throw new Exception("Stream closed");

            if (this.streamFinished)
                throw new Exception("Write beyond end of stream");

            if (!this.blockCompressor.Write(value & 0xff))
            {
                this.CloseBlock();
                this.InitialiseNextBlock();
                this.blockCompressor.Write(value & 0xff);
            }
        }

        public override void Write(byte[] data, int offset, int length)
        {
            if (this.outputStream == null)
                throw new Exception("Stream closed");

            if (this.streamFinished)
                throw new Exception("Write beyond end of stream");

            while (length > 0)
            {
                int bytesWritten;
                if ((bytesWritten = this.blockCompressor.Write(data, offset, length)) < length)
                {
                    this.CloseBlock();
                    this.InitialiseNextBlock();
                }
                offset += bytesWritten;
                length -= bytesWritten;
            }
        }

        public override void Close()
        {
            if (this.outputStream != null)
            {
                this.Finish();
                if (isOwner)
                    this.outputStream.Close();
                this.outputStream = null;
            }
        }
        #pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
        #endregion

        #region Private methods
        /// <summary>Initialises a new block for compression</summary> 
		private void InitialiseNextBlock() 
		{
			this.blockCompressor = new BZip2BlockCompressor (this.bitOutputStream, this.streamBlockSize);
		}

        /// <summary>Compress and write out the block currently in progress</summary>
        /// <remarks>If no bytes have been written to the block, it is discarded</remarks>
        /// <exception>On any I/O error writing to the output stream</exception>
        private void CloseBlock()
        {
			if (this.blockCompressor.IsEmpty) 
				return;

			this.blockCompressor.Close();
			this.streamCRC = ((this.streamCRC << 1) | (this.streamCRC >> 31)) ^ this.blockCompressor.CRC;
        }

        /// <summary>Compresses and writes out any as yet unwritten data, then writes the end of the BZip2 stream</summary>
        /// <remarks>The underlying OutputStream is not closed</remarks>
        /// <exception>On any I/O error writing to the output stream</exception>
        private void Finish()
        {
			if (!this.streamFinished)
            {
				this.streamFinished = true;
				try {
					this.CloseBlock();
					this.bitOutputStream.WriteBits(24, STREAM_END_MARKER_1);
					this.bitOutputStream.WriteBits(24, STREAM_END_MARKER_2);
					this.bitOutputStream.WriteInteger(this.streamCRC);
					this.bitOutputStream.Flush();
					this.outputStream.Flush();
				} finally {
					this.blockCompressor = null;
				}
			}
        }
        #endregion
    }
}