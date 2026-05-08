using System.Drawing;

namespace PNG {
    public class Sprite : makesprite.Sprite {
        public Sprite(File file, string name) {
            ColorDepth = file.BitDepth * file.GetSamplesPerPixel();
            TransparentPaletteIndex = file.TransparentPaletteIndex;

            Layer layer = new Layer(this);
            Layers.Add(layer);

            Frame fr = new Frame(this, file.Width, file.Height);
            fr.Duration = 1;
            Frames.Add(fr);

            Sprite.AnimRange range = new Sprite.AnimRange(name, 0, 0);
            AnimRanges.Add(range);

            uint[] pixelData = file.GetPixelData(out ColorDepth);
            fr.PixelDataWidth = file.Width;
            fr.PixelDataHeight = file.Height;
            fr.PixelData.Add(pixelData);

            if (file.PaletteData != null) {
                Palette = file.GetPaletteDataARGB();
            }
        }
    }
}
