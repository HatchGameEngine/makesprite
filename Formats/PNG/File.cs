using System.IO.Compression;
using System.Drawing;
using System.IO;

// Code portions taken from the public domain library BigGustave
namespace PNG {
    public class File {
        private static byte[] Header = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private enum ColorType {
            Indexed = 3,
            RGBA = 6
        };

        private int Width;
        private int Height;
        private int ColorDepth;
        private int BytesPerPixel;
        private byte[] Data;
        private Color[]? Palette;

        public File(int width, int height, int colorDepth, byte[] data, Color[]? palette) {
            Width = width;
            Height = height;
            ColorDepth = colorDepth;
            BytesPerPixel = colorDepth / 8;
            Data = data;
            Palette = palette;
        }

        public void Write(Stream outputStream) {
            int dataLength = 0;
            int rowLength = Width * BytesPerPixel;
            int bitDepth = 8;
            ColorType colorType = ColorDepth == 8 ? ColorType.Indexed : ColorType.RGBA;

            // Each row of a PNG IDAT begins with a "filter type" byte.
            // That's why the buffer is Height bytes bigger.
            // We just write 0, which is no filtering.
            byte[]? data = new byte[(Width * Height * BytesPerPixel) + Height];
            byte[]? paletteColors = null;
            byte[]? trnsData = null;

            int dataIndex = 0;
            int inputIndex = 0;

            if (colorType == ColorType.Indexed && Palette != null) {
                bitDepth = Palette.Length > 16 ? 8 : 4;
                paletteColors = new byte[3 * Palette.Length];
                trnsData = new byte[Palette.Length];

                for (int i = 0; i < Palette.Length; i++) {
                    int palIndex = i * 3;
                    System.Drawing.Color color = Palette[i];
                    paletteColors[palIndex + 0] = color.R;
                    paletteColors[palIndex + 1] = color.G;
                    paletteColors[palIndex + 2] = color.B;
                    trnsData[i] = color.A;
                }

                int samplesPerByte = bitDepth == 8 ? 1 : 2;
                bool applyShift = samplesPerByte == 2;

                for (int y = 0; y < Height; y++) {
                    data[dataIndex++] = 0; // None filter

                    for (int x = 0; x < rowLength; x++) {
                        byte colorIndex = Data[inputIndex++];

                        if (applyShift) {
                            // apply mask and shift
                            int withinByteIndex = x % 2;
                            if (withinByteIndex == 1) {
                                data[dataIndex] = (byte)(data[dataIndex] + colorIndex);
                                dataIndex++;
                            }
                            else {
                                data[dataIndex] = (byte)(colorIndex << 4);
                            }
                        }
                        else {
                            data[dataIndex++] = colorIndex;
                        }
                    }
                }

                dataLength = dataIndex;
            }
            else {
                for (int y = 0; y < Height; y++) {
                    data[dataIndex++] = 0; // None filter

                    for (int x = 0; x < rowLength; x++) {
                        data[dataIndex++] = Data[inputIndex++];
                    }
                }

                dataLength = dataIndex;
            }

            PNGWriter stream = new PNGWriter(outputStream);
            stream.Write(Header);

            // Write IHDR
            stream.WriteChunkLength(13);
            stream.WriteChunkHeader(System.Text.Encoding.ASCII.GetBytes("IHDR"));
            WriteUInt32BE(stream, Width);
            WriteUInt32BE(stream, Height);
            stream.WriteByte((byte)bitDepth);
            stream.WriteByte((byte)colorType);
            stream.WriteByte((byte)0); // Compression method (always 0)
            stream.WriteByte((byte)0); // Filter method (always 0)
            stream.WriteByte((byte)0); // Interlace method (0 = none, 1 = Adam7. We write zero)
            stream.WriteCRC();

            // Write PLTE and tRNS if indexed
            if (paletteColors != null) {
                stream.WriteChunkLength(paletteColors.Length);
                stream.WriteChunkHeader(System.Text.Encoding.ASCII.GetBytes("PLTE"));
                stream.Write(paletteColors);
                stream.WriteCRC();
            }

            if (trnsData != null) {
                stream.WriteChunkLength(trnsData.Length);
                stream.WriteChunkHeader(System.Text.Encoding.ASCII.GetBytes("tRNS"));
                stream.Write(trnsData);
                stream.WriteCRC();
            }

            // Write IDAT
            byte[] imageData = Compress(data, dataLength);
            stream.WriteChunkLength(imageData.Length);
            stream.WriteChunkHeader(System.Text.Encoding.ASCII.GetBytes("IDAT"));
            stream.Write(imageData);
            stream.WriteCRC();

            // Write IEND
            stream.WriteChunkLength(0);
            stream.WriteChunkHeader(System.Text.Encoding.ASCII.GetBytes("IEND"));
            stream.WriteCRC();
        }

        public void Write(BinaryWriter writer) {
            using (MemoryStream memoryStream = new MemoryStream()) {
                Write(memoryStream);
                writer.Write(memoryStream.ToArray());
            }
        }

        private static byte[] Compress(byte[] data, int dataLength) {
            const byte deflate32KbWindow = 120;
            const byte checksumBits = 1;

            const int headerLength = 2;
            const int checksumLength = 4;

            using (MemoryStream compressStream = new MemoryStream()) {
                using (DeflateStream compressor = new DeflateStream(compressStream, CompressionLevel.Optimal, true)) {
                    compressor.Write(data, 0, dataLength);
                    compressor.Close();
                }

                compressStream.Seek(0, SeekOrigin.Begin);

                byte[] result = new byte[headerLength + compressStream.Length + checksumLength];

                // Write the Zlib header.
                result[0] = deflate32KbWindow;
                result[1] = checksumBits;

                // Write the compressed data.
                int streamValue;
                int i = 0;
                while ((streamValue = compressStream.ReadByte()) != -1) {
                    result[headerLength + i] = (byte)streamValue;
                    i++;
                }

                // Write the checksum of the raw data.
                int checksum = Adler32Checksum.Calculate(data, dataLength);
                long offset = headerLength + compressStream.Length;

                result[offset++] = (byte)(checksum >> 24);
                result[offset++] = (byte)(checksum >> 16);
                result[offset++] = (byte)(checksum >> 8);
                result[offset] = (byte)(checksum >> 0);

                return result;
            }
        }

        internal class PNGWriter : Stream {
            private readonly Stream inner;
            private readonly List<byte> written = new List<byte>();

            public override bool CanRead => inner.CanRead;

            public override bool CanSeek => inner.CanSeek;

            public override bool CanWrite => inner.CanWrite;

            public override long Length => inner.Length;

            public override long Position {
                get => inner.Position;
                set => inner.Position = value;
            }

            public PNGWriter(Stream inner) {
                this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            public override void Flush() => inner.Flush();

            public void WriteChunkHeader(byte[] header) {
                written.Clear();
                Write(header, 0, header.Length);
            }

            public void WriteChunkLength(int length) {
                WriteUInt32BE(inner, length);
            }

            public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

            public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

            public override void SetLength(long value) => inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count) {
                written.AddRange(buffer.Skip(offset).Take(count));
                inner.Write(buffer, offset, count);
            }

            public void WriteCRC() {
                int result = (int)CRC32.Calculate(written);
                WriteUInt32BE(inner, result);
            }
        }

        private static void WriteUInt32BE(Stream stream, int value) {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }
    }
}
