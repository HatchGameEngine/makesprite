using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

using Hatch;
using RSDKv5;

namespace makesprite {
    public class Converter {
        private const string LOOP_FRAME_LAYER_NAME = "Loop Frame";
        private const string HITBOX_LAYER_NAME_PREFIX = "Hitbox:";
        private const string FONT_RANGE_NAME_PREFIX = "Font:";

        public enum SplitMode {
            None,
            Files,
            Groups
        }

        public static bool IsLoopFrameLayer(Sprite.Layer layer) {
            if (layer.IsGroup) {
                return false;
            }

            return layer.Name.StartsWith(LOOP_FRAME_LAYER_NAME);
        }

        public class ConversionInfo {
            public Sprite Input;
            public string Filename;

            public int LoopFrameLayerIndex = -1;
            public List<int> HitboxLayers = new List<int>();
            public List<string> HitboxNames = new List<string>();

            public List<int> frameMap = new List<int>();
            public List<int> frameDuration = new List<int>();
            public bool[]? frameIsLoopPoint = null;
            public List<Rectangle> frameCrops = new List<Rectangle>();
            public List<Vector2> frameSizes = new List<Vector2>();
            public List<Vector2> frameOffsets = new List<Vector2>();
            public List<Rectangle[]?> frameHitboxes = new List<Rectangle[]?>();
            public List<System.Drawing.Color[]> frameSheets = new List<System.Drawing.Color[]>();

            public int HitboxStartIndex = 0;

            public ConversionInfo(Sprite input, string filename) {
                Input = input;
                Filename = filename;

                // Find loop frame and hitbox layers
                for (int l = 0; l < input.Layers.Count; l++) {
                    Sprite.Layer layer = input.Layers[l];

                    if (IsLoopFrameLayer(layer)) {
                        if (LoopFrameLayerIndex == -1) {
                            LoopFrameLayerIndex = l;
                        }
                        else {
                            Program.Warning("More than one loop frame layer! Ignoring.");
                        }

                        layer.Visible = false;
                    }
                    else if (layer.Name.StartsWith(HITBOX_LAYER_NAME_PREFIX)) {
                        string name = layer.Name.Substring(HITBOX_LAYER_NAME_PREFIX.Length).TrimStart();
                        HitboxLayers.Add(l);
                        HitboxNames.Add(name);
                        layer.IsHitbox = true;
                    }
                }
            }
        }

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
        }

        public class Options {
            public bool SaveSprites = true;
            public bool SaveSheets = true;
            public bool SavePalettes = false;
            public SplitMode SplitBy = SplitMode.None;
            public int MaxSheetWidth = SpritePacker.MAX_SHEET_WIDTH;
            public int MaxSheetHeight = SpritePacker.MAX_SHEET_HEIGHT;
            public SpritePacker.SortMode SortBy = SpritePacker.SortMode.AreaAndHeight;
            public string? SheetPath = null;
            public bool NoOffsets = false;
            public bool KeepCanvasOffsets = false;
            public int OffsetX = 0;
            public int OffsetY = 0;
            public bool TrimFrames = true;
            public bool MergeDuplicateFrames = true;
            public bool IsFont = false;
            public bool Verbose = false;
        }

        public Options CurrentOptions = new Converter.Options();

        private List<SpritePacker.Box> Boxes = new List<SpritePacker.Box>();
        private Dictionary<uint, int> HashToFrameIndex = new Dictionary<uint, int>();

        private bool All8Bpp = true;

        private string OutFilenamePath = "";
        private System.IO.DirectoryInfo? ParentPath = null;
        private string? SheetRelFilename;
        private bool IsInRootPath = false;

        private bool WarnedAboutSheetPaths = false;

