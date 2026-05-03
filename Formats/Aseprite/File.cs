using System.IO;
using System.IO.Compression;

namespace Aseprite {
    public class File {
        public uint Flags;
        public byte PixelWidth;
        public byte PixelHeight;
        public short GridX;
        public short GridY;
        public ushort GridWidth;
        public ushort GridHeight;

        public enum ColorProfiles : ushort {
            None = 0,
            sRGB,
            ICC,
        }

        public static bool IsValid(BinaryReader reader) {
            reader.ReadUInt32();
            return reader.ReadUInt16() == 0xA5E0;
        }

        public static bool IsValid(Stream stream) {
            return IsValid(new BinaryReader(stream));
        }

        public Sprite Read(Stream stream) {
            return Read(new BinaryReader(stream));
        }

        public Sprite Read(BinaryReader reader) {
            Sprite sprite = new Sprite();

            uint fileSize = reader.ReadUInt32();
            ushort magicNumber = reader.ReadUInt16();
            ushort frameCount = reader.ReadUInt16();

            sprite.Width = reader.ReadUInt16();
            sprite.Height = reader.ReadUInt16();
            sprite.ColorDepth = reader.ReadUInt16();
            Flags = reader.ReadUInt32();
            reader.ReadUInt16(); // formerly Speed
            reader.ReadUInt32();
            reader.ReadUInt32();
            sprite.TransparentPaletteIndex = reader.ReadByte();
            reader.BaseStream.Seek(3, SeekOrigin.Current);
            reader.ReadUInt16(); // Number of colors
            PixelWidth = reader.ReadByte();
            PixelHeight = reader.ReadByte();
            GridX = reader.ReadInt16();
            GridY = reader.ReadInt16();
            GridWidth = reader.ReadUInt16();
            GridHeight = reader.ReadUInt16();

            // Padding
            reader.BaseStream.Seek(84, SeekOrigin.Current);

            for (int i = 0; i < frameCount; i++) {
                sprite.Frames.Add(ReadFrame(sprite, reader));
            }

            // Resolve linked frames
            int frameSize = sprite.Width * sprite.Height;
            for (int i = 0; i < frameCount; i++) {
                Sprite.Frame f = (Sprite.Frame)sprite.Frames[i];
                for (int l = 0; l < sprite.Layers.Count; l++) {
                    int linkedFrameIndex = f.LinkedFrameIndices[l];
                    if (linkedFrameIndex != -1 && linkedFrameIndex < frameCount) {
                        Sprite.Frame linkedFrame = (Sprite.Frame)sprite.Frames[linkedFrameIndex];
                        Array.Copy(linkedFrame.PixelData[l], f.PixelData[l], frameSize);
                    }
                }
            }

            return sprite;
        }

        public Sprite.Frame ReadFrame(Sprite sprite, BinaryReader reader) {
            Sprite.Frame frame = new Sprite.Frame(sprite);

            uint frameSize = reader.ReadUInt32();
            ushort magicNumber = reader.ReadUInt16();

            ushort oldChunkCount = reader.ReadUInt16();
            ushort duration = reader.ReadUInt16();
            reader.ReadUInt16();
            uint newChunkCount = reader.ReadUInt32();

            uint chunkCount = newChunkCount;
            if (chunkCount == 0) {
                chunkCount = oldChunkCount;
            }

            frame.Duration = (duration * 60 + 999) / 1000; // ceil

            for (int i = 0; i < sprite.Layers.Count; i++) {
                frame.PixelData.Add(new uint[sprite.Width * sprite.Height]);
                frame.LinkedFrameIndices.Add(-1);
            }

            for (uint i = 0; i < chunkCount; i++) {
                uint chunkSize = reader.ReadUInt32();
                ushort chunkType = reader.ReadUInt16();

                switch (chunkType) {
                    // Old palette chunk
                    case 0x0004:
                    case 0x0011:
                        ReadOldPaletteChunk(reader, sprite, chunkSize);
                        break;
                    // Layer Chunk
                    case 0x2004:
                        ReadLayerChunk(reader, sprite, frame, chunkSize);
                        break;
                    // Cel Chunk
                    case 0x2005:
                        ReadCelChunk(reader, sprite, frame, chunkSize);
                        break;
                    // Color Profile Chunk
                    case 0x2007:
                        ReadColorProfileChunk(reader, sprite, chunkSize);
                        break;
                    // Tags Chunk
                    case 0x2018:
                        ReadTagsChunk(reader, sprite, chunkSize);
                        break;
                    // Palette Chunk
                    case 0x2019:
                        ReadPaletteChunk(reader, sprite, chunkSize);
                        break;
                    // Cel Extra Chunk
                    case 0x2006:
                    // Mask Chunk
                    case 0x2016:
                    // User Data Chunk
                    case 0x2020:
                        reader.BaseStream.Seek(chunkSize - 6, SeekOrigin.Current);
                        break;
                    // External Files Chunk
                    case 0x2008:
                    // Slice Chunk
                    case 0x2022:
                    // Tileset Chunk
                    case 0x2023:
                        Console.WriteLine("Unimplemented Chunk type: 0x" + chunkType.ToString("X04"));
                        reader.BaseStream.Seek(chunkSize - 6, SeekOrigin.Current);
                        break;
                    // Fallback
                    default:
                        Console.WriteLine("Unknown Chunk type: 0x" + chunkType.ToString("X04"));
                        reader.BaseStream.Seek(chunkSize - 6, SeekOrigin.Current);
                        break;
                }
            }

            return frame;
        }

