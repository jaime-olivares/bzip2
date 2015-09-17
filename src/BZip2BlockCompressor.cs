// Bzip2 library for .net
// By Jaime Olivares
// Location: http://github.com/jaime-olivares/bzip2
// Ported from the Java implementation by Matthew Francis: https://github.com/MateuszBartosiewicz/bzip2

namespace Bzip2
{
    /// <summary>Compresses and writes a single BZip2 block</summary>
    /// <remarks>
    /// Block encoding consists of the following stages:
	/// 1. Run-Length Encoding[1] - write()
    /// 2. Burrows Wheeler Transform - close() (through BZip2DivSufSort)
    /// 3. Write block header - close()
    /// 4. Move To Front Transform - close() (through BZip2HuffmanStageEncoder)
    /// 5. Run-Length Encoding[2] - close()  (through BZip2HuffmanStageEncoder)
    /// 6. Create and write Huffman tables - close() (through BZip2HuffmanStageEncoder)
    /// 7. Huffman encode and write data - close() (through BZip2HuffmanStageEncoder)
    /// </remarks>
    internal class BZip2BlockCompressor
    {
        #region Private fields
        // The stream to which compressed BZip2 data is written
		private readonly BZip2BitOutputStream bitOutputStream;

		// CRC builder for the block
		private readonly CRC32 crc = new CRC32();

		// The RLE'd block data
		private readonly byte[] block;

		// Current length of the data within the block array
		private int blockLength;

		// A limit beyond which new data will not be accepted into the block
		private readonly int blockLengthLimit;

        // The values that are present within the RLE'd block data. For each index, true if that
        // value is present within the data, otherwise false
		private readonly bool[] blockValuesPresent = new bool[256];

		// The Burrows Wheeler Transformed block data
		private readonly int[] bwtBlock;

		// The current RLE value being accumulated (undefined when rleLength is 0)
		private int rleCurrentValue = -1;

		// The repeat count of the current RLE value
		private int rleLength;
        #endregion

        #region Public fields
        // First three bytes of the block header marker
        public const uint BLOCK_HEADER_MARKER_1 = 0x314159;

        // Last three bytes of the block header marker
        public const uint BLOCK_HEADER_MARKER_2 = 0x265359;
        #endregion

        #region Public properties
        /**
         * Determines if any bytes have been written to the block
         * @return true if one or more bytes has been written to the block, otherwise false
         */
        public bool IsEmpty
        {
            get { return ((this.blockLength == 0) && (this.rleLength == 0)); }
        }

        /**
         * Gets the CRC of the completed block. Only valid after calling Close()
         * @return The block's CRC
         */
        public uint CRC
        {
            get { return this.crc.CRC; }
        }
        #endregion

        #region Public methods
        /**
         * Public constructor
         * @param bitOutputStream The BZip2BitOutputStream to which compressed BZip2 data is written
         * @param blockSize The declared block size in bytes. Up to this many bytes will be accepted
         *                  into the block after Run-Length Encoding is applied
         */
        public BZip2BlockCompressor(BZip2BitOutputStream bitOutputStream, int blockSize)
        {
            this.bitOutputStream = bitOutputStream;

            // One extra byte is added to allow for the block wrap applied in close()
            this.block = new byte[blockSize + 1];
            this.bwtBlock = new int[blockSize + 1];
            this.blockLengthLimit = blockSize - 6; // 5 bytes for one RLE run plus one byte - see Write(int)
        }

        /**
         * Writes a byte to the block, accumulating to an RLE run where possible
         * @param value The byte to write
         * @return true if the byte was written, or false if the block is already full
         */
        public bool Write(int value)
        {
            if (this.blockLength > this.blockLengthLimit)
                return false;

            if (rleLength == 0)
            {
                this.rleCurrentValue = value;
                this.rleLength = 1;
            }
            else if (rleCurrentValue != value)
            {
                // This path commits us to write 6 bytes - one RLE run (5 bytes) plus one extra
                this.WriteRun(rleCurrentValue & 0xff, rleLength);
                this.rleCurrentValue = value;
                this.rleLength = 1;
            }
            else
            {
                if (this.rleLength == 254)
                {
                    this.WriteRun(rleCurrentValue & 0xff, 255);
                    this.rleLength = 0;
                }
                else
                {
                    this.rleLength++;
                }
            }

            return true;
        }