        public bool Convert(List<Sprite> sprites, List<string> filenames, string outputFilename) {
            if (sprites.Count == 0) {
                Console.WriteLine("No sprites to convert");
                return false;
            }

            bool singleAnim = sprites.Count == 1;

            List<ConversionInfo> conversionInfos = new List<ConversionInfo>();

            for (int i = 0; i < sprites.Count; i++) {
                Sprite? sprite = sprites[i];

                Program.LogVerbose("Sprite " + i + ": " + filenames[i]);
                Program.LogVerbose("  Size: " + sprite.Width + "x" + sprite.Height);
                Program.LogVerbose("  Color depth: " + sprite.ColorDepth + " bits per pixel");

                // Check ColorDepth
                if (sprite.ColorDepth != 8) {
                    All8Bpp = false;
                }

                conversionInfos.Add(new ConversionInfo(sprite, filenames[i]));
            }

            Program.LogVerbose("Palettized: " + (All8Bpp ? "Yes" : "No"));

            ParentPath = Directory.GetParent(outputFilename);

            if (OutFilenamePath == "") {
                OutFilenamePath = outputFilename;
            }

            string outputSpriteFilename;
            if (singleAnim || CurrentOptions.SplitBy == SplitMode.None) {
                outputSpriteFilename = OutFilenamePath;
            }
            else {
                outputSpriteFilename = conversionInfos[0].Filename;
            }

            if (CurrentOptions.SheetPath != null) {
                SheetRelFilename = CurrentOptions.SheetPath;
            }
            else if (ParentPath != null) {
                string parent = "";
                if (PathHelper.GetSpritesFolder(OutFilenamePath, out parent)) {
                    string relFilename = ParentPath.FullName;
                    if (relFilename.Length > parent.Length) {
                        SheetRelFilename = relFilename.Substring(parent.Length + 1);
                    }
                    else {
                        IsInRootPath = PathHelper.EndsInRootPath(parent);
                    }
                }
            }

            if (!IsInRootPath && SheetRelFilename == null && !WarnedAboutSheetPaths) {
                Program.Warning("Spritesheet paths will be relative to \"./\"; use --sheet-path to override.");
                WarnedAboutSheetPaths = true;
            }

            Boxes = new List<SpritePacker.Box>();
            HashToFrameIndex = new Dictionary<uint, int>();

            // Create spritesheets
            for (int i = 0; i < conversionInfos.Count; i++) {
                AddFrames(conversionInfos[i]);
            }

            // Package all unique frames
            Program.LogVerbose("Packing all unique frames...");

            SpritePacker spritePacker = new SpritePacker(
                8, 8,
                CurrentOptions.MaxSheetWidth, CurrentOptions.MaxSheetHeight,
                true,
                CurrentOptions.SortBy
            );

            List<SpritePacker.Package>? packages = spritePacker.PackBoxes(ref Boxes);
            if (packages == null) {
                Console.WriteLine("Could not pack all unique frames");
                return false;
            }

            Program.LogVerbose("Number of unique frames: " + HashToFrameIndex.Count);
            Program.LogVerbose("Number of unique boxes: " + Boxes.Count);

            // Create spritesheets from packages
            List<SpritesheetImage> spritesheets = CreateSpritesheets(conversionInfos, packages);
            List<string> spritesheetNames = new List<string>();

            if (spritesheets.Count > 0) {
                GetSpritesheetNames(outputFilename, spritesheets.Count, ".png", spritesheetNames);

                // Save spritesheets
                if (CurrentOptions.SaveSheets) {
                    if (!SaveSpritesheets(spritesheets, spritesheetNames)) {
                        Console.WriteLine("Could not save spritesheets");
                        return false;
                    }
                }

                // Save palettes
                if (CurrentOptions.SavePalettes) {
                    string filename = Path.GetFileNameWithoutExtension(outputFilename);
                    if (!singleAnim && ParentPath != null) {
                        filename = Path.Combine(ParentPath.FullName, filename);
                    }
                    filename += ".hpal";

                    Color[]? palette = spritesheets[0].Palette;
                    if (palette != null) {
                        SavePalette(palette, filename);
                    }
                    else {
                        Program.Warning("No palette in spritesheet. Did not export to " + filename);
                    }
                }
            }

            // Create sprite files or file
            if (CurrentOptions.SaveSprites) {
                GenerateSprites(conversionInfos, spritesheetNames, outputSpriteFilename);
            }

            return true;
        }

        private string GetExportedFilename(string filename, string fileExtension, bool singleAnim) {
            if (!singleAnim) {
                filename = Path.GetFileNameWithoutExtension(filename) + fileExtension;

                if (ParentPath != null) {
                    filename = Path.Combine(ParentPath.FullName, filename);
                }
            }

            return filename;
        }

