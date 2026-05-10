public class Color {
    public byte Red;
    public byte Green;
    public byte Blue;
    public byte Alpha;

    public static Color Transparent = new Color(0, 0, 0, 0);

    public Color(byte red, byte green, byte blue, byte alpha) {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    public Color(byte red, byte green, byte blue) {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = 0xFF;
    }

    public Color(byte value, byte alpha) {
        Red = value;
        Green = value;
        Blue = value;
        Alpha = alpha;
    }

    public Color(byte index) {
        Red = index;
        Green = 0;
        Blue = 0;
        Alpha = 0xFF;
    }

    public Color(uint argb) {
        Alpha = (byte)((argb & 0xFF000000) >> 24);
        Blue = (byte)((argb & 0xFF0000) >> 16);
        Green = (byte)((argb & 0xFF00) >> 8);
        Red = (byte)(argb & 0xFF);
    }

    public uint ToABGR() {
        return (uint)(Alpha << 24 | Blue << 16 | Green << 8 | Red);
    }
}
