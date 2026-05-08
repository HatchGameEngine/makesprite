using System.Drawing;
using System.IO.Compression;

// Code portions taken from the public domain library BigGustave
namespace PNG {
    public enum ColorType {
        Grayscale = 0,
        Truecolor = 2,
        Indexed = 3,
        GrayscaleAlpha = 4,
        TruecolorAlpha = 6
    };

    public enum FilterType {
        None = 0,
        Sub = 1,
        Up = 2,
        Average = 3,
        Paeth = 4
    };

    public enum InterlaceMethod {
        None = 0,
        Adam7 = 1
    };

    public class File : makesprite.ImageFile {
        private static byte[] Header = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public int BitDepth;
        public ColorType ColorType;
        public int BytesPerPixel;
        private int SamplesPerPixel;
        public byte CompressionMethod;
        public FilterType FilterType;
        public InterlaceMethod InterlaceMethod;
        public byte[]? PaletteData = null;
        public byte[]? TransparencyData = null;

        public File(int width, int height, int bitDepth, byte[] data, Color[]? palette) : base(width, height, bitDepth, data, palette) {
            BitDepth = bitDepth;
            BytesPerPixel = bitDepth / 8;

            if (palette != null) {
                SetPalette(palette);
            }
        }

        private void SetPalette(Color[] palette) {
            bool hasTransparency = false;

            Palette = palette;
            PaletteData = new byte[palette.Length * 3];
            TransparencyData = null;
            TransparentPaletteIndex = -1;

            for (int i = 0; i < palette.Length; i++) {
                PaletteData[(i * 3) + 0] = palette[i].R;
                PaletteData[(i * 3) + 1] = palette[i].G;
                PaletteData[(i * 3) + 2] = palette[i].B;

                if (palette[i].A != 0xFF) {
                    hasTransparency = true;
                }
            }

            if (hasTransparency) {
                TransparencyData = new byte[palette.Length];

                for (int i = 0; i < palette.Length; i++) {
                    byte alpha = palette[i].A;
                    if (alpha == 0 && TransparentPaletteIndex == -1) {
                        TransparentPaletteIndex = i;
                    }

                    TransparencyData[i] = alpha;
                }
            }
        }

        static public bool ReadAndValidatePNGHeader(BinaryReader reader) {
            byte[] header = reader.ReadBytes(Header.Length);
            return header.SequenceEqual(Header);
        }

        static public bool IsValid(BinaryReader reader) {
            if (ReadAndValidatePNGHeader(reader)) {
                return true;
            }

            return false;
        }

        static public bool IsValid(Stream stream) {
            return IsValid(new BinaryReader(stream));
        }

        public byte GetSamplesPerPixel() {
            switch (ColorType) {
            case ColorType.Grayscale:
                return 1;
            case ColorType.Indexed:
                return 1;
            case ColorType.Truecolor:
                return 3;
            case ColorType.GrayscaleAlpha:
                return 2;
            case ColorType.TruecolorAlpha:
                return 4;
            default:
                return 0;
            }
        }

        public int GetBytesPerScanline() {
            switch (BitDepth) {
            case 1:
                return (Width + 7) / 8;
            case 2:
                return (Width + 3) / 4;
            case 4:
                return (Width + 1) / 2;
            case 8:
            case 16:
                return Width * SamplesPerPixel * (BitDepth / 8);
            default:
                return 0;
            }
        }

