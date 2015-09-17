using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bzip2
{
    /// <summary>
    /// Unit test for the BZip2 compression library
    /// </summary>
    [TestClass]
    public class UnitTests
    {
        private const int BufferSizeLarge = 10000000; // Almost 10 Mb
        private const int BufferSizeSmall = 100000;   // Around 100 Kb
        private static byte[] Buffer = new byte[BufferSizeLarge];

        /// <summary>
        /// Fills the test buffer with random values
        /// </summary>
        [TestInitialize]
        public void InitializeBuffer()
        {
            var random = new Random();
            random.NextBytes(Buffer);
        }

        /// <summary>
        /// Performs a CRC check and compare against well-known results
        /// The buffer has different values
        /// </summary>
        [TestMethod]
        public void CrcAlgorithmDifferentValues()
        {
            byte[] buffer = {
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A,
                0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8, 0xF9, 0xFA
            };

            var crc = new CRC32();
            for (int i = 0; i < buffer.Length; i++)
                crc.UpdateCrc(buffer[i]);

            Assert.AreEqual(crc.CRC, 0x8AEE127A);
        }

        /// <summary>
        /// Performs a CRC check and compare against well-known results
        /// The buffer has different values
        /// </summary>
        [TestMethod]
        public void CrcAlgorithmSameValues()
        {
            var crc = new CRC32();
            crc.UpdateCrc(0x55,10);
            Assert.AreEqual(crc.CRC, 0xA1E07747); 
        }

        /// <summary>
        /// Compresses the full buffer and checks for a reasonable compressed size
        /// </summary>
        [TestMethod]
        public void CompressSmokeLarge()
        {
            var input = new MemoryStream(Buffer);
            var output = new MemoryStream();

            var compressor = new BZip2OutputStream(output, false);
            input.CopyTo(compressor);
            compressor.Close();

            // Estimated size between inputSize*0.5 and inputSize*1.1
            Assert.IsTrue(output.Length > BufferSizeLarge * 0.5);
            Assert.IsTrue(output.Length < BufferSizeLarge * 1.1);
        }

        /// <summary>
        /// Compresses a portion of the buffer and checks for a reasonable compressed size
        /// </summary>
        [TestMethod]
        public void CompressSmokeSmall()
        {
            var input = new MemoryStream(Buffer, 0, BufferSizeSmall);
            var output = new MemoryStream();

            var compressor = new BZip2OutputStream(output, false);
            input.CopyTo(compressor);
            compressor.Close();

            // Estimated size between inputSize*0.5 and inputSize*1.1
            Assert.IsTrue(output.Length > BufferSizeSmall * 0.5);
            Assert.IsTrue(output.Length < BufferSizeSmall * 1.1);
        }

        /// <summary>
        /// Compresses and decompresses a long random buffer
        /// </summary>
        [TestMethod]
        public void CompressAndDecompress()
        {
            var input = new MemoryStream(Buffer);
            var output = new MemoryStream();

            var compressor = new BZip2OutputStream(output, false);
            input.CopyTo(compressor);
            compressor.Close();

            Assert.IsTrue(output.Length > 4);

            output.Position = 0;
            var output2 = new MemoryStream();
            var decompressor = new BZip2InputStream(output, false);
            decompressor.CopyTo(output2);

            Assert.AreEqual(Buffer.Length, output2.Length);
        }
    }
}