        private void AddFrames(ConversionInfo convert) {
            Sprite sprite = convert.Input;
            if (sprite.Frames.Count == 0) {
                return;
            }

            int centerX = sprite.Width / 2;
            int centerY = sprite.Height / 2;

            convert.frameIsLoopPoint = new bool[sprite.Frames.Count];

            Program.LogVerbose("Number of frames in " + convert.Filename + ": " + sprite.Frames.Count);

            // Obtain all unique frame spritesheets
            for (int f = 0; f < sprite.Frames.Count; f++) {
                int canvasSize = sprite.Width * sprite.Height;

                System.Drawing.Color[] frameCanvas = new System.Drawing.Color[canvasSize];
                for (int p = 0; p < canvasSize; p++) {
                    frameCanvas[p] = System.Drawing.Color.Transparent;
                }

                // Get loop index if possible
                if (convert.LoopFrameLayerIndex != -1) {
                    bool isEmpty = sprite.Frames[f].IsEmptyOnLayer(convert.LoopFrameLayerIndex);
                    convert.frameIsLoopPoint[f] = !isEmpty;
                }

                // Prepare hitboxes
                if (convert.HitboxLayers.Count > 0) {
                    convert.frameHitboxes.Add(new Rectangle[convert.HitboxLayers.Count]);
                }
                else {
                    convert.frameHitboxes.Add(null);
                }

                // Write pixels onto frame's canvas
                Rectangle crop = new Rectangle(sprite.Width, sprite.Height, 0, 0);
                if (sprite.ColorDepth == 8) {
                    BuildIndexedFrame(convert, sprite.Frames[f], frameCanvas, crop);
                }
                else if (sprite.ColorDepth == 16) {
                    BuildGrayscaleFrame(convert, sprite.Frames[f], frameCanvas, crop);
                }
                else {
                    BuildFrame(convert, sprite.Frames[f], frameCanvas, crop);
                }

                // Adjust Hitboxes
                for (int h = 0; h < convert.HitboxLayers.Count; h++) {
                    Rectangle[]? frameHitboxes = convert.frameHitboxes.Last();
                    if (frameHitboxes == null) {
                        continue;
                    }

                    Rectangle? hitbox = frameHitboxes[h];
                    bool hasHitbox = hitbox != null;
                    if (hitbox == null) {
                        hitbox = new Rectangle();
                    }
                    if (hasHitbox) {
                        if (!CurrentOptions.KeepCanvasOffsets) {
                            hitbox.X -= centerX;
                            hitbox.Y -= centerY;
                            hitbox.Width -= centerX;
                            hitbox.Height -= centerY;
                        }
                        hitbox.Width += 1;
                        hitbox.Height += 1;
                    }
                    frameHitboxes[h] = hitbox;
                }

                // Hash canvas to check for uniqueness
                uint frameHash = GetFrameHash(sprite, frameCanvas, crop, 0xDEADBEEF);

                // Adjust Box from X1Y1X2Y2 to XYWH
                crop.Width -= crop.X - 1;
                crop.Height -= crop.Y - 1;
                crop.Width = Math.Max(crop.Width, 0);
                crop.Height = Math.Max(crop.Height, 0);

                // Add frame properties
                convert.frameCrops.Add(crop);
                convert.frameSheets.Add(frameCanvas);
                convert.frameSizes.Add(new Vector2(sprite.Width, sprite.Height));
                if (sprite.Frames[f].Duration == 1) {
                    convert.frameDuration.Add(0);
                }
                else {
                    convert.frameDuration.Add((sprite.Frames[f].Duration * 60 + 999) / 1000); // ceil
                }

                if (HashToFrameIndex.ContainsKey(frameHash)) {
                    convert.frameMap.Add(HashToFrameIndex[frameHash]);
                }
                else {
                    // Never duplicate empty frames
                    // (The hash stays as 0xDEADBEEF if the canvas was empty.)
                    if (CurrentOptions.MergeDuplicateFrames || frameHash == 0xDEADBEEF) {
                        HashToFrameIndex.Add(frameHash, Boxes.Count);
                    }

                    convert.frameMap.Add(Boxes.Count);

                    Rectangle rect;
                    if (CurrentOptions.TrimFrames) {
                        rect = new Rectangle(0, 0, crop.Width, crop.Height);
                    }
                    else {
                        rect = new Rectangle(0, 0, sprite.Width, sprite.Height);
                    }

                    SpritePacker.Box box = new SpritePacker.Box(Boxes.Count, rect);
                    Boxes.Add(box);
                }

                int frameOffsetX = 0;
                int frameOffsetY = 0;

                if (!CurrentOptions.NoOffsets) {
                    frameOffsetX = crop.X;
                    frameOffsetY = crop.Y;

                    if (!CurrentOptions.KeepCanvasOffsets) {
                        frameOffsetX -= centerX;
                        frameOffsetY -= centerY;
                    }
                }

                frameOffsetX += CurrentOptions.OffsetX;
                frameOffsetY += CurrentOptions.OffsetY;

                convert.frameOffsets.Add(new Vector2(frameOffsetX, frameOffsetY));
            }
        }