        private void ReadOldPaletteChunk(BinaryReader reader, Sprite sprite, uint chunkSize) {
            // .ase specs tell us to ignore these chunks if there is
            // a chunk of type 0x2019
            if (sprite.Palette != null) {
                reader.BaseStream.Seek(chunkSize - 6, SeekOrigin.Current);
                return;
            }

            int ind = 0, total = 0;
            ushort packetCount = reader.ReadUInt16();
            for (int p = 0; p < packetCount; p++) {
                byte paletteEntries = reader.ReadByte();
                int colorCount = reader.ReadByte();
                if (colorCount == 0) {
                    colorCount = 256;
                }

                total += colorCount;
                Array.Resize(ref sprite.Palette, total);

                for (int c = 0; c < colorCount; c++) {
                    byte r = reader.ReadByte();
                    byte g = reader.ReadByte();
                    byte b = reader.ReadByte();
                    uint argb = (uint)(b << 16 | g << 8 | r) | 0xFF000000;

                    sprite.Palette[ind + c] = argb;
                }

                ind += colorCount;
            }
        }

        private void ReadLayerChunk(BinaryReader reader, Sprite sprite, Sprite.Frame frame, uint chunkSize) {
            ushort flags = reader.ReadUInt16();
            ushort layerType = reader.ReadUInt16();
            ushort layerChildLevel = reader.ReadUInt16();

            reader.ReadUInt16(); // default layer width (ignored)
            reader.ReadUInt16(); // default layer height (ignored)

            ushort blendMode = reader.ReadUInt16();
            byte opacity = reader.ReadByte();
            reader.BaseStream.Seek(3, SeekOrigin.Current);

            string layerName = ReadString(reader);

            if (layerType == (int)Sprite.Layer.LayerTypes.Tilemap) {
                Console.WriteLine("Tilemap layers are not supported! Ignoring layer \"" + layerName + "\"");
                return;
            }

            Sprite.Layer layer = new Sprite.Layer(sprite, layerName, flags, layerType, blendMode);
            sprite.AddLayer(layer, layerChildLevel);

            frame.PixelData.Add(new uint[sprite.Width * sprite.Height]);
            frame.LinkedFrameIndices.Add(-1);
        }

        private void ReadCelChunk(BinaryReader reader, Sprite sprite, Sprite.Frame frame, uint chunkSize) {
            long endPos = reader.BaseStream.Position + chunkSize - 6;

            ushort layerIndex = reader.ReadUInt16();
            short posX = reader.ReadInt16();
            short posY = reader.ReadInt16();
            byte opacity = reader.ReadByte();

            ushort celType = reader.ReadUInt16();
            reader.BaseStream.Seek(7, SeekOrigin.Current);

            switch (celType) {
                // Raw cel
                case 0:
                    ReadRawCel(reader, sprite, frame.PixelData[layerIndex], posX, posY);
                    break;
                // Linked cel
                case 1:
                    ushort framePos = reader.ReadUInt16();
                    frame.LinkedFrameIndices[layerIndex] = (int)framePos;
                    break;
                // Compressed Image
                case 2:
                    ReadCompressedImage(reader, sprite, frame.PixelData[layerIndex], posX, posY, endPos);
                    break;
                default:
                    Console.WriteLine("Unknown Cel type: 0x" + celType.ToString("X04"));
                    break;
            }
        }

