namespace makesprite {
    public class Sprite {
        public int ColorDepth;
        public int TransparentPaletteIndex = -1;

        public List<Frame> Frames = new List<Frame>();
        public List<Layer> Layers = new List<Layer>();
        public List<AnimRange> AnimRanges = new List<AnimRange>();
        public List<string> HitboxNames = new List<string>();
        public uint[]? Palette = null;

        public enum AnimationDirection {
            Forward,
            Reverse,
            PingPong,
            PingPongReverse
        }

        public enum RotationStyle {
            None,
            Full,
            Degrees45,
            Degrees90,
            Degrees180,
            StaticFrames
        }

        public Sprite Copy() {
            Sprite copy = new Sprite();
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
                    for (int p = 0; p < fr.Width * fr.Height; p++) {
                        fr.PixelData[l][p] = Palette[fr.PixelData[l][p]];
                    }
                }
            }
        }

        // Finds loop frame layers, assigns frames as loop points, and removes the layers
        public void DetectLoopFrameLayers() {
            int loopFrameLayerIndex = -1;

            for (int l = 0; l < Layers.Count; l++) {
                Layer layer = Layers[l];

                if (layer.IsLoopFrameLayer()) {
                    if (loopFrameLayerIndex == -1) {
                        loopFrameLayerIndex = l;

                        Program.LogVerbose("Loop frame layer index: " + l);
                    }
                    else {
                        Program.Warning("More than one loop frame layer! Ignoring.");
                    }
                }
            }

            if (loopFrameLayerIndex == -1) {
                return;
            }

            for (int f = 0; f < Frames.Count; f++) {
                Frame frame = Frames[f];
                if (!frame.IsEmptyOnLayer(loopFrameLayerIndex)) {
                    frame.IsLoopPoint = true;
                }

                frame.PixelData.RemoveAt(loopFrameLayerIndex);
            }

            RemoveLayer(loopFrameLayerIndex);
        }

        // Finds hitbox layers, adds the hitboxes to the frames, and removes the layers
        public void DetectHitboxLayers() {
            List<int> hitboxLayers = new List<int>();

            for (int l = 0; l < Layers.Count; l++) {
                Layer layer = Layers[l];

                if (layer.Name.StartsWith(Layer.HITBOX_LAYER_NAME_PREFIX)) {
                    string name = layer.Name.Substring(Layer.HITBOX_LAYER_NAME_PREFIX.Length).TrimStart();

                    hitboxLayers.Add(l);
                    HitboxNames.Add(name);
                }
            }

            for (int f = 0; f < Frames.Count; f++) {
                Frame frame = Frames[f];

                frame.GetHitboxes(hitboxLayers);

                for (int l = hitboxLayers.Count - 1; l >= 0; l--) {
                    frame.PixelData.RemoveAt(hitboxLayers[l]);
                }
            }

            for (int l = hitboxLayers.Count - 1; l >= 0; l--) {
                RemoveLayer(hitboxLayers[l]);
            }
        }

        private void RemoveLayer(int layerIndex) {
            for (var l = 0; l < Layers.Count; l++) {
                RemoveChildLayers(Layers[l], layerIndex);
            }

            Layers.RemoveAt(layerIndex);
        }

        private void RemoveChildLayers(Layer layer, int layerIndex) {
            for (var c = 0; c < layer.Children.Count; c++) {
                RemoveChildLayers(layer.Children[c], layerIndex);

                if (layer.Children[c] == Layers[layerIndex]) {
                    layer.Children.RemoveAt(c);
                }
            }
        }

        public class AnimRange {
            public string Name;
            public int Start;
            public int End;
            public float Speed = 1.0F;
            public AnimationDirection Direction;
            public RotationStyle RotationStyle = RotationStyle.Full;

            public AnimRange(string name, int start, int end) {
                Name = name;
                Start = start;
                End = end;
            }

            public AnimRange(string name, int start, int end, AnimationDirection direction) {
                Name = name;
                Start = start;
                End = end;
                Direction = direction;
            }

            public AnimRange(string name, int start, int end, float speed, AnimationDirection direction, RotationStyle rotationStyle) {
                Name = name;
                Start = start;
                End = end;
                Speed = speed;
                Direction = direction;
                RotationStyle = rotationStyle;
            }
        }

        public class Layer {
            public const string HITBOX_LAYER_NAME_PREFIX = "Hitbox:";
            public const string LOOP_FRAME_LAYER_NAME = "Loop Frame";

            public string Name;
            public bool Visible;
            public bool IsBackground = false;
            public bool IsGroup = false;
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

            public Layer(Sprite owner, string name) {
                OwningSprite = owner;
                Name = name;
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
                if (IsGroup) {
                    return false;
                }

                return IsVisible();
            }

            public bool IsLoopFrameLayer() {
                if (IsGroup) {
                    return false;
                }

                return Name.StartsWith(LOOP_FRAME_LAYER_NAME);
            }
        }

        public class Frame {
            public int Width;
            public int Height;
            public float Duration;
            public int SheetX;
            public int SheetY;
            public int ID;
            public bool IsLoopPoint = false;
            public Vector2? Offsets = null;
            public List<Hitbox> Hitboxes = new List<Hitbox>();
            public List<uint[]> PixelData = new List<uint[]>();
            public int PixelDataWidth;
            public int PixelDataHeight;
            private Sprite OwningSprite;

            public Frame(Sprite owner, int width, int height) {
                Width = width;
                Height = height;
                OwningSprite = owner;
            }

            public Frame Copy(Sprite owningSprite) {
                Frame copy = new Frame(owningSprite, Width, Height);
                copy.Duration = Duration;
                copy.SheetX = SheetX;
                copy.SheetY = SheetY;
                copy.ID = ID;
                copy.IsLoopPoint = IsLoopPoint;
                copy.Offsets = Offsets;
                copy.PixelDataWidth = PixelDataWidth;
                copy.PixelDataHeight = PixelDataHeight;

                for (int h = 0; h < Hitboxes.Count; h++) {
                    copy.Hitboxes.Add(Hitboxes[h].Copy());
                }

                return copy;
            }

            public void GetHitboxes(List<int> hitboxLayers) {
                for (int l = 0; l < hitboxLayers.Count; l++) {
                    int layerIndex = hitboxLayers[l];

                    Hitbox hitbox = new Hitbox(OwningSprite.HitboxNames[l], Width, Height, 0, 0);

                    for (int y = 0; y < Height; y++) {
                        for (int x = 0; x < Width; x++) {
                            int px = SheetX + x;
                            int py = SheetY + y;
                            if (px < 0 || px >= PixelDataWidth || py < 0 || py >= PixelDataHeight) {
                                continue;
                            }

                            uint value = PixelData[layerIndex][px + (py * PixelDataWidth)];
                            if (OwningSprite.ColorDepth == 8) {
                                if (value == (uint)OwningSprite.TransparentPaletteIndex) {
                                    continue;
                                }
                            }
                            else if (OwningSprite.ColorDepth == 16) {
                                uint alpha = (value & 0xFF00) >> 8;
                                if (alpha == 0) {
                                    continue;
                                }
                            }
                            else {
                                uint alpha = value & 0xFF000000;
                                if (alpha == 0) {
                                    continue;
                                }
                            }

                            hitbox.X = Math.Min(hitbox.X, x);
                            hitbox.Y = Math.Min(hitbox.Y, y);
                            hitbox.Width = Math.Max(hitbox.Width, x + 1);
                            hitbox.Height = Math.Max(hitbox.Height, y + 1);
                        }
                    }

                    Hitboxes.Add(hitbox);
                }
            }

            public bool IsEmptyOnLayer(int layerIndex) {
                uint[] pixelData = PixelData[layerIndex];

                if (OwningSprite.ColorDepth == 8) {
                    if (OwningSprite.Palette == null || OwningSprite.TransparentPaletteIndex == -1) {
                        return true;
                    }

                    for (int y = 0; y < Height; y++) {
                        for (int x = 0; x < Width; x++) {
                            int px = SheetX + x;
                            int py = SheetY + y;
                            if (px < 0 || px >= PixelDataWidth || py < 0 || py >= PixelDataHeight) {
                                continue;
                            }

                            uint index = pixelData[px + (py * PixelDataWidth)];
                            if (index == (uint)OwningSprite.TransparentPaletteIndex) {
                                continue;
                            }

                            uint argb = OwningSprite.Palette[index];
                            uint alpha = argb & 0xFF000000;
                            if (alpha != 0) {
                                return false;
                            }
                        }
                    }
                }
                else if (OwningSprite.ColorDepth == 16) {
                    for (int y = 0; y < Height; y++) {
                        for (int x = 0; x < Width; x++) {
                            int px = SheetX + x;
                            int py = SheetY + y;
                            if (px < 0 || px >= PixelDataWidth || py < 0 || py >= PixelDataHeight) {
                                continue;
                            }

                            uint value = pixelData[px + (py * PixelDataWidth)];
                            uint alpha = value & 0xFF00;
                            if (alpha != 0) {
                                return false;
                            }
                        }
                    }
                }
                else {
                    for (int y = 0; y < Height; y++) {
                        for (int x = 0; x < Width; x++) {
                            int px = SheetX + x;
                            int py = SheetY + y;
                            if (px < 0 || px >= PixelDataWidth || py < 0 || py >= PixelDataHeight) {
                                continue;
                            }

                            uint argb = pixelData[px + (py * PixelDataWidth)];
                            uint alpha = argb & 0xFF000000;
                            if (alpha != 0) {
                                return false;
                            }
                        }
                    }
                }

                return true;
            }
        }

        public class Hitbox {
            public string Name;
            public int X;
            public int Y;
            public int Width;
            public int Height;

            public Hitbox(string name, int x, int y, int width, int height) {
                Name = name;
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }

            public Hitbox Copy() {
                return new Hitbox(Name, X, Y, Width, Height);
            }
        }
    }
}