        private uint GetFrameHash(Sprite sprite, System.Drawing.Color[] canvas, Rectangle crop, uint frameHash) {
            if (crop.Width == 0 || crop.Height == 0) {
                return frameHash;
            }

            int canvasSize = sprite.Width * sprite.Height;

            for (int p = 0; p < canvasSize; p++) {
                int px = p % sprite.Width;
                int py = p / sprite.Width;
                // Width and Height in this respect are just X2 and Y2
                if (px >= crop.X && py >= crop.Y &&
                    px <= crop.Width && py <= crop.Height) {
                    frameHash = JenkinsHash((uint)canvas[p].ToArgb(), frameHash);
                }
            }

            return frameHash;
        }

        private void AddHitbox(Rectangle[] frameHitboxes, int hitboxIndex, int px, int py, int width, int height) {
            if (frameHitboxes[hitboxIndex] == null) {
                frameHitboxes[hitboxIndex] = new Rectangle(width, height, 0, 0);
            }

            Rectangle hitbox = frameHitboxes[hitboxIndex];
            hitbox.X = Math.Min(hitbox.X, px);
            hitbox.Y = Math.Min(hitbox.Y, py);
            hitbox.Width = Math.Max(hitbox.Width, px);
            hitbox.Height = Math.Max(hitbox.Height, py);
        }

        private void BuildFrame(ConversionInfo convert, Sprite.Frame frame, System.Drawing.Color[] canvas, Rectangle crop) {
            Sprite? sprite = convert.Input;
            int canvasSize = sprite.Width * sprite.Height;

            Rectangle[]? frameHitboxes = convert.frameHitboxes.Last();

            for (int l = 0; l < frame.PixelData.Count; l++) {
                Sprite.Layer layer = sprite.Layers[l];
                if (!layer.CanDraw()) {
                    continue;
                }

                if (layer.IsHitbox) {
                    if (frameHitboxes == null) {
                        continue;
                    }

                    int hitboxIndex = convert.HitboxLayers.IndexOf(l);

                    for (int p = 0; p < canvasSize; p++) {
                        uint argb = frame.PixelData[l][p];
                        uint color = (argb & 0xFF000000);
                        if (color == 0) {
                            continue;
                        }

                        int px = p % sprite.Width;
                        int py = p / sprite.Width;
                        AddHitbox(frameHitboxes, hitboxIndex, px, py, sprite.Width, sprite.Height);
                    }
                }
                else {
                    for (int p = 0; p < canvasSize; p++) {
                        uint argb = frame.PixelData[l][p];
                        uint color = (argb & 0xFF000000);
                        if (color == 0) {
                            continue;
                        }

                        color |= (argb & 0xFF0000) >> 16;
                        color |= (argb & 0xFF00);
                        color |= (argb & 0xFF) << 16;

                        canvas[p] = System.Drawing.Color.FromArgb((int)color);

                        int px = p % sprite.Width;
                        int py = p / sprite.Width;
                        crop.X = Math.Min(crop.X, px);
                        crop.Y = Math.Min(crop.Y, py);
                        crop.Width = Math.Max(crop.Width, px);
                        crop.Height = Math.Max(crop.Height, py);
                    }
                }
            }
        }

