using System.IO;
using System.Drawing;

namespace PNG {
    public class Sprite : makesprite.Sprite {
        public Sprite(File file, string name) {
            Width = file.Width;
            Height = file.Height;
            ColorDepth = file.BitDepth * file.GetSamplesPerPixel();
            TransparentPaletteIndex = file.TransparentPaletteIndex;

            Layer layer = new Layer(this);
            Layers.Add(layer);

            Frame fr = new Frame(this);
            fr.Duration = 1;
            Frames.Add(fr);

            Sprite.AnimRange range = new Sprite.AnimRange(name, 0, 0, 0);
            AnimRanges.Add(range);

            uint[] pixelData = GetPixelData(file.Data, file.ColorType, file.BytesPerPixel, file.BitDepth);
            fr.PixelData.Add(pixelData);

            if (file.Palette != null) {
                Palette = GetPaletteData(file.Palette, file.TransparencyData);
            }
        }

        private uint[] GetPixelData(byte[] data, ColorType colorType, int bytesPerPixel, int bitDepth) {
            uint[] pixelData = new uint[Width * Height];

            long dataIndex = 0;

            byte GetDownscaled16BitPixel() {
                byte first = data[dataIndex++];
                byte second = data[dataIndex++];

                int us = (first << 8) + second;
                return (byte)Math.Round((255 * us) / (double)ushort.MaxValue);
            }

            // Currently we only support indexed, grayscale, or RGBA in the converter.
            // 16-bit is scaled down to 8-bit, and grayscale and RGB formats become RGBA.
            // Since the converter would change grayscale to RGBA anyway, we just do that here.
            if (bytesPerPixel > 1) {
                ColorDepth = 32;
            }

            switch (bytesPerPixel) {
            case 1:
                if (colorType == ColorType.Indexed) {
                    DecodePalettizedData(pixelData, data, bitDepth);
                    break;
                }
                else if (colorType != ColorType.Grayscale) {
                    // This shouldn't be possible
                    throw new Exception("Invalid PNG file");
                }

                for (int p = 0; p < Width * Height; p++) {
                    byte value = data[dataIndex++];
                    pixelData[p] = (uint)(value << 16 | value << 8 | value) | 0xFF000000;
                }

                ColorDepth = 32;
                break;
            case 2:
                if (colorType == ColorType.Grayscale) {
                    for (int p = 0; p < Width * Height; p++) {
                        byte value = GetDownscaled16BitPixel();
                        pixelData[p] = (uint)(value << 16 | value << 8 | value) | 0xFF000000;
                    }
                }
                else {
                    for (int p = 0; p < Width * Height; p++) {
                        byte value = data[dataIndex++];
                        byte alpha = data[dataIndex++];
                        pixelData[p] = (uint)(alpha << 24 | value << 16 | value << 8 | value);
                    }
                }
                break;
            case 3:
                for (int p = 0; p < Width * Height; p++) {
                    byte r = data[dataIndex++];
                    byte g = data[dataIndex++];
                    byte b = data[dataIndex++];
                    pixelData[p] = (uint)(b << 16 | g << 8 | r) | 0xFF000000;
                }
                break;
            case 4:
                if (colorType == ColorType.GrayscaleAlpha) {
                    for (int p = 0; p < Width * Height; p++) {
                        byte value = GetDownscaled16BitPixel();
                        byte alpha = GetDownscaled16BitPixel();
                        pixelData[p] = (uint)(alpha << 24 | value << 16 | value << 8 | value);
                    }
                }
                else {
                    for (int p = 0; p < Width * Height; p++) {
                        byte r = data[dataIndex++];
                        byte g = data[dataIndex++];
                        byte b = data[dataIndex++];
                        byte a = data[dataIndex++];
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

        private uint[] GetPaletteData(byte[] data, byte[]? transparencyData) {
            uint[] palette = new uint[data.Length / 3];

            int palDataIndex = 0;
            for (int p = 0; palDataIndex < data.Length; p++) {
                byte r = data[palDataIndex++];
                byte g = data[palDataIndex++];
                byte b = data[palDataIndex++];
                byte a = 0xFF;
                if (transparencyData != null && p < transparencyData.Length) {
                    a = transparencyData[p];
                }

                palette[p] = (uint)(a << 24 | b << 16 | g << 8 | r);
            }

            return palette;
        }
    }
}