        public File(BinaryReader reader) {
            if (!ReadAndValidatePNGHeader(reader)) {
                throw new InvalidOperationException("Invalid PNG file");
            }

            ReadIHDR(reader);

            SamplesPerPixel = GetSamplesPerPixel();
            BytesPerPixel = SamplesPerPixel * ((BitDepth + 7) / 8);
            ColorDepth = BitDepth * SamplesPerPixel;

            MemoryStream pixelDataStream = new MemoryStream();
            byte[] crc = new byte[4];
            bool reachedEnd = false;

            while (!reachedEnd) {
                int headerLength = ReadInt32BE(reader);
                byte[] nameBytes = reader.ReadBytes(4);
                string headerName = System.Text.Encoding.ASCII.GetString(nameBytes);

                byte[] data = new byte[headerLength];
                int read = reader.Read(data, 0, data.Length);
                if (read != data.Length) {
                    throw new InvalidOperationException($"Expected to read {headerLength} bytes for {headerName}, got {read} bytes");
                }

                switch (headerName) {
                case "PLTE":
                    if (headerLength % 3 != 0) {
                        throw new InvalidOperationException($"{headerName} length must be multiple of 3, but was {headerLength} bytes long");
                    }

                    PaletteData = new byte[data.Length];

                    int dataIndex = 0;
                    for (int i = 0; i < data.Length; i += 3) {
                        PaletteData[dataIndex++] = data[i + 0];
                        PaletteData[dataIndex++] = data[i + 1];
                        PaletteData[dataIndex++] = data[i + 2];
                    }
                    break;
                case "IDAT":
                    pixelDataStream.Write(data, 0, data.Length);
                    break;
                case "IEND":
                    reachedEnd = true;
                    break;
                case "tRNS":
                    TransparencyData = new byte[data.Length];

                    for (int i = 0; i < data.Length; i++) {
                        byte alpha = data[i];
                        if (alpha == 0 && TransparentPaletteIndex == -1) {
                            TransparentPaletteIndex = i;
                        }
                        TransparencyData[i] = alpha;
                    }
                    break;
                default:
                    if (char.IsUpper(headerName[0])) {
                        throw new NotSupportedException($"Unknown critical header {headerName}");
                    }
                    break;
                }

                read = reader.Read(crc, 0, crc.Length);
                if (read != 4) {
                    throw new InvalidOperationException($"Could not read CRC. Read {read} bytes instead");
                }

                int result = (int)CRC32.Calculate(System.Text.Encoding.ASCII.GetBytes(headerName), data);
                int crcActual = (crc[0] << 24) + (crc[1] << 16) + (crc[2] << 8) + crc[3];
                if (result != crcActual) {
                    throw new InvalidOperationException($"Calculated CRC {result} did not match expected CRC {crcActual} for {headerName}");
                }
            }

            pixelDataStream.Flush();
            pixelDataStream.Seek(2, SeekOrigin.Begin);

            MemoryStream output = new MemoryStream();
            using (DeflateStream deflateStream = new DeflateStream(pixelDataStream, CompressionMode.Decompress)) {
                deflateStream.CopyTo(output);
                deflateStream.Close();
            }

            Data = PrepareDecodedData(Decoder.Decode(output.ToArray(), this));
        }

        public File(Stream stream) : this(new BinaryReader(stream)) {}

        private byte[] PrepareDecodedData(byte[] decoded) {
            int p = 0;
            int dataIndex = 0;

            byte[] data = new byte[Width * Height * BytesPerPixel];

            for (int y = 0; y < Height; y++) {
                dataIndex++;

                for (int x = 0; x < Width * BytesPerPixel; x++) {
                    data[p++] = decoded[dataIndex++];
                }
            }

            return data;
        }

        private void ReadIHDR(BinaryReader reader) {
            int headerLength = ReadInt32BE(reader);
            byte[] nameBytes = reader.ReadBytes(4);
            string headerName = System.Text.Encoding.ASCII.GetString(nameBytes);

            if (headerName != "IHDR") {
                throw new InvalidOperationException($"Expected IHDR chunk, got {headerName}");
            }

            if (headerLength != 13) {
                throw new InvalidOperationException($"Expected IHDR to be 13 bytes long, but was {headerLength} bytes long");
            }

            byte[] data = new byte[13];
            int read = reader.Read(data, 0, data.Length);
            if (read != data.Length) {
                throw new InvalidOperationException($"Expected to read 13 bytes for IHDR, got {read} bytes");
            }

            byte[] crc = new byte[4];
            read = reader.Read(crc, 0, crc.Length);
            if (read != 4) {
                throw new InvalidOperationException($"Could not read CRC. Read {read} bytes instead");
            }

            Width = ReadInt32BE(data, 0);
            Height = ReadInt32BE(data, 4);
            BitDepth = data[8];
            ColorType = (ColorType)data[9];
            CompressionMethod = data[10];
            FilterType = (FilterType)data[11];
            InterlaceMethod = (InterlaceMethod)data[12];
        }

