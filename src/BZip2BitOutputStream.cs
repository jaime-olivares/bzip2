// Bzip2 library for .net
// By Jaime Olivares
// Location: http://github.com/jaime-olivares/bzip2
// Ported from the Java implementation by Matthew Francis: https://github.com/MateuszBartosiewicz/bzip2

using System.IO;

namespace Bzip2
{
    /// <summary>Implements a bit-wise output stream</summary>
    /// <remarks>
    /// Allows the writing of single bit booleans, unary numbers, bit
    /// strings of arbitrary length(up to 24 bits), and bit aligned 32-bit integers.A single byte at a
    /// time is written to the wrapped stream when sufficient bits have been accumulated
    /// </remarks>
    internal class BZip2BitOutputStream
    {
        #region Private fields
        // The stream to which bits are written
		private readonly Stream outputStream;

		// A buffer of bits waiting to be written to the output stream	 
		private uint bitBuffer;

		// The number of bits currently buffered in bitBuffer
		private int bitCount;
        #endregion

        #region Public methods
        /**
         * Public constructor
	     * @param outputStream The OutputStream to wrap
	     */
        public BZip2BitOutputStream(Stream outputStream)
        {
            this.outputStream = outputStream;
        }

        /**
		 * Writes a single bit to the wrapped output stream
		 * @param value The bit to write
		 * @Exception if an error occurs writing to the stream
		 */
		public void WriteBoolean (bool value)
        {
            this.bitCount++;
			this.bitBuffer |= ((value ? 1u : 0u) << (32 - bitCount));

			if (bitCount == 8)
            {
				this.outputStream.WriteByte((byte)(bitBuffer >> 24));
				bitBuffer = 0;
				bitCount = 0;
			}
		}
			
	    /**
	     * Writes a zero-terminated unary number to the wrapped output stream
	     * @param value The number to write (must be non-negative)
	     * @Exception if an error occurs writing to the stream
	     */
		public void WriteUnary (int value)  
        {
			while (value-- > 0)
            {
				this.WriteBoolean (true); 
			}
			this.WriteBoolean (false);
		}

	    /**
	     * Writes up to 24 bits to the wrapped output stream
	     * @param count The number of bits to write (maximum 24)
	     * @param value The bits to write
	     * @Exception if an error occurs writing to the stream
	     */
		public void WriteBits (int count,  uint value) 
        {
			this.bitBuffer |= ((value << (32 - count)) >> bitCount);
			this.bitCount += count;

			while (bitCount >= 8)
            {
				this.outputStream.WriteByte((byte)(bitBuffer >> 24));
				bitBuffer <<= 8;
				bitCount -= 8;
			}
		}

		/**
	     * Writes an integer as 32 bits of output
	     * @param value The integer to write
	     * @Exception if an error occurs writing to the stream
	     */
		public void WriteInteger (uint value)  
        {
			this.WriteBits (16, (value >> 16) & 0xffff);
			this.WriteBits (16, value & 0xffff);
		}

		/**
	     * Writes any remaining bits to the output stream, zero padding to a whole byte as required
	     * @Exception if an error occurs writing to the stream
	     */
		public void Flush()  
        {
			if (this.bitCount > 0) 
				this.WriteBits (8 - this.bitCount, 0);
		}
        #endregion
    }
}
