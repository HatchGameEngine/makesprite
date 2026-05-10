namespace makesprite {
    public class SpritesheetImage {
        public int Width;
        public int Height;
        public int ColorDepth;
        public byte[] Data;
        public Color[]? Palette;

        public SpritesheetImage(int width, int height, int colorDepth, Color[]? palette) {
            Width = width;
            Height = height;
            ColorDepth = colorDepth;
            Data = new byte[width * height * (colorDepth / 8)];
            Palette = palette;
        }

        public SpritesheetImage(int width, int height, int colorDepth, byte[] data, Color[]? palette) {
            Width = width;
            Height = height;
            ColorDepth = colorDepth;
            Data = data;
            Palette = palette;
        }
    }
}
