using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using Hatch;

namespace makesprite {
    public class Converter {
        private const string FONT_RANGE_NAME_PREFIX = "Font:";

        public enum SpriteFormat {
            RSDKv5,
            JSON
        }

        public enum SplitMode {
            None,
            Files,
            Groups
        }

        public class ConversionInfo {
            public Sprite Input;
            public string Filename;

            public List<int> frameMap = new List<int>();
            public List<Rectangle> frameCrops = new List<Rectangle>();
            public List<Vector2> frameSizes = new List<Vector2>();
            public List<Vector2> frameOffsets = new List<Vector2>();
            public List<System.Drawing.Color[]> frameSheets = new List<System.Drawing.Color[]>();

            public int HitboxStartIndex = 0;

            public ConversionInfo(Sprite input, string filename) {
                Input = input;
                Filename = filename;
            }
        }

        public class Options {
            public SpriteFormat OutputFormat = SpriteFormat.RSDKv5;
            public string SpriteExtension = "";
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
            public int Framerate = 60;
            public int ExportFramerate = 60;
            public bool TrimFrames = true;
            public bool MergeDuplicateFrames = true;
            public bool Sequence = false;
            public bool UseRDSKCompatibleSheetPaths = false;
            public bool IsFont = false;
            public bool CanOverwrite = false;
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

            List<ConversionInfo> conversionInfos = new List<ConversionInfo>();

            for (int i = 0; i < sprites.Count; i++) {
                Sprite? sprite = sprites[i];

                Program.LogVerbose("Sprite " + i + ": " + filenames[i]);
                Program.LogVerbose("  Color depth: " + sprite.ColorDepth + " bits per pixel");

                // Check ColorDepth
                if (sprite.ColorDepth != 8) {
                    All8Bpp = false;
                }

                sprite.DetectHitboxLayers();

                conversionInfos.Add(new ConversionInfo(sprite, filenames[i]));
            }

            Program.LogVerbose("Palettized: " + (All8Bpp ? "Yes" : "No"));

            ParentPath = Directory.GetParent(outputFilename);

            if (OutFilenamePath == "") {
                OutFilenamePath = outputFilename;
            }

            List<string> outputFilenames = GetOutputFilenames(conversionInfos);