        private void ReadRawCel(BinaryReader reader, Sprite sprite, uint[] pixelData, short posX, short posY) {
            ushort cwidth = reader.ReadUInt16();
            ushort cheight = reader.ReadUInt16();

            switch (sprite.ColorDepth) {
                case 8:
                    for (int y = 0; y < cheight; y++) {
                        for (int x = 0; x < cwidth; x++) {
                            pixelData[posX + x + (posY + y) * sprite.Width] = reader.ReadByte();
                        }
                    }
                    break;
                case 16:
                    for (int y = 0; y < cheight; y++) {
                        for (int x = 0; x < cwidth; x++) {
                            pixelData[posX + x + (posY + y) * sprite.Width] = reader.ReadUInt16();
                        }
                    }
                    break;
                case 32:
                    for (int y = 0; y < cheight; y++) {
                        for (int x = 0; x < cwidth; x++) {
                            pixelData[posX + x + (posY + y) * sprite.Width] = reader.ReadUInt32();
                        }
                    }
                    break;
            }
        }

        private void ReadCompressedImage(BinaryReader reader, Sprite sprite, uint[] pixelData, short posX, short posY, long endPos) {
            ushort cpwidth = reader.ReadUInt16();
            ushort cpheight = reader.ReadUInt16();
            reader.ReadUInt16();

            uint compressedSize = (uint)(endPos - reader.BaseStream.Position);

            MemoryStream outMemoryStream = new MemoryStream();
            MemoryStream inMemoryStream = new MemoryStream();
            inMemoryStream.Write(reader.ReadBytes((int)compressedSize), 0, (int)compressedSize);
            inMemoryStream.Position = 0;

            DeflateStream decompress = new DeflateStream(inMemoryStream, CompressionMode.Decompress);
            decompress.CopyTo(outMemoryStream);
            outMemoryStream.Position = 0;

            BinaryReader pxReader = new BinaryReader(outMemoryStream);

            for (int y = 0; y < cpheight; y++) {
                for (int x = 0; x < cpwidth; x++) {
                    uint value = 0;
                    switch (sprite.ColorDepth) {
                        case 8:
                            value = pxReader.ReadByte();
                            break;
                        case 16:
                            value = pxReader.ReadUInt16();
                            break;
                        case 32:
                            value = pxReader.ReadUInt32();
                            break;
                    }
                    int px = posX + x;
                    int py = posY + y;
                    if (px >= 0 && py >= 0 && px < sprite.Width && py < sprite.Height) {
                        pixelData[px + py * sprite.Width] = value;
                    }
                }
            }
        }

        private void ReadColorProfileChunk(BinaryReader reader, Sprite sprite, uint chunkSize) {
            ushort type = reader.ReadUInt16();
            ushort flags = reader.ReadUInt16();
            float fixedGamma = reader.ReadSingle();

            reader.BaseStream.Seek(8, SeekOrigin.Current);
            if (type == (ushort)ColorProfiles.ICC) {
                uint dataLen = reader.ReadUInt32();
                reader.BaseStream.Seek(dataLen, SeekOrigin.Current);
            }
        }

        private void ReadTagsChunk(BinaryReader reader, Sprite sprite, uint chunkSize) {
            ushort tagCount = reader.ReadUInt16();
            reader.BaseStream.Seek(8, SeekOrigin.Current);

            for (ushort t = 0; t < tagCount; t++) {
                ushort frameStart = reader.ReadUInt16();
                ushort frameEnd = reader.ReadUInt16();
                byte loopAnimDirection = reader.ReadByte();
                // 0 - Forward
                // 1 - Reverse
                // 2 - Ping-pong
                reader.BaseStream.Seek(8, SeekOrigin.Current);

                byte r = reader.ReadByte();
                byte g = reader.ReadByte();
                byte b = reader.ReadByte();
                reader.ReadByte();

                string tagName = ReadString(reader);

                sprite.AnimRanges.Add(new Sprite.AnimRange(tagName, frameStart, frameEnd, loopAnimDirection));
            }
        }

        private void ReadPaletteChunk(BinaryReader reader, Sprite sprite, uint chunkSize) {
            uint paletteSize = reader.ReadUInt32();
            uint colorIndexFirst = reader.ReadUInt32();
            uint colorIndexLast = reader.ReadUInt32();
            reader.BaseStream.Seek(8, SeekOrigin.Current);

            sprite.Palette = new uint[paletteSize];

            for (uint p = colorIndexFirst; p <= colorIndexLast; p++) {
                ushort flags = reader.ReadUInt16();
                byte r = reader.ReadByte();
                byte g = reader.ReadByte();
                byte b = reader.ReadByte();
                byte a = reader.ReadByte();
                uint argb = (uint)(a << 24 | b << 16 | g << 8 | r);

                sprite.Palette[p] = argb;

                if ((flags & 1) != 0) {
                    // Read color name
                    ReadString(reader);
                }
            }
        }

        public static string ReadString(BinaryReader reader) {
            string str = "";
            ushort length = reader.ReadUInt16();
            for (ushort i = 0; i < length; i++) {
                char it = (char)reader.ReadByte();
                str += it;
            }
            return str;
        }
    }
}
