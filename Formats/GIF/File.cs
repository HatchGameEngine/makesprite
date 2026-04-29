using System.IO;
using System.Drawing;

// Code portions taken from the public domain library gifdec
namespace GIF {
    public class Frame {
        public byte[] Data;
        public ushort Delay;
        public byte TransparentPaletteIndex;
        public Color[]? Palette = null;

        public Frame(ushort width, ushort height) {
            Data = new byte[width * height];
        }
    };

    public class File {
        public ushort Width;
        public ushort Height;
        public List<Frame> Frames = new List<Frame>();
        public byte TransparentPaletteIndex;
        public ushort Delay;
        public Color[] Palette = new Color[256];
        public ushort NumPaletteColors = 0;

        private class CodeTableEntry {
            public short Length;
            public short Prefix;
            public byte Suffix;
        };

        static public bool ReadAndValidateHeader(BinaryReader reader) {
            byte[] magicGIF = reader.ReadBytes(3);
            string stringMagicGIF = System.Text.Encoding.ASCII.GetString(magicGIF);
            if (stringMagicGIF != "GIF") {
                return false;
            }

            byte[] magic89a = reader.ReadBytes(3);
            string stringMagic89a = System.Text.Encoding.ASCII.GetString(magic89a);
            if (!(stringMagic89a == "89a" || stringMagic89a == "87a")) {
                return false;
            }

            return true;
        }

        static public bool IsValid(BinaryReader reader) {
            if (ReadAndValidateHeader(reader)) {
                return true;
            }

            return false;
        }

        static public bool IsValid(Stream stream) {
            return IsValid(new BinaryReader(stream));
        }