            if (CurrentOptions.SheetPath != null) {
                SheetRelFilename = CurrentOptions.SheetPath;
            }
            else if (ParentPath != null) {
                bool onlyResources = !CurrentOptions.UseRDSKCompatibleSheetPaths;
                string parent = "";

                if (PathHelper.GetSpritesFolder(OutFilenamePath, out parent, onlyResources)) {
                    string relFilename = ParentPath.FullName;
                    if (relFilename.Length > parent.Length) {
                        SheetRelFilename = relFilename.Substring(parent.Length + 1);
                    }
                    else {
                        IsInRootPath = PathHelper.EndsInRootPath(parent, onlyResources);
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

                string palettePath = "";
                if (CurrentOptions.SavePalettes) {
                    string filename = Path.GetFileNameWithoutExtension(outputFilename);
                    if (sprites.Count > 1 && ParentPath != null) {
                        filename = Path.Combine(ParentPath.FullName, filename);
                    }
                    filename += ".hpal";

                    if (!CurrentOptions.CanOverwrite && File.Exists(filename)) {
                        throw new InvalidOperationException("File \"" + filename + "\" already exists! Use --overwrite to replace it.");
                    }

                    palettePath = filename;
                }

                // Save spritesheets
                if (CurrentOptions.SaveSheets) {
                    if (!SaveSpritesheets(spritesheets, spritesheetNames)) {
                        Console.WriteLine("Could not save spritesheets");
                        return false;
                    }
                }

                // Save palette
                if (CurrentOptions.SavePalettes) {
                    Color[]? palette = spritesheets[0].Palette;
                    if (palette != null) {
                        SavePalette(palette, palettePath);
                    }
                    else {
                        Program.Warning("No palette in spritesheet. Did not export to " + palettePath);
                    }
                }
            }

            // Create sprite files or file
            if (CurrentOptions.SaveSprites) {
                GenerateSprites(conversionInfos, outputFilenames, spritesheetNames);
            }

            Program.LogVerbose("Converted " + sprites.Count + " sprite(s)");

            return true;
        }

        private List<string> GetOutputFilenames(List<ConversionInfo> conversionInfos) {
            List<string> outputFilenames = new List<string>();

            bool singleAnim = conversionInfos.Count == 1;

            string basePath;
            if (singleAnim || CurrentOptions.SplitBy == SplitMode.None) {
                basePath = OutFilenamePath;
            }
            else {
                basePath = conversionInfos[0].Filename;
            }

            for (int a = 0; a < conversionInfos.Count; a++) {
                string filename;
                if (CurrentOptions.SplitBy == SplitMode.None || CurrentOptions.SplitBy != SplitMode.Groups && singleAnim) {
                    filename = GetExportedFilename(basePath, CurrentOptions.SpriteExtension, singleAnim);
                }
                else {
                    filename = GetExportedFilename(conversionInfos[a].Filename, CurrentOptions.SpriteExtension, singleAnim);
                }

                if (!CurrentOptions.CanOverwrite && File.Exists(filename)) {
                    throw new InvalidOperationException("File \"" + filename + "\" already exists! Use --overwrite to replace it.");
                }

                outputFilenames.Add(filename);
            }

            return outputFilenames;
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

            Program.LogVerbose("Number of frames in " + convert.Filename + ": " + sprite.Frames.Count);

            // Obtain all unique frame spritesheets
            for (int f = 0; f < sprite.Frames.Count; f++) {
                Sprite.Frame frame = sprite.Frames[f];

                // Initialize canvas
                int canvasSize = frame.Width * frame.Height;
                System.Drawing.Color[] frameCanvas = new System.Drawing.Color[canvasSize];
                for (int p = 0; p < canvasSize; p++) {
                    frameCanvas[p] = System.Drawing.Color.Transparent;
                }

                // Write pixels onto the frame's canvas
                Rectangle crop = new Rectangle(frame.Width, frame.Height, 0, 0);
                if (sprite.ColorDepth == 8) {
                    BuildIndexedFrame(convert, frame, frameCanvas, crop);
                }
                else if (sprite.ColorDepth == 16) {
                    BuildGrayscaleFrame(convert, frame, frameCanvas, crop);
                }
                else {
                    BuildFrame(convert, frame, frameCanvas, crop);
                }

                // Hash canvas to check for uniqueness
                uint frameHash = GetFrameHash(frame, frameCanvas, crop, 0xDEADBEEF);

                // Adjust Box from X1Y1X2Y2 to XYWH
                crop.Width -= crop.X - 1;
                crop.Height -= crop.Y - 1;
                crop.Width = Math.Max(crop.Width, 0);
                crop.Height = Math.Max(crop.Height, 0);

                // Add frame properties
                convert.frameCrops.Add(crop);
                convert.frameSheets.Add(frameCanvas);
                convert.frameSizes.Add(new Vector2(frame.Width, frame.Height));

                Vector2 frameSize;
                if (CurrentOptions.TrimFrames) {
                    frameSize = new Vector2(crop.Width, crop.Height);
                }
                else {
                    frameSize = new Vector2(frame.Width, frame.Height);
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

                    Rectangle rect = new Rectangle(0, 0, frameSize.X, frameSize.Y);
                    SpritePacker.Box box = new SpritePacker.Box(Boxes.Count, rect);
                    Boxes.Add(box);
                }

                // Get offsets
                int offsetX = 0;
                int offsetY = 0;

                if (frame.Offsets != null) {
                    offsetX = frame.Offsets.X;
                    offsetY = frame.Offsets.Y;
                }
                else {
                    if (CurrentOptions.TrimFrames) {
                        offsetX = crop.X;
                        offsetY = crop.Y;
                    }

                    if (!CurrentOptions.KeepCanvasOffsets) {
                        offsetX -= frame.Width / 2;
                        offsetY -= frame.Height / 2;

                        // Adjust hitboxes
                        for (int h = 0; h < frame.Hitboxes.Count; h++) {
                            Sprite.Hitbox hitbox = frame.Hitboxes[h];
                            hitbox.X -= frame.Width / 2;
                            hitbox.Y -= frame.Height / 2;
                            hitbox.Width -= frame.Width / 2;
                            hitbox.Height -= frame.Height / 2;
                        }
                    }
                }

                int frameOffsetX = 0;
                int frameOffsetY = 0;

                if (!CurrentOptions.NoOffsets) {
                    frameOffsetX = offsetX;
                    frameOffsetY = offsetY;
                }

                frameOffsetX += CurrentOptions.OffsetX;
                frameOffsetY += CurrentOptions.OffsetY;

                convert.frameOffsets.Add(new Vector2(frameOffsetX, frameOffsetY));
            }
        }

        private uint GetFrameHash(Sprite.Frame frame, System.Drawing.Color[] canvas, Rectangle crop, uint frameHash) {
            if (crop.Width == 0 || crop.Height == 0) {
                return frameHash;
            }

            int canvasSize = frame.Width * frame.Height;

            for (int p = 0; p < canvasSize; p++) {
                int px = p % frame.Width;
                int py = p / frame.Width;
                // Width and Height in this respect are just X2 and Y2
                if (px >= crop.X && py >= crop.Y &&
                    px <= crop.Width && py <= crop.Height) {
                    frameHash = JenkinsHash((uint)canvas[p].ToArgb(), frameHash);
                }
            }

            return frameHash;
        }

        private void BuildFrame(ConversionInfo convert, Sprite.Frame frame, System.Drawing.Color[] canvas, Rectangle crop) {
            Sprite? sprite = convert.Input;

            for (int l = 0; l < frame.PixelData.Count; l++) {
                Sprite.Layer layer = sprite.Layers[l];
                if (!layer.CanDraw()) {
                    continue;
                }

                for (int y = 0; y < frame.Height; y++) {
                    for (int x = 0; x < frame.Width; x++) {
                        int px = frame.SheetX + x;
                        int py = frame.SheetY + y;

                        int p = px + (py * frame.PixelDataWidth);
                        uint argb = frame.PixelData[l][p];
                        uint color = (argb & 0xFF000000);
                        if (color == 0) {
                            continue;
                        }

                        color |= (argb & 0xFF0000) >> 16;
                        color |= (argb & 0xFF00);
                        color |= (argb & 0xFF) << 16;

                        p = x + (y * frame.Width);
                        canvas[p] = System.Drawing.Color.FromArgb((int)color);

                        crop.X = Math.Min(crop.X, x);
                        crop.Y = Math.Min(crop.Y, y);
                        crop.Width = Math.Max(crop.Width, x);
                        crop.Height = Math.Max(crop.Height, y);
                    }
                }
            }
        }

        private void BuildGrayscaleFrame(ConversionInfo convert, Sprite.Frame frame, System.Drawing.Color[] canvas, Rectangle crop) {
            Sprite? sprite = convert.Input;

            for (int l = 0; l < frame.PixelData.Count; l++) {
                Sprite.Layer layer = sprite.Layers[l];
                if (!layer.CanDraw()) {
                    continue;
                }

                for (int y = 0; y < frame.Height; y++) {
                    for (int x = 0; x < frame.Width; x++) {
                        int px = frame.SheetX + x;
                        int py = frame.SheetY + y;

                        int p = px + (py * frame.PixelDataWidth);
                        uint value = frame.PixelData[l][p];
                        uint alpha = (value & 0xFF00) >> 8;
                        if (alpha == 0) {
                            continue;
                        }

                        value &= 0xFF;

                        p = x + (y * frame.Width);
                        canvas[p] = System.Drawing.Color.FromArgb((int)alpha, (int)value, (int)value, (int)value);

                        crop.X = Math.Min(crop.X,px);
                        crop.Y = Math.Min(crop.Y, y);
                        crop.Width = Math.Max(crop.Width, x);
                        crop.Height = Math.Max(crop.Height, y);
                    }
                }
            }
        }

        private void BuildIndexedFrame(ConversionInfo convert, Sprite.Frame frame, System.Drawing.Color[] canvas, Rectangle crop) {
            Sprite? sprite = convert.Input;

            for (int l = 0; l < frame.PixelData.Count; l++) {
                Sprite.Layer layer = sprite.Layers[l];
                if (!layer.CanDraw()) {
                    continue;
                }

                for (int y = 0; y < frame.Height; y++) {
                    for (int x = 0; x < frame.Width; x++) {
                        int px = frame.SheetX + x;
                        int py = frame.SheetY + y;

                        int p = px + (py * frame.PixelDataWidth);
                        uint index = frame.PixelData[l][p];
                        if (index == (uint)sprite.TransparentPaletteIndex) {
                            continue;
                        }

                        p = x + (y * frame.Width);
                        canvas[p] = System.Drawing.Color.FromArgb((int)index, 0, 0);

                        crop.X = Math.Min(crop.X, x);
                        crop.Y = Math.Min(crop.Y, y);
                        crop.Width = Math.Max(crop.Width, x);
                        crop.Height = Math.Max(crop.Height, y);
                    }
                }
            }
        }

        private Color[]? BuildPalette(uint[] srcPalette) {
            Color[] palette = new Color[srcPalette.Length];

            for (int i = 0; i < palette.Length; i++) {
                uint argb = srcPalette[i];
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
                    uint[] firstPalette = firstSprite.Palette;

                    text += ", " + firstPalette.Length + " colors";

                    palette = BuildPalette(firstPalette);
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

                int bytesPerPixel = All8Bpp ? 1 : 4;

                for (int i = 0; i < convert.frameMap.Count; i++) {
                    int uniqueFrameIndex = convert.frameMap[i];
                    SpritePacker.Box boxx = Boxes[uniqueFrameIndex];
                    Rectangle crop = convert.frameCrops[i];

                    if (addedBoxID.ContainsKey(uniqueFrameIndex)) {
                        continue;
                    }

                    int frameWidth = convert.frameSizes[i].X;
                    int frameHeight = convert.frameSizes[i].Y;
                    int canvasSize = frameWidth * frameHeight;

                    SpritesheetImage sheet = spritesheets[boxx.PackageID];
                    byte[] bytes = sheet.Data;
                    int stride = sheet.Width * bytesPerPixel;

                    if (All8Bpp) {
                        for (int p = 0; p < canvasSize; p++) {
                            System.Drawing.Color c = convert.frameSheets[i][p];
                            if (c.A == 0) {
                                continue;
                            }

                            int px = p % frameWidth;
                            int py = p / frameWidth;

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

                            int px = p % frameWidth;
                            int py = p / frameWidth;

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

                            int px = p % frameWidth;
                            int py = p / frameWidth;

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
            List<string> paths = new List<string>();

            for (int i = 0; i < spritesheetNames.Count; i++) {
                string filename = spritesheetNames[i];
                if (ParentPath != null) {
                    filename = Path.Combine(ParentPath.FullName, filename);
                }

                if (!CurrentOptions.CanOverwrite && File.Exists(filename)) {
                    throw new InvalidOperationException("File \"" + filename + "\" already exists! Use --overwrite to replace it.");
                }

                paths.Add(filename);
            }

            for (int i = 0; i < spritesheets.Count; i++) {
                string filename = paths[i];

                Program.LogVerbose("Saving spritesheet " + filename);

                // If the path doesn't exist, create it.
                string? directoryPath = Path.GetDirectoryName(filename);
                if (directoryPath != null && directoryPath != "" && !Directory.Exists(directoryPath)) {
                    Directory.CreateDirectory(directoryPath);
                }

                SpritesheetImage sheet = spritesheets[i];
                PNG.File png = new PNG.File(sheet.Width, sheet.Height, sheet.ColorDepth, sheet.Data, sheet.Palette);
                using (FileStream fs = new FileStream(filename, FileMode.Create)) {
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

        private void GenerateSprites(List<ConversionInfo> conversionInfos, List<string> outputFilenames, List<string> spritesheetNames) {
            List<Hatch.Sprite> outputSprites = new List<Hatch.Sprite>();

            int framerate = CurrentOptions.ExportFramerate;

            Hatch.Sprite currentSprite = new Hatch.Sprite();
            currentSprite.Framerate = framerate;

            if (CurrentOptions.SplitBy == SplitMode.None) {
                outputSprites.Add(currentSprite);
            }

            // Prepare the sprites
            for (int a = 0; a < conversionInfos.Count; a++) {
                ConversionInfo info = conversionInfos[a];
                Sprite sourceSprite = info.Input;

                string filename = outputFilenames[a];
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

                PrepareSprite(currentSprite, info, spritesheetNames);

                if (CurrentOptions.SplitBy != SplitMode.None) {
                    outputSprites.Add(currentSprite);

                    if (a + 1 < conversionInfos.Count) {
                        currentSprite = new Hatch.Sprite();
                        currentSprite.Framerate = framerate;
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

                AddAnimationsToSprite(currentSprite, info);
            }

            // Save the sprites
            for (int i = 0; i < outputSprites.Count; i++) {
                SaveSpriteFile(outputSprites[i], outputFilenames[i]);
            }
        }

        private void PrepareSprite(Hatch.Sprite outSprite,
            ConversionInfo convert,
            List<string> spritesheetNames) {
            // Add hitboxes
            convert.HitboxStartIndex = outSprite.HitboxNames.Count;
            for (int h = 0; h < convert.Input.HitboxNames.Count; h++) {
                outSprite.HitboxNames.Add(convert.Input.HitboxNames[h]);
            }

            // Add spritesheets
            foreach (string sheet in spritesheetNames) {
                if (SheetRelFilename != null) {
                    // TODO: Figure out why I needed to do this
                    string pathStart = SheetRelFilename;
                    if (pathStart.StartsWith("Sprites/")) {
                        pathStart = pathStart.Substring("Sprites/".Length);
                    }

                    outSprite.SpritesheetNames.Add(Path.Combine(pathStart, sheet));
                }
                else if (IsInRootPath) {
                    outSprite.SpritesheetNames.Add(sheet);
                }
                else {
                    outSprite.SpritesheetNames.Add("./" + sheet);
                }
            }
        }

        private void AddAnimationsToSprite(Hatch.Sprite outSprite, ConversionInfo convert) {
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

                Hatch.Sprite.Animation animEntry;
                if (CurrentOptions.Sequence && outSprite.Animations.Count > 0) {
                    animEntry = outSprite.Animations[0];
                }
                else {
                    animEntry = new Hatch.Sprite.Animation(rangeName);
                    animEntry.Direction = (Hatch.Sprite.AnimationDirection)range.Direction;

                    if (isFont) {
                        animEntry.Speed = 0;
                    }
                }

                for (int f = range.Start; f <= range.End; f++) {
                    Sprite.Frame frame = sprite.Frames[f];
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

                    Hatch.Sprite.Animation.Frame fr = animEntry.AddFrame(
                        boxx.Rect.X, boxx.Rect.Y,
                        crop.Width, crop.Height,
                        offsetX, offsetY,
                        frame.Duration,
                        boxx.PackageID,
                        isFont ? boxx.Rect.Width : frame.ID
                    );

                    if (!isFont && frame.IsLoopPoint) {
                        animEntry.LoopFrame = (byte)(f - range.Start);
                    }

                    for (int h = 0; h < outSprite.HitboxNames.Count; h++) {
                        int index = h - convert.HitboxStartIndex;
                        if (index >= 0 && index < frame.Hitboxes.Count) {
                            Sprite.Hitbox hitbox = frame.Hitboxes[index];
                            fr.AddHitbox(outSprite.HitboxNames[h], hitbox.X, hitbox.Y, hitbox.Width, hitbox.Height);
                        }
                        else {
                            fr.AddHitbox(outSprite.HitboxNames[h], 0, 0, 0, 0);
                        }
                    }
                }

                if (isFont) {
                    if (tallestFrame < 0) {
                        tallestFrame = 0;
                    }
                    animEntry.LoopFrame = (byte)tallestFrame;
                }

                if (!CurrentOptions.Sequence || outSprite.Animations.Count == 0) {
                    outSprite.AddAnimation(animEntry);
                }
            }
        }

        private void SaveSpriteFile(Hatch.Sprite sprite, string filename) {
            Program.LogVerbose("Saving sprite " + filename);

            // If the path doesn't exist, create it.
            string? directoryPath = Path.GetDirectoryName(filename);
            if (directoryPath != null && directoryPath != "" && !Directory.Exists(directoryPath)) {
                Directory.CreateDirectory(directoryPath);
            }

            // Save sprite file
            using (FileStream fs = new FileStream(filename, FileMode.Create)) {
                switch (CurrentOptions.OutputFormat) {
                case SpriteFormat.RSDKv5:
                    Program.LogVerbose("  Format: RSDKv5");
                    using (BinaryWriter writer = new BinaryWriter(fs)) {
                        sprite.WriteRSDKv5(writer);
                    }
                    break;
                case SpriteFormat.JSON:
                    Program.LogVerbose("  Format: JSON");
                    using (StreamWriter writer = new StreamWriter(fs)) {
                        JsonSerializerOptions options = Hatch.Sprite.GetSerializerOptions();
                        string json = sprite.SerializeAsJSON(options);
                        writer.Write(json);
                    }
                    break;
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