        private void BuildGrayscaleFrame(ConversionInfo convert, Sprite.Frame frame, System.Drawing.Color[] canvas, Rectangle crop) {
            Sprite? sprite = convert.Input;
            int canvasSize = sprite.Width * sprite.Height;

            Rectangle[]? frameHitboxes = convert.frameHitboxes.Last();

            for (int l = 0; l < frame.PixelData.Count; l++) {
                Sprite.Layer layer = sprite.Layers[l];
                if (!layer.CanDraw()) {
                    continue;
                }

                if (layer.IsHitbox) {
                    if (frameHitboxes == null) {
                        continue;
                    }

                    int hitboxIndex = convert.HitboxLayers.IndexOf(l);

                    for (int p = 0; p < canvasSize; p++) {
                        uint value = frame.PixelData[l][p];
                        uint alpha = (value & 0xFF00) >> 8;
                        if (alpha == 0) {
                            continue;
                        }

                        int px = p % sprite.Width;
                        int py = p / sprite.Width;
                        AddHitbox(frameHitboxes, hitboxIndex, px, py, sprite.Width, sprite.Height);
                    }
                }
                else {
                    for (int p = 0; p < canvasSize; p++) {
                        uint value = frame.PixelData[l][p];
                        uint alpha = (value & 0xFF00) >> 8;
                        if (alpha == 0) {
                            continue;
                        }

                        value &= 0xFF;

                        canvas[p] = System.Drawing.Color.FromArgb((int)alpha, (int)value, (int)value, (int)value);

                        int px = p % sprite.Width;
                        int py = p / sprite.Width;
                        crop.X = Math.Min(crop.X, px);
                        crop.Y = Math.Min(crop.Y, py);
                        crop.Width = Math.Max(crop.Width, px);
                        crop.Height = Math.Max(crop.Height, py);
                    }
                }
            }
        }

        private void BuildIndexedFrame(ConversionInfo convert, Sprite.Frame frame, System.Drawing.Color[] canvas, Rectangle crop) {
            Sprite? sprite = convert.Input;
            int canvasSize = sprite.Width * sprite.Height;

            Rectangle[]? frameHitboxes = convert.frameHitboxes.Last();

            for (int l = 0; l < frame.PixelData.Count; l++) {
                Sprite.Layer layer = sprite.Layers[l];
                if (!layer.CanDraw()) {
                    continue;
                }

                bool isHitboxLayer = layer.IsHitbox;

                if (layer.IsHitbox) {
                    if (frameHitboxes == null) {
                        continue;
                    }

                    int hitboxIndex = convert.HitboxLayers.IndexOf(l);

                    for (int p = 0; p < canvasSize; p++) {
                        uint index = frame.PixelData[l][p];
                        if (index == sprite.TransparentPaletteIndex) {
                            continue;
                        }

                        int px = p % sprite.Width;
                        int py = p / sprite.Width;
                        AddHitbox(frameHitboxes, hitboxIndex, px, py, sprite.Width, sprite.Height);
                    }
                }
                else {
                    for (int p = 0; p < canvasSize; p++) {
                        uint index = frame.PixelData[l][p];
                        if (index == sprite.TransparentPaletteIndex) {
                            continue;
                        }

                        canvas[p] = System.Drawing.Color.FromArgb((int)index, 0, 0);

                        int px = p % sprite.Width;
                        int py = p / sprite.Width;
                        crop.X = Math.Min(crop.X, px);
                        crop.Y = Math.Min(crop.Y, py);
                        crop.Width = Math.Max(crop.Width, px);
                        crop.Height = Math.Max(crop.Height, py);
                    }
                }
            }
        }

        private Color[]? BuildPalette(uint[] asePalette) {
            Color[] palette = new Color[asePalette.Length];

            for (int i = 0; i < palette.Length; i++) {
                uint argb = asePalette[i];
                int alpha = (int)((argb & 0xFF000000) >> 24);
                int blue = (int)((argb & 0xFF0000) >> 16);
                int green = (int)((argb & 0xFF00) >> 8);
                int red = (int)(argb & 0xFF);
                palette[i] = System.Drawing.Color.FromArgb(alpha, red, green, blue);
            }

            return palette;
        }