        public File(BinaryReader reader) {
            if (!ReadAndValidateHeader(reader)) {
                throw new Exception("Invalid GIF file");
            }

            Width = reader.ReadUInt16();
            Height = reader.ReadUInt16();

            byte logicalScreenDesc = reader.ReadByte();
            TransparentPaletteIndex = reader.ReadByte();
            reader.BaseStream.Seek(1, SeekOrigin.Current);

            byte colorBitDepth = (byte)(((logicalScreenDesc & 0x70) >> 4) + 1);
            if ((logicalScreenDesc & 0x80) != 0) {
                NumPaletteColors = (ushort)(2 << (logicalScreenDesc & 0x7));
            }

            // Prepare image data
            int canvasSize = Width * Height;
            byte[] canvas = new byte[canvasSize];

            // Load palette
            for (int p = 0; p < NumPaletteColors; p++) {
                int red = reader.ReadByte();
                int green = reader.ReadByte();
                int blue = reader.ReadByte();
                int alpha = p == TransparentPaletteIndex ? 0 : 0xFF;

                // Store color
                Palette[p] = System.Drawing.Color.FromArgb(alpha, red, green, blue);
            }

            int widthMinusOne = Width - 1;
            int heightMinusOne = Height - 1;
            int eighthHeight = Height >> 3;
            int quarterHeight = Height >> 2;
            int halfHeight = Height >> 1;

            CodeTableEntry[] codeTable = new CodeTableEntry[0x1000];
            for (int i = 0; i < codeTable.Length; i++) {
                codeTable[i] = new CodeTableEntry();
            }

            ushort frameDelay = 1;
            int disposal = 0;
            bool hasTransparency = false;
            byte transparentColorIndex = TransparentPaletteIndex;

            // Get frame
            byte type = reader.ReadByte();
            while (type != 0) {
                int strLen = 0, frmOff = 0;

                switch (type) {
                // Extension
                case 0x21: {
                    byte subtype = reader.ReadByte();
                    switch (subtype) {
                    // Graphics Control Extension
                    case 0xF9: {
                        reader.BaseStream.Seek(1, SeekOrigin.Current);

                        byte bits = reader.ReadByte();
                        disposal = (bits >> 2) & 3;
                        hasTransparency = (bits & 1) != 0;

                        frameDelay = reader.ReadUInt16();
                        transparentColorIndex = reader.ReadByte();

                        reader.BaseStream.Seek(1, SeekOrigin.Current);
                        break;
                    }
                    // Plain Text Extension
                    case 0x01:
                    // Comment Extension
                    case 0xFE:
                    // Application Extension
                    case 0xFF: {
                        byte blockSize = reader.ReadByte();
                        // Continue until we run out of blocks
                        while (blockSize > 0) {
                            reader.BaseStream.Seek(blockSize, SeekOrigin.Current); // Skip block
                            blockSize = reader.ReadByte(); // Next block Size
                        }
                        break;
                    }
                    default:
                        throw new Exception("Unsupported GIF control extension");
                    }
                    break;
                }
                // Image descriptor
                case 0x2C: {
                    ushort frameX = reader.ReadUInt16();
                    ushort frameY = reader.ReadUInt16();

                    if (frameX >= Width || frameY >= Height) {
                        throw new Exception("Invalid GIF Image Descriptor");
                    }

                    ushort frameWidth = reader.ReadUInt16();
                    ushort frameHeight = reader.ReadUInt16();

                    frameWidth = (ushort)Math.Min(frameWidth, Width - frameX);
                    frameHeight = (ushort)Math.Min(frameHeight, Height - frameY);

                    Color[]? framePalette = null;

                    byte packedField = reader.ReadByte();

                    // If a local color table exists,
                    if ((packedField & 0x80) != 0) {
                        int size = 2 << (packedField & 0x07);

                        framePalette = new Color[size];

                        // Load all colors
                        for (int p = 0; p < size; p++) {
                            int red = reader.ReadByte();
                            int green = reader.ReadByte();
                            int blue = reader.ReadByte();
                            int alpha = p == TransparentPaletteIndex ? 0 : 0xFF;

                            // Store color
                            framePalette[p] = System.Drawing.Color.FromArgb(alpha, red, green, blue);
                        }
                    }

                    int bitsWidth = 0;

                    bool interlaced = (packedField & 0x40) == 0x40;
                    if (interlaced) {
                        if ((widthMinusOne & (widthMinusOne - 1)) != 0) {
                            throw new Exception("Interlaced GIF width must be power of two");
                        }
                        if ((heightMinusOne & (heightMinusOne - 1)) != 0) {
                            throw new Exception("Interlaced GIF height must be power of two");
                        }

                        while (widthMinusOne > 0) {
                            widthMinusOne >>= 1;
                            bitsWidth++;
                        }
                        widthMinusOne = Width - 1;
                    }

                    int codeSize = reader.ReadByte();
                    int clearCode = 1 << codeSize;
                    int eoiCode = clearCode + 1;
                    int emptyCode = eoiCode + 1;

                    codeSize++;
                    int initCodeSize = codeSize;

                    CodeTableEntry entry;

                    // Init table
                    for (int i = 0; i <= eoiCode; i++) {
                        entry = new CodeTableEntry();
                        entry.Length = 1;
                        entry.Prefix = 0xFFF;
                        entry.Suffix = (byte)i;
                        codeTable[i] = entry;
                    }

                    int blockLength = 0;
                    int bitCache = 0;
                    int bitCacheLength = 0;
                    bool tableFull = false;

                    int currentCode = ReadCode(reader, codeSize, ref blockLength, ref bitCache, ref bitCacheLength);

                    codeSize = initCodeSize;
                    emptyCode = eoiCode + 1;

                    entry = new CodeTableEntry();

                    // Clear the canvas
                    if (disposal == 2) {
                        for (int p = 0; p < canvasSize; p++) {
                            canvas[p] = transparentColorIndex;
                        }
                    }

                    while (blockLength > 0) {
                        bool mark = false;

                        if (currentCode == clearCode) {
                            codeSize = initCodeSize;
                            emptyCode = eoiCode + 1;
                            tableFull = false;
                        }
                        else if (!tableFull) {
                            codeTable[emptyCode].Length = (short)(strLen + 1);
                            codeTable[emptyCode].Prefix = (short)currentCode;
                            codeTable[emptyCode].Suffix = entry.Suffix;
                            emptyCode++;

                            // Once we reach highest code, increase code size
                            if ((emptyCode & (emptyCode - 1)) == 0) {
                                mark = true;
                            }

                            if (emptyCode >= 0x1000) {
                                mark = false;
                                tableFull = true;
                            }
                        }

                        currentCode = ReadCode(reader, codeSize, ref blockLength, ref bitCache, ref bitCacheLength);

                        if (currentCode == clearCode) {
                            continue;
                        }
                        else if (currentCode == eoiCode) {
                            break;
                        }

                        if (mark) {
                            codeSize++;
                        }

                        entry = codeTable[currentCode];
                        strLen = entry.Length;

                        for (var i = 0; i < strLen; i++) {
                            int p = frmOff + entry.Length - 1;
                            int x = p % frameWidth;
                            int y = p / frameWidth;

                            if (interlaced) {
                                int row = p >> bitsWidth;
                                int offset = 0;

                                if (row < eighthHeight) {
                                    offset = row << 3;
                                }
                                else if (row < quarterHeight) {
                                    offset = ((row - eighthHeight) << 3) + 4;
                                }
                                else if (row < halfHeight) {
                                    offset = ((row - quarterHeight) << 2) + 2;
                                }
                                else {
                                    offset = ((row - halfHeight) << 1) + 1;
                                }

                                p = (p & widthMinusOne) + (offset << bitsWidth);
                            }

                            canvas[((frameY + y) * Width) + (frameX + x)] = entry.Suffix;

                            if (entry.Prefix != 0xFFF) {
                                entry = codeTable[entry.Prefix];
                            }
                            else {
                                break;
                            }
                        }

                        frmOff += strLen;

                        if (currentCode < emptyCode - 1 && !tableFull) {
                            codeTable[emptyCode - 1].Suffix = entry.Suffix;
                        }
                    }

                    Frame frame = new Frame(Width, Height);
                    frame.Palette = framePalette;
                    frame.Delay = frameDelay;
                    frame.TransparentPaletteIndex = transparentColorIndex;
                    Array.Copy(canvas, frame.Data, canvasSize);

                    Frames.Add(frame);
                    break;
                }
                }

                type = reader.ReadByte();
                if (type == 0x3B) {
                    break;
                }
            }
        }

        public File(Stream stream) : this(new BinaryReader(stream)) {}

        private int ReadCode(BinaryReader reader, int codeSize, ref int blockLength, ref int bitCache, ref int bitCacheLength) {
            if (blockLength == 0) {
                blockLength = reader.ReadByte();
            }

            while (bitCacheLength <= codeSize && blockLength > 0) {
                int read = reader.ReadByte();
                blockLength--;
                bitCache |= read << bitCacheLength;
                bitCacheLength += 8;

                if (blockLength == 0) {
                    blockLength = reader.ReadByte();
                }
            }

            int result = bitCache & ((1 << codeSize) - 1);
            bitCache >>= codeSize;
            bitCacheLength -= codeSize;

            return result;
        }
    }
}
