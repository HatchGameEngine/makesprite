using System.IO;
using System.IO.Compression;

namespace Aseprite {
    public class ASE : makesprite.Sprite {
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

        public ASE(Stream stream) : this(new BinaryReader(stream)) { }

        internal ASE(BinaryReader reader) {
            uint fileSize = reader.ReadUInt32();
            ushort magicNumber = reader.ReadUInt16();
            ushort frameCount = reader.ReadUInt16();

            Width = reader.ReadUInt16();
            Height = reader.ReadUInt16();
            ColorDepth = reader.ReadUInt16();
            Flags = reader.ReadUInt32();
            reader.ReadUInt16(); // formerly Speed
            reader.ReadUInt32();
            reader.ReadUInt32();
            TransparentPaletteIndex = reader.ReadByte();
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
                Frames.Add(new Frame(this, reader));
            }

            // Resolve linked frames
            for (int i = 0; i < frameCount; i++) {
                ASE.Frame f = (ASE.Frame)Frames[i];
                for (int l = 0; l < Layers.Count; l++) {
                    int linkedFrameIndex = f.LinkedFrameIndices[l];
                    if (linkedFrameIndex != -1 && linkedFrameIndex < frameCount) {
                        Frame linkedFrame = (ASE.Frame)Frames[linkedFrameIndex];
                        Array.Copy(linkedFrame.PixelData[l], f.PixelData[l], Width * Height);
                    }
                }
            }
        }

        public static bool IsFile(Stream stream) {
            return IsFile(new BinaryReader(stream));
        }

        public static bool IsFile(BinaryReader reader) {
            reader.ReadUInt32();
            return reader.ReadUInt16() == 0xA5E0;
        }

        public void AddLayer(Layer layer, int childLevel) {
            layer.ChildLevel = childLevel;

            for (int i = Layers.Count - 1; i >= 0; i--) {
                ASE.Layer otherLayer = (ASE.Layer)Layers[i];
                if (otherLayer.ChildLevel == childLevel - 1) {
                    otherLayer.AddChild(layer);
                    break;
                }
            }

            Layers.Add(layer);
        }

        public new class Layer : makesprite.Sprite.Layer {
            public int Flags;
            public int Type;
            public int ChildLevel = 0;

            public enum LayerTypes {
                Image = 0,
                Group = 1,
                Tilemap = 2
            }

            public enum FlagTypes {
                Visible = 1,
                Editable = 2,
                MovementLocked = 4,
                Background = 8,
                PreferLinkedCels = 16,
                Collapsed = 32,
                Reference = 64
            }

            public Layer(makesprite.Sprite owner, string name, int flags, int type, int blendMode) : base(owner, name, true, blendMode) {
                Flags = flags;
                Type = type;

                if (Type == (int)LayerTypes.Group) {
                    IsGroup = true;
                }
                if (IsGroup || (Flags & (int)FlagTypes.Visible) == 0) {
                    Visible = false;
                }
                if ((Flags & (int)FlagTypes.Background) != 0) {
                    IsBackground = true;
                }
            }
        }