        private List<SpritesheetImage> CreateSpritesheets(List<ConversionInfo> conversionInfos, List<SpritePacker.Package> packages) {
            List<SpritesheetImage> spritesheets = new List<SpritesheetImage>();

            Program.LogVerbose("Creating spritesheets...");

            foreach (SpritePacker.Package package in packages) {
                string text = "  Spritesheet image: " + package.Width + "x" + package.Height;

                Color[]? palette = null;
                Sprite firstSprite = conversionInfos[0].Input;
                if (All8Bpp && firstSprite.Palette != null) {
                    uint[] asePalette = firstSprite.Palette;

                    text += ", " + asePalette.Length + " colors";

                    palette = BuildPalette(asePalette);
                }

                Program.LogVerbose(text);

                SpritesheetImage sheet = new SpritesheetImage(package.Width, package.Height, All8Bpp ? 8 : 32, palette);

                spritesheets.Add(sheet);
            }

            if (spritesheets.Count == 0) {
                return spritesheets;
            }

            // Add unique frames to spritesheets
            Dictionary<int, int> addedBoxID = new Dictionary<int, int>();
            for (int a = 0; a < conversionInfos.Count; a++) {
                ConversionInfo convert = conversionInfos[a];
                Sprite sprite = convert.Input;

                int canvasSize = sprite.Width * sprite.Height;
                int bytesPerPixel = All8Bpp ? 1 : 4;

                for (int i = 0; i < convert.frameMap.Count; i++) {
                    int uniqueFrameIndex = convert.frameMap[i];
                    SpritePacker.Box boxx = Boxes[uniqueFrameIndex];
                    Rectangle crop = convert.frameCrops[i];

                    if (addedBoxID.ContainsKey(uniqueFrameIndex)) {
                        continue;
                    }

                    SpritesheetImage sheet = spritesheets[boxx.PackageID];
                    byte[] bytes = sheet.Data;
                    int stride = sheet.Width * bytesPerPixel;

                    if (All8Bpp) {
                        for (int p = 0; p < canvasSize; p++) {
                            System.Drawing.Color c = convert.frameSheets[i][p];
                            if (c.A == 0) {
                                continue;
                            }

                            int px = p % sprite.Width;
                            int py = p / sprite.Width;

                            px -= crop.X;
                            py -= crop.Y;

                            px += boxx.Rect.X;
                            py += boxx.Rect.Y;

                            bytes[(py * stride) + px] = c.R;
                        }
                    }
                    else if (sprite.ColorDepth == 8 && sprite.Palette != null) {
                        for (int p = 0; p < canvasSize; p++) {
                            System.Drawing.Color c = convert.frameSheets[i][p];
                            if (c.A == 0) {
                                continue;
                            }

                            int px = p % sprite.Width;
                            int py = p / sprite.Width;

                            px -= crop.X;
                            py -= crop.Y;

                            px += boxx.Rect.X;
                            py += boxx.Rect.Y;

                            uint argb = sprite.Palette[c.R];
                            long index = (py * stride) + (px * bytesPerPixel);
                            bytes[index + 0] = (byte)(argb & 0xFF);
                            bytes[index + 1] = (byte)((argb & 0xFF00) >> 8);
                            bytes[index + 2] = (byte)((argb & 0xFF0000) >> 16);
                            bytes[index + 3] = (byte)((argb & 0xFF000000) >> 24);
                        }
                    }
                    else {
                        for (int p = 0; p < canvasSize; p++) {
                            System.Drawing.Color c = convert.frameSheets[i][p];
                            if (c.A == 0) {
                                continue;
                            }

                            int px = p % sprite.Width;
                            int py = p / sprite.Width;

                            px -= crop.X;
                            py -= crop.Y;

                            px += boxx.Rect.X;
                            py += boxx.Rect.Y;

                            long index = (py * stride) + (px * bytesPerPixel);
                            bytes[index + 0] = c.R;
                            bytes[index + 1] = c.G;
                            bytes[index + 2] = c.B;
                            bytes[index + 3] = c.A;
                        }
                    }

                    addedBoxID.Add(uniqueFrameIndex, 1);
                }
            }

            return spritesheets;
        }

        private void GetSpritesheetNames(string outputFilename, int numSpritesheets, string fileExtension, List<string> names) {
            string filename = Path.GetFileNameWithoutExtension(outputFilename);

            for (int i = 0; i < numSpritesheets; i++) {
                string suffix;
                if (numSpritesheets != 1) {
                    suffix = (i + fileExtension);
                }
                else {
                    suffix = fileExtension;
                }

                string sheetFilename = filename.Replace('\\', '/') + suffix;
                names.Add(sheetFilename);
            }
        }

