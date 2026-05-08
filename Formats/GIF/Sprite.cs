using System.Drawing;

namespace GIF {
    public class Sprite : makesprite.Sprite {
        private int Width;
        private int Height;

        public Sprite(File file, string name, bool ignorePaletteMismatch = false) {
            Width = file.Width;
            Height = file.Height;
            ColorDepth = 8;
            TransparentPaletteIndex = file.TransparentPaletteIndex;

            Color[]? paletteToUse = file.Palette;
            int numPaletteColors = file.NumPaletteColors;
            bool hasPaletteInGCT = numPaletteColors > 0;

            for (var f = 0; f < file.Frames.Count; f++) {
                GIF.Frame gifFrame = file.Frames[f];

                Frame fr = new Frame(this, Width, Height);
                fr.Duration = gifFrame.Delay * 10;

                uint[] pixelData = new uint[Width * Height];
                for (int p = 0; p < Width * Height; p++) {
                    pixelData[p] = gifFrame.Data[p];
                }

                fr.PixelDataWidth = Width;
                fr.PixelDataHeight = Height;
                fr.PixelData.Add(pixelData);

                Frames.Add(fr);

                if (!hasPaletteInGCT && gifFrame.Palette != null) {
                    // In case there wasn't a Global Color Table in this GIF, use this frame's palette.
                    paletteToUse = gifFrame.Palette;
                    numPaletteColors = paletteToUse.Length;
                }
            }

            if (paletteToUse == null) {
                return;
            }

            Layer layer = new Layer(this);
            Layers.Add(layer);

            Sprite.AnimRange range = new Sprite.AnimRange(name, 0, Frames.Count - 1, 0);
            AnimRanges.Add(range);

            if (numPaletteColors == 0) {
                numPaletteColors = 256;
            }

            Palette = new uint[numPaletteColors];

            for (int p = 0; p < numPaletteColors; p++) {
                byte r = paletteToUse[p].R;
                byte g = paletteToUse[p].G;
                byte b = paletteToUse[p].B;
                byte a = paletteToUse[p].A;
                Palette[p] = (uint)(a << 24 | b << 16 | g << 8 | r);
            }

            // Because a spritesheet can only have a single palette, we verify
            // that the palettes of all frames are equal to the one that will
            // be used for the spritesheet.
            // If they aren't, we unpalettize all frames.
            bool makeNonPalettized = false;

            if (!ignorePaletteMismatch && !hasPaletteInGCT) {
                for (var f = 0; f < file.Frames.Count; f++) {
                    GIF.Frame gifFrame = file.Frames[f];
                    if (gifFrame.Palette == null) {
                        continue;
                    }

                    if (gifFrame.Palette.Length != paletteToUse.Length) {
                        makeNonPalettized = true;
                        break;
                    }

                    if (!gifFrame.Palette.SequenceEqual(paletteToUse)) {
                        makeNonPalettized = true;
                        break;
                    }
                }
            }

            if (makeNonPalettized && paletteToUse != null) {
                makesprite.Program.Warning("GIF had unique palettes per frame. Making the sprite non-palettized.");

                MakeNonPalettized(file, paletteToUse);
            }
        }

        public void MakeNonPalettized(File file, Color[] paletteToUse) {
            if (ColorDepth == 32) {
                return;
            }

            ColorDepth = 32;

            for (var f = 0; f < Frames.Count; f++) {
                Frame fr = Frames[f];
                GIF.Frame gifFrame = file.Frames[f];
                Color[] framePalette = paletteToUse;

                if (gifFrame.Palette != null) {
                    framePalette = gifFrame.Palette;
                }

                for (int p = 0; p < Width * Height; p++) {
                    uint colorIndex = fr.PixelData[0][p];

                    byte r = framePalette[colorIndex].R;
                    byte g = framePalette[colorIndex].G;
                    byte b = framePalette[colorIndex].B;
                    byte a = framePalette[colorIndex].A;

                    fr.PixelData[0][p] = (uint)(a << 24 | b << 16 | g << 8 | r);
                }
            }
        }
    }
}