        /**
         * Writes an array to the block
         * @param data The array to write
         * @param offset The offset within the input data to write from
         * @param length The number of bytes of input data to write
         * @return The actual number of input bytes written. May be less than the number requested, or
         *         zero if the block is already full
         */
        public int Write(byte[] data, int offset, int length)
        {
            var written = 0;

            while (length-- > 0)
            {
                if (!this.Write(data[offset++]))
                    break;
                written++;
            }

            return written;
        }

        /**
         * Compresses and writes out the block
         * Exception on any I/O error writing the data
         */
        public void Close()
        {
            // If an RLE run is in progress, write it out
            if (this.rleLength > 0)
                this.WriteRun(this.rleCurrentValue & 0xff, this.rleLength);

            // Apply a one byte block wrap required by the BWT implementation
            this.block[this.blockLength] = this.block[0];

            // Perform the Burrows Wheeler Transform
            var divSufSort = new BZip2DivSufSort(this.block, this.bwtBlock, this.blockLength);
            var bwtStartPointer = divSufSort.BWT();

            // Write out the block header
            this.bitOutputStream.WriteBits(24, BLOCK_HEADER_MARKER_1);
            this.bitOutputStream.WriteBits(24, BLOCK_HEADER_MARKER_2);
            this.bitOutputStream.WriteInteger(this.crc.CRC);
            this.bitOutputStream.WriteBoolean(false); // Randomised block flag. We never create randomised blocks
            this.bitOutputStream.WriteBits(24, (uint)bwtStartPointer);

            // Write out the symbol map
            this.WriteSymbolMap();

            // Perform the Move To Front Transform and Run-Length Encoding[2] stages 
            var mtfEncoder = new BZip2MTFAndRLE2StageEncoder(this.bwtBlock, this.blockLength, this.blockValuesPresent);
            mtfEncoder.Encode();

            // Perform the Huffman Encoding stage and write out the encoded data
            var huffmanEncoder = new BZip2HuffmanStageEncoder(this.bitOutputStream, mtfEncoder.MtfBlock, mtfEncoder.MtfLength, mtfEncoder.MtfAlphabetSize, mtfEncoder.MtfSymbolFrequencies);
            huffmanEncoder.Encode();
        }
        #endregion

        #region Private methods
        /**
         * Write the Huffman symbol to output byte map
         * @Exception on any I/O error writing the data
         */
		private void WriteSymbolMap()  
        {
			var condensedInUse = new bool[16];

			for (var i = 0; i < 16; i++) {
				for (int j = 0, k = i << 4; j < 16; j++, k++) {
					if (blockValuesPresent[k]) {
						condensedInUse[i] = true;
					}
				}
			}

			for (var i = 0; i < 16; i++) {
				bitOutputStream.WriteBoolean(condensedInUse[i]);
			}

			for (var i = 0; i < 16; i++) {
				if (condensedInUse[i]) {
					for (int j = 0, k = i * 16; j < 16; j++, k++) {
						bitOutputStream.WriteBoolean(blockValuesPresent[k]);
					}
				}
			}
		}
			
		/**
         * Writes an RLE run to the block array, updating the block CRC and present values array as required
         * @param value The value to write
         * @param runLength The run length of the value to write
         */
		private void WriteRun ( int value, int runLength) 
        {
			this.blockValuesPresent[value] = true;
			this.crc.UpdateCrc (value, runLength);

			var byteValue = (byte)value;
			switch (runLength)
            {
			    case 1:
				    block[blockLength] = byteValue;
				    this.blockLength = blockLength + 1;
				    break;

			    case 2:
				    block[blockLength] = byteValue;
				    block[blockLength + 1] = byteValue;
				    this.blockLength = blockLength + 2;
				    break;

			    case 3:
				    block[blockLength] = byteValue;
				    block[blockLength + 1] = byteValue;
				    block[blockLength + 2] = byteValue;
				    this.blockLength = blockLength + 3;
				    break;

			    default:
				    runLength -= 4;
				    this.blockValuesPresent[runLength] = true;
				    block[blockLength] = byteValue;
				    block[blockLength + 1] = byteValue;
				    block[blockLength + 2] = byteValue;
				    block[blockLength + 3] = byteValue;
				    block[blockLength + 4] = (byte)runLength;
				    this.blockLength = blockLength + 5;
				    break;
			}
		}
        #endregion
	}
}