        public uint[] GetPixelData(out int colorDepth) {
            uint[] pixelData = new uint[Width * Height];

            long dataIndex = 0;

            byte GetDownscaled16BitPixel() {
                byte first = Data[dataIndex++];
                byte second = Data[dataIndex++];

                int us = (first << 8) + second;
                return (byte)Math.Round((255 * us) / (double)ushort.MaxValue);
            }

            colorDepth = ColorDepth;

            // Currently we only support indexed, grayscale, or RGBA in the converter.
            // 16-bit is scaled down to 8-bit, and grayscale and RGB formats become RGBA.
            // Since the converter would change grayscale to RGBA anyway, we just do that here.
            if (BytesPerPixel > 1) {
                colorDepth = 32;
            }

            switch (BytesPerPixel) {
            case 1:
                if (ColorType == ColorType.Indexed) {
                    DecodePalettizedData(pixelData, Data, BitDepth);
                    break;
                }
                else if (ColorType != ColorType.Grayscale) {
                    // This shouldn't be possible
                    throw new InvalidOperationException("Invalid PNG file");
                }

                for (int p = 0; p < Width * Height; p++) {
                    byte value = Data[dataIndex++];
                    pixelData[p] = (uint)(value << 16 | value << 8 | value) | 0xFF000000;
                }

                colorDepth = 32;
                break;
            case 2:
                if (ColorType == ColorType.Grayscale) {
                    for (int p = 0; p < Width * Height; p++) {
                        byte value = GetDownscaled16BitPixel();
                        pixelData[p] = (uint)(value << 16 | value << 8 | value) | 0xFF000000;
                    }
                }
                else {
                    for (int p = 0; p < Width * Height; p++) {
                        byte value = Data[dataIndex++];
                        byte alpha = Data[dataIndex++];
                        pixelData[p] = (uint)(alpha << 24 | value << 16 | value << 8 | value);
                    }
                }
                break;
            case 3:
                for (int p = 0; p < Width * Height; p++) {
                    byte r = Data[dataIndex++];
                    byte g = Data[dataIndex++];
                    byte b = Data[dataIndex++];
                    pixelData[p] = (uint)(b << 16 | g << 8 | r) | 0xFF000000;
                }
                break;
            case 4:
                if (ColorType == ColorType.GrayscaleAlpha) {
                    for (int p = 0; p < Width * Height; p++) {
                        byte value = GetDownscaled16BitPixel();
                        byte alpha = GetDownscaled16BitPixel();
                        pixelData[p] = (uint)(alpha << 24 | value << 16 | value << 8 | value);
                    }
                }
                else {
                    for (int p = 0; p < Width * Height; p++) {
                        byte r = Data[dataIndex++];
                        byte g = Data[dataIndex++];
                        byte b = Data[dataIndex++];
                        byte a = Data[dataIndex++];
                        pixelData[p] = (uint)(a << 24 | b << 16 | g << 8 | r);
                    }
                }
                break;
            case 6:
                for (int p = 0; p < Width * Height; p++) {
                    byte r = GetDownscaled16BitPixel();
                    byte g = GetDownscaled16BitPixel();
                    byte b = GetDownscaled16BitPixel();
                    pixelData[p] = (uint)(b << 16 | g << 8 | r) | 0xFF000000;
                }
                break;
            case 8:
                for (int p = 0; p < Width * Height; p++) {
                    byte r = GetDownscaled16BitPixel();
                    byte g = GetDownscaled16BitPixel();
                    byte b = GetDownscaled16BitPixel();
                    byte a = GetDownscaled16BitPixel();
                    pixelData[p] = (uint)(a << 24 | b << 16 | g << 8 | r);
                }
                break;
            }

            return pixelData;
        }

        private void DecodePalettizedData(uint[] pixelData, byte[] data, int bitDepth) {
            int scanlineWidth = (((bitDepth * Width) + 15) / 8) - 1;

            byte[] bitBuffer = new byte[8];

            int rowsLeft = Height;
            int scanlineIndex = 0;
            int outIndex = 0;

            while (rowsLeft > 0) {
                int scanlineEndIndex = scanlineIndex + scanlineWidth;
                while (scanlineIndex < scanlineEndIndex) {
                    byte pixels = data[scanlineIndex++];

                    int i = 0;
                    for (int bitIndex = 0; bitIndex < 8; bitIndex += bitDepth) {
                        bitBuffer[i++] = (byte)(pixels & ((1 << bitDepth) - 1));
                        pixels >>= bitDepth;
                    }
                    --i;

                    for (int bitIndex = 0; bitIndex < 8; bitIndex += bitDepth) {
                        pixelData[outIndex++] = bitBuffer[i--];
                    }
                }

                rowsLeft--;
            }
        }

