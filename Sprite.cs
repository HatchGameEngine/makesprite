using System.IO;
using System.IO.Compression;

namespace makesprite {
    public class Sprite {
        public int Width;
        public int Height;
        public int ColorDepth;
        public int TransparentPaletteIndex = -1;

        public List<Frame> Frames = new List<Frame>();
        public List<Layer> Layers = new List<Layer>();
        public List<AnimRange> AnimRanges = new List<AnimRange>();
        public uint[]? Palette = null;

        public Sprite Copy() {
            Sprite copy = new Sprite();
            copy.Width = Width;
            copy.Height = Height;
            copy.ColorDepth = ColorDepth;
            copy.AnimRanges = AnimRanges;
            copy.Palette = Palette;
            copy.TransparentPaletteIndex = TransparentPaletteIndex;
            return copy;
        }

        public void MakeNonPalettized() {
            if (ColorDepth != 8 || Palette == null) {
                return;
            }

            ColorDepth = 32;

            for (var f = 0; f < Frames.Count; f++) {
                Frame fr = Frames[f];

                for (var l = 0; l < Layers.Count; l++) {
                    for (int p = 0; p < Width * Height; p++) {
                        fr.PixelData[l][p] = Palette[fr.PixelData[l][p]];
                    }
                }
            }
        }

        public class AnimRange {
            public string Name;
            public int Start;
            public int End;
            public int Type;

            public AnimRange(string name, int start, int end, int type) {
                Name = name;
                Start = start;
                End = end;
                Type = type;
            }
        }

        public class Layer {
            public string Name;
            public bool Visible;
            public bool IsBackground = false;
            public bool IsGroup = false;
            public bool IsHitbox = false;
            public int BlendMode;
            private Sprite OwningSprite;
            public Layer? Parent = null;
            public List<Layer> Children = new List<Layer>();

            public Layer(Sprite owner) {
                OwningSprite = owner;
                Name = "Layer";
                Visible = true;
                BlendMode = 0;
            }

            public Layer(Sprite owner, string name, bool visible, int blendMode) {
                OwningSprite = owner;
                Name = name;
                Visible = visible;
                BlendMode = blendMode;
            }

            public void AddChild(Layer otherLayer) {
                otherLayer.Parent = this;
                Children.Add(otherLayer);
            }

            public bool IsVisible() {
                if (!Visible || IsGroup) {
                    return false;
                }

                if (IsBackground) {
                    // Background layers are usually ignored, but if the only
                    // layer is a background layer, it's considered to be visible.
                    if (OwningSprite != null && OwningSprite.Layers.Count > 1) {
                        return false;
                    }
                }

                return true;
            }

            public bool CanDraw() {
                if (IsHitbox) {
                    return true;
                }
                else if (IsGroup) {
                    return false;
                }

                return IsVisible();
            }
        }

        public class Frame {
            public int Duration;
            public List<uint[]> PixelData = new List<uint[]>();
            private Sprite OwningSprite;

            public Frame(Sprite owner) {
                OwningSprite = owner;
            }

            public Frame Copy() {
                Frame copy = new Frame(OwningSprite);
                copy.Duration = Duration;
                return copy;
            }

            public bool IsEmptyOnLayer(int layerIndex) {
                int canvasSize = OwningSprite.Width * OwningSprite.Height;
                uint[] pixelData = PixelData[layerIndex];

                if (OwningSprite.ColorDepth == 8) {
                    if (OwningSprite.Palette == null || OwningSprite.TransparentPaletteIndex == -1) {
                        return true;
                    }

                    for (int p = 0; p < canvasSize; p++) {
                        uint index = pixelData[p];
                        if (index == (uint)OwningSprite.TransparentPaletteIndex) {
                            continue;
                        }

                        uint argb = OwningSprite.Palette[index];
                        uint alpha = (argb & 0xFF000000);
                        if (alpha != 0) {
                            return false;
                        }
                    }
                }
                else if (OwningSprite.ColorDepth == 16) {
                    for (int p = 0; p < canvasSize; p++) {
                        uint value = pixelData[p];
                        uint alpha = (value & 0xFF00);
                        if (alpha != 0) {
                            return false;
                        }
                    }
                }
                else {
                    for (int p = 0; p < canvasSize; p++) {
                        uint argb = pixelData[p];
                        uint alpha = (argb & 0xFF000000);
                        if (alpha != 0) {
                            return false;
                        }
                    }
                }

                return true;
            }
        }
    }
}