        private bool SaveSpritesheets(List<SpritesheetImage> spritesheets, List<string> spritesheetNames) {
            for (int i = 0; i < spritesheets.Count; i++) {
                string outSheetFilename = spritesheetNames[i];
                if (ParentPath != null) {
                    outSheetFilename = Path.Combine(ParentPath.FullName, outSheetFilename);
                }

                Program.LogVerbose("Saving spritesheet " + outSheetFilename);

                // If the path doesn't exist, create it.
                string? directoryPath = Path.GetDirectoryName(outSheetFilename);
                if (directoryPath != null && directoryPath != "" && !Directory.Exists(directoryPath)) {
                    Directory.CreateDirectory(directoryPath);
                }

                SpritesheetImage sheet = spritesheets[i];
                PNG.File png = new PNG.File(sheet.Width, sheet.Height, sheet.ColorDepth, sheet.Data, sheet.Palette);
                using (FileStream fs = new FileStream(outSheetFilename, FileMode.Create)) {
                    using (BinaryWriter writer = new BinaryWriter(fs)) {
                        png.Write(writer);
                    }
                }
            }

            return true;
        }

        private void SavePalette(Color[] palette, string filename) {
            Program.LogVerbose("Saving palette " + filename);

            // If the path doesn't exist, create it.
            string? directoryPath = Path.GetDirectoryName(filename);
            if (directoryPath != null && directoryPath != "" && !Directory.Exists(directoryPath)) {
                Directory.CreateDirectory(directoryPath);
            }

            // Save palette file
            using (FileStream fs = new FileStream(filename, FileMode.Create)) {
                using (BinaryWriter writer = new BinaryWriter(fs)) {
                    Hatch.Palette.Write(palette, writer);
                }
            }
        }

        private void GenerateSprites(List<ConversionInfo> conversionInfos, List<string> spritesheetNames, string path) {
            List<RSDKv5.Sprite> outputSprites = new List<RSDKv5.Sprite>();
            List<string> outputFilenames = new List<string>();
            string fileExtension = Program.SPRITE_EXTENSION;

            RSDKv5.Sprite currentSprite = new RSDKv5.Sprite();

            if (CurrentOptions.SplitBy == SplitMode.None) {
                outputSprites.Add(currentSprite);
            }

            bool singleAnim = conversionInfos.Count == 1;

            // Prepare the sprites
            for (int a = 0; a < conversionInfos.Count; a++) {
                ConversionInfo info = conversionInfos[a];
                Sprite sourceSprite = info.Input;

                string filename;
                if (CurrentOptions.SplitBy == SplitMode.None || CurrentOptions.SplitBy != SplitMode.Groups && singleAnim) {
                    filename = GetExportedFilename(path, fileExtension, singleAnim);
                }
                else {
                    filename = GetExportedFilename(info.Filename, fileExtension, singleAnim);
                }

                if (sourceSprite.Frames.Count == 0) {
                    Console.WriteLine("No frames for sprite " + filename);
                    continue;
                }

                // No ranges? Create one.
                if (sourceSprite.AnimRanges.Count == 0) {
                    Program.LogVerbose("Sprite " + filename + " has no ranges. Creating one.");

                    Sprite.AnimRange range = new Sprite.AnimRange("Animation", 0, sourceSprite.Frames.Count - 1, 0);
                    sourceSprite.AnimRanges.Add(range);
                }

                outputFilenames.Add(filename);

                PrepareRSDKSprite(currentSprite, info, spritesheetNames);

                if (CurrentOptions.SplitBy != SplitMode.None) {
                    outputSprites.Add(currentSprite);

                    if (a + 1 < conversionInfos.Count) {
                        currentSprite = new RSDKv5.Sprite();
                    }
                }
            }

            // Add animation and frame data
            for (int a = 0; a < conversionInfos.Count; a++) {
                ConversionInfo info = conversionInfos[a];
                if (info.Input.Frames.Count == 0) {
                    continue;
                }

                if (CurrentOptions.SplitBy == SplitMode.None) {
                    currentSprite = outputSprites[0];
                }
                else {
                    currentSprite = outputSprites[a];
                }

                AddAnimationsToRSDKSprite(currentSprite, info);
            }

            // Save the sprites
            for (int i = 0; i < outputSprites.Count; i++) {
                SaveRSDKSpriteFile(outputSprites[i], outputFilenames[i]);
            }
        }

        private void PrepareRSDKSprite(RSDKv5.Sprite outSprite,
            ConversionInfo convert,
            List<string> spritesheetNames) {
            // Add hitboxes
            convert.HitboxStartIndex = outSprite.HitboxNames.Count;

            for (int l = 0; l < convert.HitboxLayers.Count; l++) {
                outSprite.HitboxNames.Add(convert.HitboxNames[l]);
            }

            // Add spritesheets
            foreach (string sheet in spritesheetNames) {
                if (SheetRelFilename != null) {
                    outSprite.SpritesheetNames.Add(Path.Combine(SheetRelFilename, sheet));
                }
                else if (IsInRootPath) {
                    outSprite.SpritesheetNames.Add(sheet);
                }
                else {
                    outSprite.SpritesheetNames.Add("./" + sheet);
                }
            }
        }