        public override uint[] GetFramePixels(int frameIndex, out int colorDepth) {
            return GetPixelData(out colorDepth);
        }

        public override Color[]? GetFramePalette(int frameIndex) {
            if (PaletteData == null) {
                return null;
            }

            Color[] palette = new Color[PaletteData.Length / 3];

            int palDataIndex = 0;
            for (int p = 0; palDataIndex < PaletteData.Length; p++) {
                byte r = PaletteData[palDataIndex++];
                byte g = PaletteData[palDataIndex++];
                byte b = PaletteData[palDataIndex++];
                byte a = 0xFF;
                if (TransparencyData != null && p < TransparencyData.Length) {
                    a = TransparencyData[p];
                }

                palette[p] = System.Drawing.Color.FromArgb((int)a, (int)r, (int)g, (int)b);
            }

            return palette;
        }

        public override uint[]? GetFramePaletteARGB(int frameIndex) {
            return GetPaletteDataARGB();
        }

        public uint[]? GetPaletteDataARGB() {
            if (PaletteData == null) {
                return null;
            }

            uint[] palette = new uint[PaletteData.Length / 3];

            int palDataIndex = 0;
            for (int p = 0; palDataIndex < PaletteData.Length; p++) {
                byte r = PaletteData[palDataIndex++];
                byte g = PaletteData[palDataIndex++];
                byte b = PaletteData[palDataIndex++];
                byte a = 0xFF;
                if (TransparencyData != null && p < TransparencyData.Length) {
                    a = TransparencyData[p];
                }

                palette[p] = (uint)(a << 24 | b << 16 | g << 8 | r);
            }

            return palette;
        }

        public void Write(Stream outputStream) {
            int dataLength = 0;
            int rowLength = Width * BytesPerPixel;

            // Each row of a PNG IDAT begins with a "filter type" byte.
            // This is why the buffer is 'Height' bytes bigger.
            byte[]? data = new byte[(Width * Height * BytesPerPixel) + Height];

            int dataIndex = 0;
            int inputIndex = 0;

            ColorType = BitDepth == 8 ? ColorType.Indexed : ColorType.TruecolorAlpha;

            if (ColorType == ColorType.Indexed && PaletteData != null) {
                BitDepth = PaletteData.Length > 16 ? 8 : 4;

                int samplesPerByte = BitDepth == 8 ? 1 : 2;
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
            WriteInt32BE(stream, Width);
            WriteInt32BE(stream, Height);
            stream.WriteByte((byte)(BitDepth / BytesPerPixel));
            stream.WriteByte((byte)ColorType);
            stream.WriteByte((byte)0); // Compression method (always 0)
            stream.WriteByte((byte)0); // Filter method (always 0)
            stream.WriteByte((byte)InterlaceMethod.None);
            stream.WriteCRC();

            // Write PLTE
            if (PaletteData != null) {
                stream.WriteChunkLength(PaletteData.Length);
                stream.WriteChunkHeader(System.Text.Encoding.ASCII.GetBytes("PLTE"));
                stream.Write(PaletteData);
                stream.WriteCRC();
            }

            // Write tRNS if indexed
            if (ColorType == ColorType.Indexed && TransparencyData != null) {
                stream.WriteChunkLength(TransparencyData.Length);
                stream.WriteChunkHeader(System.Text.Encoding.ASCII.GetBytes("tRNS"));
                stream.Write(TransparencyData);
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

        private class PNGWriter : Stream {
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
                WriteInt32BE(inner, length);
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
                WriteInt32BE(inner, result);
            }
        }

        private static int ReadInt32BE(BinaryReader reader) {
            return (reader.ReadByte() << 24) + (reader.ReadByte() << 16) + (reader.ReadByte() << 8) + reader.ReadByte();
        }

        public static int ReadInt32BE(byte[] bytes, int offset) {
            return (bytes[0 + offset] << 24) + (bytes[1 + offset] << 16) + (bytes[2 + offset] << 8) + bytes[3 + offset];
        }

        public static int ReadInt32BE(byte[] bytes) {
            return ReadInt32BE(bytes, 0);
        }

        private static void WriteInt32BE(Stream stream, int value) {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }
    }
}