        public new class Frame : makesprite.Sprite.Frame {
            public List<int> LinkedFrameIndices = new List<int>();

            internal Frame(ASE ase, BinaryReader reader) : base((makesprite.Sprite)ase) {
                uint frameSize = reader.ReadUInt32();
                ushort magicNumber = reader.ReadUInt16();

                ushort oldChunkCount = reader.ReadUInt16();
                Duration = reader.ReadUInt16();
                reader.ReadUInt16();
                uint newChunkCount = reader.ReadUInt32();

                uint chunkCount = newChunkCount;
                if (chunkCount == 0)
                    chunkCount = oldChunkCount;

                for (int i = 0; i < ase.Layers.Count; i++) {
                    PixelData.Add(new uint[ase.Width * ase.Height]);
                    LinkedFrameIndices.Add(-1);
                }

                for (uint i = 0; i < chunkCount; i++) {
                    uint chunkSize = reader.ReadUInt32();
                    ushort chunkType = reader.ReadUInt16();

                    switch (chunkType) {
                        // Old palette chunk
                        case 0x0004:
                        case 0x0011:
                            ReadOldPaletteChunk(reader, ase, chunkSize);
                            break;
                        // Layer Chunk
                        case 0x2004:
                            ReadLayerChunk(reader, ase, chunkSize);
                            break;
                        // Cel Chunk
                        case 0x2005:
                            ReadCelChunk(reader, ase, chunkSize);
                            break;
                        // Color Profile Chunk
                        case 0x2007:
                            ReadColorProfileChunk(reader, ase, chunkSize);
                            break;
                        // Tags Chunk
                        case 0x2018:
                            ReadTagsChunk(reader, ase, chunkSize);
                            break;
                        // Palette Chunk
                        case 0x2019:
                            ReadPaletteChunk(reader, ase, chunkSize);
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
            }

            private void ReadOldPaletteChunk(BinaryReader reader, ASE ase, uint chunkSize) {
                // .ase specs tell us to ignore these chunks if there is
                // a chunk of type 0x2019
                if (ase.Palette != null) {
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
                    Array.Resize(ref ase.Palette, total);

                    for (int c = 0; c < colorCount; c++) {
                        byte r = reader.ReadByte();
                        byte g = reader.ReadByte();
                        byte b = reader.ReadByte();
                        uint argb = (uint)(b << 16 | g << 8 | r) | 0xFF000000;

                        ase.Palette[ind + c] = argb;
                    }

                    ind += colorCount;
                }
            }

            private void ReadLayerChunk(BinaryReader reader, ASE ase, uint chunkSize) {
                ushort flags = reader.ReadUInt16();
                ushort layerType = reader.ReadUInt16();
                ushort layerChildLevel = reader.ReadUInt16();

                reader.ReadUInt16(); // default layer width (ignored)
                reader.ReadUInt16(); // default layer height (ignored)

                ushort blendMode = reader.ReadUInt16();
                byte opacity = reader.ReadByte();
                reader.BaseStream.Seek(3, SeekOrigin.Current);

                string layerName = ReadString(reader);

                if (layerType == (int)Layer.LayerTypes.Tilemap) {
                    Console.WriteLine("Tilemap layers are not supported! Ignoring layer \"" + layerName + "\"");
                    return;
                }

                Layer layer = new Layer(ase, layerName, flags, layerType, blendMode);
                ase.AddLayer(layer, layerChildLevel);

                PixelData.Add(new uint[ase.Width * ase.Height]);
                LinkedFrameIndices.Add(-1);
            }

            private void ReadCelChunk(BinaryReader reader, ASE ase, uint chunkSize) {
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
                        ReadRawCel(reader, ase, layerIndex, posX, posY);
                        break;
                    // Linked cel
                    case 1:
                        ushort framePos = reader.ReadUInt16();
                        LinkedFrameIndices[layerIndex] = (int)framePos;
                        break;
                    // Compressed Image
                    case 2:
                        ReadCompressedImage(reader, ase, layerIndex, posX, posY, endPos);
                        break;
                    default:
                        Console.WriteLine("Unknown Cel type: 0x" + celType.ToString("X04"));
                        break;
                }
            }

            private void ReadRawCel(BinaryReader reader, ASE ase, ushort layerIndex, short posX, short posY) {
                ushort cwidth = reader.ReadUInt16();
                ushort cheight = reader.ReadUInt16();

                switch (ase.ColorDepth) {
                    case 8:
                        for (int y = 0; y < cheight; y++) {
                            for (int x = 0; x < cwidth; x++) {
                                PixelData[layerIndex][posX + x + (posY + y) * ase.Width] = reader.ReadByte();
                            }
                        }
                        break;
                    case 16:
                        for (int y = 0; y < cheight; y++) {
                            for (int x = 0; x < cwidth; x++) {
                                PixelData[layerIndex][posX + x + (posY + y) * ase.Width] = reader.ReadUInt16();
                            }
                        }
                        break;
                    case 32:
                        for (int y = 0; y < cheight; y++) {
                            for (int x = 0; x < cwidth; x++) {
                                PixelData[layerIndex][posX + x + (posY + y) * ase.Width] = reader.ReadUInt32();
                            }
                        }
                        break;
                }
            }

            private void ReadCompressedImage(BinaryReader reader, ASE ase, ushort layerIndex, short posX, short posY, long endPos) {
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
                        switch (ase.ColorDepth) {
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
                        if (px >= 0 && py >= 0 && px < ase.Width && py < ase.Height) {
                            PixelData[layerIndex][px + py * ase.Width] = value;
                        }
                    }
                }
            }

            private void ReadColorProfileChunk(BinaryReader reader, ASE ase, uint chunkSize) {
                ushort type = reader.ReadUInt16();
                ushort flags = reader.ReadUInt16();
                float fixedGamma = reader.ReadSingle();

                reader.BaseStream.Seek(8, SeekOrigin.Current);
                if (type == (ushort)ColorProfiles.ICC) {
                    uint dataLen = reader.ReadUInt32();
                    reader.BaseStream.Seek(dataLen, SeekOrigin.Current);
                }
            }

            private void ReadTagsChunk(BinaryReader reader, ASE ase, uint chunkSize) {
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

                    ase.AnimRanges.Add(new AnimRange(tagName, frameStart, frameEnd, loopAnimDirection));
                }
            }

            private void ReadPaletteChunk(BinaryReader reader, ASE ase, uint chunkSize) {
                uint paletteSize = reader.ReadUInt32();
                uint colorIndexFirst = reader.ReadUInt32();
                uint colorIndexLast = reader.ReadUInt32();
                reader.BaseStream.Seek(8, SeekOrigin.Current);

                ase.Palette = new uint[paletteSize];

                for (uint p = colorIndexFirst; p <= colorIndexLast; p++) {
                    ushort flags = reader.ReadUInt16();
                    byte r = reader.ReadByte();
                    byte g = reader.ReadByte();
                    byte b = reader.ReadByte();
                    byte a = reader.ReadByte();
                    uint argb = (uint)(a << 24 | b << 16 | g << 8 | r);

                    ase.Palette[p] = argb;

                    if ((flags & 1) != 0) {
                        // Read color name
                        ReadString(reader);
                    }
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
