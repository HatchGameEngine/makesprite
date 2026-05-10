namespace makesprite {
    public class ImageFile {
        public int Width;
        public int Height;
        public int ColorDepth;
        public byte[] Data;
        public Color[]? Palette = null;
        public int TransparentPaletteIndex = -1;

        public ImageFile() {
            Width = 32;
            Height = 32;
            ColorDepth = 32;
            Data = new byte[Width * Height * (ColorDepth / 8)];
        }

        public ImageFile(int width, int height, int colorDepth, byte[] data, Color[]? palette) {
            Width = width;
            Height = height;
            ColorDepth = colorDepth;
            Data = data;
            Palette = palette;
        }

        public virtual uint[] GetFramePixels(int frameIndex, out int colorDepth) {
            uint[] pixelData = new uint[Width * Height];

            switch (ColorDepth) {
            case 8:
                for (int p = 0; p < Width * Height; p++) {
                    pixelData[p] = Data[p];
                }
                break;
            case 32:
                for (int p = 0; p < Width * Height; p++) {
                    int index = p * 4;
                    byte r = Data[index + 0];
                    byte g = Data[index + 1];
                    byte b = Data[index + 2];
                    byte a = Data[index + 3];
                    pixelData[p] = (uint)(a << 24 | b << 16 | g << 8 | r);
                }
                break;
            }

            colorDepth = ColorDepth;

            return pixelData;
        }

        public virtual Color[]? GetFramePalette(int frameIndex) {
            return Palette;
        }

        public virtual uint[]? GetFramePaletteABGR(int frameIndex) {
            Color[]? colors = GetFramePalette(frameIndex);
            if (colors == null) {
                return null;
            }

            uint[] palette = new uint[colors.Length];
            for (int p = 0; p < colors.Length; p++) {
                palette[p] = colors[p].ToABGR();
            }

            return palette;
        }

        public static ImageFile? Load(FileStream stream) {
            // Detect GIF file
            if (GIF.File.IsValid(stream)) {
                stream.Seek(0, SeekOrigin.Begin);

                return new GIF.File(stream);
            }
            stream.Seek(0, SeekOrigin.Begin);

            // Detect PNG file
            if (PNG.File.IsValid(stream)) {
                stream.Seek(0, SeekOrigin.Begin);

                return new PNG.File(stream);
            }

            return null;
        }
    }
}