        private void AddAnimationsToRSDKSprite(RSDKv5.Sprite outSprite, ConversionInfo convert) {
            Sprite sprite = convert.Input;

            int tallestFrame = -65535;

            // Add animation entries
            for (int i = 0; i < sprite.AnimRanges.Count; i++) {
                Sprite.AnimRange range = sprite.AnimRanges[i];
                string rangeName = range.Name;

                bool isFont = CurrentOptions.IsFont;
                if (rangeName.StartsWith(FONT_RANGE_NAME_PREFIX)) {
                    rangeName = rangeName.Substring(FONT_RANGE_NAME_PREFIX.Length).TrimStart();
                    isFont = true;
                }

                RSDKv5.Sprite.Animation animEntry = new RSDKv5.Sprite.Animation(rangeName);
                if (isFont) {
                    animEntry.Speed = 0;
                }

                for (int f = range.Start; f <= range.End; f++) {
                    int uniqueFrameIndex = convert.frameMap[f];
                    SpritePacker.Box boxx = Boxes[uniqueFrameIndex];
                    Rectangle crop = convert.frameCrops[f];

                    if (isFont && boxx.Rect.Height > tallestFrame) {
                        tallestFrame = boxx.Rect.Height;
                    }

                    int offsetX = convert.frameOffsets[f].X;
                    int offsetY = convert.frameOffsets[f].Y;
                    if (isFont) {
                        if (!CurrentOptions.KeepCanvasOffsets && boxx.Rect.Width > 1 && boxx.Rect.Height > 1) {
                            offsetX += convert.frameSizes[f].X / 2;
                            offsetY += convert.frameSizes[f].Y / 2;
                        }
                        else {
                            offsetX = offsetY = 0;
                        }
                    }

                    RSDKv5.Sprite.Animation.Frame fr = animEntry.AddFrame(
                        boxx.Rect.X, boxx.Rect.Y,
                        crop.Width, crop.Height,
                        offsetX, offsetY,
                        convert.frameDuration[f],
                        boxx.PackageID
                    );

                    if (isFont) {
                        fr.ID = (ushort)boxx.Rect.Width;
                    }
                    else if (convert.frameIsLoopPoint != null && convert.frameIsLoopPoint[f] && animEntry.LoopIndex == 0) {
                        animEntry.LoopIndex = (byte)(f - range.Start);
                    }

                    Rectangle[]? frameHitboxes = convert.frameHitboxes[f];
                    for (int h = 0; h < outSprite.HitboxNames.Count; h++) {
                        int index = h - convert.HitboxStartIndex;
                        if (frameHitboxes != null && index >= 0 && index < frameHitboxes.Length) {
                            fr.AddHitbox(
                                frameHitboxes[index].X, frameHitboxes[index].Y,
                                frameHitboxes[index].Width, frameHitboxes[index].Height
                            );
                        }
                        else {
                            fr.AddHitbox(0, 0, 0, 0);
                        }
                    }
                }

                if (isFont) {
                    if (tallestFrame < 0) {
                        tallestFrame = 0;
                    }
                    animEntry.LoopIndex = (byte)tallestFrame;
                }

                outSprite.Animations.Add(animEntry);
            }
        }

        private void SaveRSDKSpriteFile(RSDKv5.Sprite sprite, string filename) {
            Program.LogVerbose("Saving sprite " + filename);

            // If the path doesn't exist, create it.
            string? directoryPath = Path.GetDirectoryName(filename);
            if (directoryPath != null && directoryPath != "" && !Directory.Exists(directoryPath)) {
                Directory.CreateDirectory(directoryPath);
            }

            // Save sprite file
            using (FileStream fs = new FileStream(filename, FileMode.Create)) {
                using (BinaryWriter writer = new BinaryWriter(fs)) {
                    sprite.Write(writer);
                }
            }
        }

        private static uint JenkinsHash(uint key, uint hash) {
            hash += key;
            hash += hash << 10;
            hash ^= hash >> 6;

            hash += hash << 3;
            hash ^= hash >> 11;
            hash += hash << 15;
            return hash;
        }
    }
}
