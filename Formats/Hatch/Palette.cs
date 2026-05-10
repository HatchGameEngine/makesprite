namespace Hatch {
    public class Palette {
        private const int MAX_ROWS = 16;

        public static void Write(Color[] colors, BinaryWriter writer) {
            writer.Write((uint)0x4C415048); // Signature
            writer.Write((uint)1); // Palette count

            // Count up how many palette rows are being used
            int rowsUsed = (int)Math.Ceiling((float)colors.Length / MAX_ROWS);
            int activeRowsBits = 0;
            for (int i = 0; i < rowsUsed; i++) {
                activeRowsBits |= 1 << i;
            }
            writer.Write((ushort)activeRowsBits); // Used palette rows

            for (int i = 0; i < rowsUsed * 16; i++) {
                byte red, green, blue;

                // There may be less colors in the palette than there will be in the file.
                // So some additional #FF00FF colors may be written.
                if (i < colors.Length) {
                    red = (byte)colors[i].Red;
                    green = (byte)colors[i].Green;
                    blue = (byte)colors[i].Blue;
                }
                else {
                    red = (byte)0xFF;
                    green = (byte)0x00;
                    blue = (byte)0xFF;
                }

                writer.Write(red);
                writer.Write(green);
                writer.Write(blue);
            }
        }
    }
}
