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
            public List<SpritePacker.Rect> frameCrops = new List<SpritePacker.Rect>();
            public List<Vector2> frameSizes = new List<Vector2>();
            public List<Vector2> frameOffsets = new List<Vector2>();
            public List<SpritePacker.Rect[]?> frameHitboxes = new List<SpritePacker.Rect[]?>();
            public List<System.Drawing.Color[]> frameSheets = new List<System.Drawing.Color[]>();

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
            public int MaxSheetWidth = SpritePacker.MAX_SHEET_WIDTH;
            public int MaxSheetHeight = SpritePacker.MAX_SHEET_HEIGHT;
            public SpritePacker.SortMode SortBy = SpritePacker.SortMode.AreaAndHeight;
            public string? SheetPath = null;
            public bool NoOffsets = false;
            public bool KeepCanvasOffsets = false;
            public int OffsetX = 0;
            public int OffsetY = 0;
            public bool IsFont = false;
            public bool Verbose = false;
        }

        public Options CurrentOptions = new Converter.Options();

        private List<SpritePacker.Box> Boxes = new List<SpritePacker.Box>();
        private Dictionary<uint, int> HashToFrameIndex = new Dictionary<uint, int>();

        private bool All8Bpp = true;

        private string? OutFilenamePath;
        private string? SheetRelFilename;
        private bool IsInRootPath = false;

        private bool WarnedAboutSheetPaths = false;

        public bool Convert(List<Sprite> sprites, List<string> filenames, string outputFilename) {
            if (sprites.Count == 0) {
                Console.WriteLine("No sprites to convert");
                return false;
            }

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

            bool singleAnim = conversionInfos.Count == 1;

            System.IO.DirectoryInfo? parentPath = Directory.GetParent(outputFilename);
            if (!singleAnim && parentPath != null) {
                OutFilenamePath = parentPath.FullName;
            }
            else {
                OutFilenamePath = outputFilename;
            }

            if (CurrentOptions.SheetPath != null) {
                SheetRelFilename = CurrentOptions.SheetPath;
            }
            else if (parentPath != null) {
                string parent = "";
                if (PathHelper.GetSpritesFolder(OutFilenamePath, out parent)) {
                    string relFilename = parentPath.FullName;
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
                    if (!SaveSpritesheets(spritesheets, spritesheetNames, parentPath)) {
                        Console.WriteLine("Could not save spritesheets");
                        return false;
                    }
                }

                // Save palettes
                Color[]? palette = spritesheets[0].Palette;
                if (CurrentOptions.SavePalettes && palette != null) {
                    string filename;
                    if (!singleAnim) {
                        filename = Path.Combine(OutFilenamePath, conversionInfos[0].Filename);
                    }
                    else {
                        filename = OutFilenamePath;
                    }
                    filename = Path.GetFileNameWithoutExtension(filename) + ".hpal";

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
            }

            // Create animation files
            if (CurrentOptions.SaveSprites) {
                for (int a = 0; a < conversionInfos.Count; a++) {
                    ConversionInfo info = conversionInfos[a];

                    string filename;
                    if (!singleAnim) {
                        filename = Path.Combine(OutFilenamePath, Path.GetFileName(info.Filename));
                    }
                    else {
                        filename = OutFilenamePath;
                    }

                    SaveAnimation(info, spritesheetNames, outputFilename);
                }
            }

            return true;
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
                    convert.frameHitboxes.Add(new SpritePacker.Rect[convert.HitboxLayers.Count]);
                }
                else {
                    convert.frameHitboxes.Add(null);
                }

                // Write pixels onto frame's canvas
                SpritePacker.Rect crop;
                if (sprite.ColorDepth == 8) {
                    crop = BuildIndexedFrame(convert, sprite.Frames[f], frameCanvas);
                }
                else if (sprite.ColorDepth == 16) {
                    crop = BuildGrayscaleFrame(convert, sprite.Frames[f], frameCanvas);
                }
                else {
                    crop = BuildFrame(convert, sprite.Frames[f], frameCanvas);
                }

                // Adjust Hitboxes
                for (int h = 0; h < convert.HitboxLayers.Count; h++) {
                    SpritePacker.Rect[]? frameHitboxes = convert.frameHitboxes.Last();
                    if (frameHitboxes == null) {
                        continue;
                    }

                    SpritePacker.Rect? hitbox = frameHitboxes[h];
                    bool hasHitbox = hitbox != null;
                    if (hitbox == null) {
                        hitbox = new SpritePacker.Rect(0, 0, 0, 0);
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
                uint frameHash = 0xDEADBEEF;
                for (int p = 0; p < canvasSize; p++) {
                    int px = p % sprite.Width;
                    int py = p / sprite.Width;
                    // Width and Height in this respect are just X2 and Y2
                    if (px >= crop.X && py >= crop.Y &&
                        px <= crop.Width && py <= crop.Height) {
                        frameHash = JenkinsHash((uint)frameCanvas[p].ToArgb(), frameHash);
                    }
                }

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
                    HashToFrameIndex.Add(frameHash, Boxes.Count);
                    convert.frameMap.Add(Boxes.Count);

                    SpritePacker.Box box = new SpritePacker.Box(Boxes.Count, new SpritePacker.Rect(0, 0, crop.Width, crop.Height));
                    box.OffX = crop.X - centerX;
                    box.OffY = crop.Y - centerY;
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

        private void AddHitbox(SpritePacker.Rect[] frameHitboxes, int hitboxIndex, int px, int py, int width, int height) {
            if (frameHitboxes[hitboxIndex] == null) {
                frameHitboxes[hitboxIndex] = new SpritePacker.Rect(width, height, 0, 0);
            }

            SpritePacker.Rect hitbox = frameHitboxes[hitboxIndex];
            hitbox.X = Math.Min(hitbox.X, px);
            hitbox.Y = Math.Min(hitbox.Y, py);
            hitbox.Width = Math.Max(hitbox.Width, px);
            hitbox.Height = Math.Max(hitbox.Height, py);
        }

        private SpritePacker.Rect BuildFrame(ConversionInfo convert, Sprite.Frame frame, System.Drawing.Color[] frameCanvas) {
            Sprite? sprite = convert.Input;
            int canvasSize = sprite.Width * sprite.Height;

            SpritePacker.Rect crop = new SpritePacker.Rect(sprite.Width, sprite.Height, 0, 0);

            SpritePacker.Rect[]? frameHitboxes = convert.frameHitboxes.Last();

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

                        frameCanvas[p] = System.Drawing.Color.FromArgb((int)color);

                        int px = p % sprite.Width;
                        int py = p / sprite.Width;
                        crop.X = Math.Min(crop.X, px);
                        crop.Y = Math.Min(crop.Y, py);
                        crop.Width = Math.Max(crop.Width, px);
                        crop.Height = Math.Max(crop.Height, py);
                    }
                }
            }

            return crop;
        }

        private SpritePacker.Rect BuildGrayscaleFrame(ConversionInfo convert, Sprite.Frame frame, System.Drawing.Color[] frameCanvas) {
            Sprite? sprite = convert.Input;
            int canvasSize = sprite.Width * sprite.Height;

            SpritePacker.Rect crop = new SpritePacker.Rect(sprite.Width, sprite.Height, 0, 0);

            SpritePacker.Rect[]? frameHitboxes = convert.frameHitboxes.Last();

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

                        frameCanvas[p] = System.Drawing.Color.FromArgb((int)alpha, (int)value, (int)value, (int)value);

                        int px = p % sprite.Width;
                        int py = p / sprite.Width;
                        crop.X = Math.Min(crop.X, px);
                        crop.Y = Math.Min(crop.Y, py);
                        crop.Width = Math.Max(crop.Width, px);
                        crop.Height = Math.Max(crop.Height, py);
                    }
                }
            }

            return crop;
        }

        private SpritePacker.Rect BuildIndexedFrame(ConversionInfo convert, Sprite.Frame frame, System.Drawing.Color[] frameCanvas) {
            Sprite? sprite = convert.Input;
            int canvasSize = sprite.Width * sprite.Height;

            SpritePacker.Rect crop = new SpritePacker.Rect(sprite.Width, sprite.Height, 0, 0);
            if (sprite.Palette == null) {
                return crop;
            }

            SpritePacker.Rect[]? frameHitboxes = convert.frameHitboxes.Last();

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

                        frameCanvas[p] = System.Drawing.Color.FromArgb((int)index, 0, 0);

                        int px = p % sprite.Width;
                        int py = p / sprite.Width;
                        crop.X = Math.Min(crop.X, px);
                        crop.Y = Math.Min(crop.Y, py);
                        crop.Width = Math.Max(crop.Width, px);
                        crop.Height = Math.Max(crop.Height, py);
                    }
                }
            }

            return crop;
        }

        private Color[]? BuildPalette(uint[] asePalette) {
            Color[] palette = new Color[asePalette.Length];

            for (int i = 0; i < palette.Length; i++) {
                uint argb = asePalette[i];
                int alpha = (int)((argb & 0xFF000000) >> 24);
                int blue = (int)((argb & 0xFF0000) >> 16);
                int green = (int)((argb & 0xFF00) >> 8);
                int red = (int)((argb & 0xFF));
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
                    SpritePacker.Rect crop = convert.frameCrops[i];

                    if (addedBoxID.ContainsKey(uniqueFrameIndex)) {
                        continue;
                    }

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

                        SpritesheetImage sheet = spritesheets[boxx.PackageID];
                        byte[] bytes = sheet.Data;
                        int stride = sheet.Width * bytesPerPixel;
                        if (All8Bpp) {
                            bytes[(py * stride) + px] = c.R;
                        }
                        else {
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
            string outFilename = Path.GetFileNameWithoutExtension(outputFilename);

            for (int i = 0; i < numSpritesheets; i++) {
                string suffix;
                if (numSpritesheets != 1) {
                    suffix = (i + fileExtension);
                }
                else {
                    suffix = fileExtension;
                }

                string sheetFilename = outFilename.Replace('\\', '/') + suffix;
                names.Add(sheetFilename);
            }
        }

        private bool SaveSpritesheets(List<SpritesheetImage> spritesheets, List<string> spritesheetNames, System.IO.DirectoryInfo? parentPath) {
            for (int i = 0; i < spritesheets.Count; i++) {
                string outSheetFilename = spritesheetNames[i];
                if (parentPath != null) {
                    outSheetFilename = Path.Combine(parentPath.FullName, outSheetFilename);
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

        private void SaveAnimation(ConversionInfo info, List<string> spritesheetNames, string filename) {
            Sprite sprite = info.Input;
            if (sprite.Frames.Count == 0) {
                Console.WriteLine("No frames for sprite " + filename);
                return;
            }

            // No ranges? Create one.
            if (sprite.AnimRanges.Count == 0) {
                Program.LogVerbose("Sprite " + filename + " has no ranges. Creating one.");

                Sprite.AnimRange range = new Sprite.AnimRange("Animation", 0, sprite.Frames.Count - 1, 0);
                sprite.AnimRanges.Add(range);
            }

            RSDKv5.Sprite outSprite = new RSDKv5.Sprite();

            GenerateAnimation(outSprite, info, spritesheetNames);

            Program.LogVerbose("Saving sprite " + filename);

            // If the path doesn't exist, create it.
            string? directoryPath = Path.GetDirectoryName(filename);
            if (directoryPath != null && directoryPath != "" && !Directory.Exists(directoryPath)) {
                Directory.CreateDirectory(directoryPath);
            }

            // Save animation file
            using (FileStream fs = new FileStream(filename, FileMode.Create)) {
                using (BinaryWriter writer = new BinaryWriter(fs)) {
                    outSprite.Write(writer);
                }
            }
        }

        private void GenerateAnimation(RSDKv5.Sprite outSprite,
            ConversionInfo convert,
            List<string> spritesheetNames) {
            Sprite sprite = convert.Input;
            if (sprite.Frames.Count == 0) {
                return;
            }

            // Add hitboxes
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
                        boxx.Rect.Width, boxx.Rect.Height,
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

                    SpritePacker.Rect[]? frameHitboxes = convert.frameHitboxes[f];
                    if (frameHitboxes != null) {
                        for (int h = 0; h < frameHitboxes.Length; h++) {
                            fr.AddHitbox(
                                frameHitboxes[h].X, frameHitboxes[h].Y,
                                frameHitboxes[h].Width, frameHitboxes[h].Height
                            );
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
