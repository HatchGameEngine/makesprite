using System;
using System.IO;

namespace makesprite {
    class Program {
        public const string SPRITE_EXTENSION = ".bin";

        static List<string> InputFiles = new List<string>();

        static string OutputFilename = "";

        public static Converter.Options ConverterOptions = new Converter.Options();

        static bool GroupSplitSheets = false;
        static bool IgnorePaletteMismatch = false;

        private class SpriteGroup {
            public List<Sprite> Sprites = new List<Sprite>();
            public List<string> Filenames = new List<string>();
        };

        static int Main(string[] args) {
            if (args.Length == 0 || args.Any(match => match == "-h" || match == "--help")) {
                PrintUsage();
                return 1;
            }

            List<string> cmdLineArgs = new List<string>(args);
            if (!ParseCommandLineArgs(cmdLineArgs)) {
                return 1;
            }

            if (InputFiles.Count == 0) {
                Console.WriteLine("Missing argument --input");
                PrintUsage();
                return 1;
            }

            List<Sprite> sprites = ReadInputFiles();

            Converter converter = new Converter();
            converter.CurrentOptions = ConverterOptions;

            string outFilename = OutputFilename;
            if (outFilename == "") {
                outFilename = Path.GetFileNameWithoutExtension(InputFiles[0]);
                outFilename += SPRITE_EXTENSION;
            }

            if (ConverterOptions.SplitBy == Converter.SplitMode.Groups) {
                if (!ConvertGroupedSprites(converter, sprites, InputFiles, outFilename)) {
                    return 1;
                }
            }
            else {
                if (!converter.Convert(sprites, InputFiles, outFilename)) {
                    return 1;
                }
            }

            return 0;
        }

        private static List<Sprite> ReadInputFiles() {
            List<Sprite> sprites = new List<Sprite>();

            // Read sprite files
            for (int i = 0; i < InputFiles.Count; i++) {
                Sprite? sprite = null;
                string filename = InputFiles[i];

                LogVerbose("Reading file " + filename);

                try {
                    sprite = DetectAndReadSpriteFile(filename);
                }
                catch (System.IO.FileNotFoundException) {
                    Console.WriteLine("Could not find file " + filename);
                    Environment.Exit(1);
                }

                sprites.Add(sprite);
            }

            return sprites;
        }

        private static Sprite DetectAndReadSpriteFile(string filename) {
            Sprite? sprite = null;

            string format = "unknown";

            using (FileStream stream = new FileStream(filename, FileMode.Open)) {
                if (Aseprite.File.IsValid(stream)) {
                    format = "Aseprite file";

                    stream.Seek(0, SeekOrigin.Begin);

                    Aseprite.File file = new Aseprite.File();
                    sprite = file.Read(stream);
                }

                stream.Seek(0, SeekOrigin.Begin);

                if (GIF.File.IsValid(stream)) {
                    format = "GIF";

                    stream.Seek(0, SeekOrigin.Begin);

                    GIF.File file = new GIF.File(stream);
                    sprite = new GIF.Sprite(file, Path.GetFileNameWithoutExtension(filename), IgnorePaletteMismatch);
                }
            }

            if (sprite == null) {
                Console.WriteLine("Unrecognized file format for " + filename);
                Environment.Exit(1);
            }

            LogVerbose("Format: " + format);

            return sprite;
        }

        private static Dictionary<string, SpriteGroup> SplitSpritesByGroups(List<Sprite> sprites, List<string> filenames) {
            Dictionary<string, SpriteGroup> groups = new Dictionary<string, SpriteGroup>();

            LogVerbose("Splitting sprites by groups...");

            for (int i = 0; i < sprites.Count; i++) {
                Sprite sprite = sprites[i];

                string outFilename;
                if (OutputFilename == "") {
                    outFilename = Path.GetFileNameWithoutExtension(filenames[i]);
                    outFilename += SPRITE_EXTENSION;
                }
                else {
                    outFilename = OutputFilename;
                }

                string baseFilename = Path.GetFileNameWithoutExtension(outFilename);
                string fileExtension = Path.GetExtension(outFilename);

                // Find the loop frame layer first
                int loopFrameLayerIndex = -1;
                for (int l = 0; l < sprite.Layers.Count; l++) {
                    Sprite.Layer layer = sprite.Layers[l];
                    if (Converter.IsLoopFrameLayer(layer)) {
                        if (loopFrameLayerIndex == -1) {
                            loopFrameLayerIndex = l;
                        }
                        else {
                            Warning("More than one loop frame layer! Ignoring.");
                        }
                    }
                }

                // Now split by groups
                for (int l = 0; l < sprite.Layers.Count; l++) {
                    Sprite.Layer layer = sprite.Layers[l];
                    if (!layer.IsGroup || layer.Parent != null || l == loopFrameLayerIndex) {
                        continue;
                    }

                    string groupName = baseFilename + layer.Name + fileExtension;

                    LogVerbose("  Group: \"" + layer.Name + "\"");

                    SpriteGroup currentGroup;
                    if (groups.ContainsKey(groupName)) {
                        currentGroup = groups[groupName];
                    }
                    else {
                        currentGroup = new SpriteGroup();
                        groups.Add(groupName, currentGroup);
                    }

                    Sprite groupSprite = sprite.Copy();
                    groupSprite.Layers = layer.Children;

                    for (int f = 0; f < sprite.Frames.Count; f++) {
                        Sprite.Frame originalFrame = sprite.Frames[f];
                        Sprite.Frame frame = originalFrame.Copy();

                        for (int p = 0; p < groupSprite.Layers.Count; p++) {
                            frame.PixelData.Add(originalFrame.PixelData[l + 1 + p]);
                        }

                        // Add the pixel data of the loop frame layer
                        if (loopFrameLayerIndex != -1) {
                            frame.PixelData.Add(originalFrame.PixelData[loopFrameLayerIndex]);
                        }

                        groupSprite.Frames.Add(frame);
                    }

                    // Add the loop frame layer itself
                    if (loopFrameLayerIndex != -1) {
                        groupSprite.Layers.Add(sprite.Layers[loopFrameLayerIndex]);
                    }

                    currentGroup.Sprites.Add(groupSprite);
                    currentGroup.Filenames.Add(groupName);
                }
            }

            return groups;
        }

        private static bool ConvertGroupedSprites(Converter converter, List<Sprite> sprites, List<string> filenames, string outFilename) {
            Dictionary<string, SpriteGroup> groups = SplitSpritesByGroups(sprites, filenames);

            if (GroupSplitSheets) {
                foreach (var item in groups) {
                    SpriteGroup group = item.Value;
                    if (!converter.Convert(group.Sprites, group.Filenames, item.Key)) {
                        return false;
                    }
                }
            }
            else {
                sprites.Clear();
                filenames.Clear();

                foreach (var item in groups) {
                    SpriteGroup group = item.Value;
                    foreach (var groupSprite in group.Sprites) {
                        sprites.Add(groupSprite);
                    }
                    foreach (var groupName in group.Filenames) {
                        filenames.Add(groupName);
                    }
                }

                if (!converter.Convert(sprites, filenames, outFilename)) {
                    return false;
                }
            }

            return true;
        }

        static void PrintUsage() {
            Console.WriteLine("""
usage: makesprite -i | --input <file>... [-o | --output <path>]
       [--sheet-path <path>] [--max-sheet-width <size>] [--max-sheet-height <size>]
       [--keep-canvas-offsets] [--no-offsets] [--offset-x <amount>] [--offset-y <amount>]
       [-s | --split-groups] [--combine-sheets] [--frame-sort <mode>]
       [-f | --font] [-h | --help]

Options:
  -i, --input <file>...      A list of files to convert.
  -o, --output <file>        The name of the output. This option also defines
                             the name of the output spritesheets. If splitting
                             by files, this option only defines the name of the
                             output spritesheets, and the output sprites are
                             named after the input file names. If splitting by
                             groups, the output sprites are named after the
                             group names, prefixed by the argument passed to
                             this option.
  --sheet-path               The parent path to use for the spritesheet names.
                             This affects the paths written to the sprite, not
                             where the spritesheet images are exported.
  --max-sheet-width <size>   The maximum width of a spritesheet.
  --max-sheet-height <size>  The maximum height of a spritesheet.
  --keep-canvas-offsets      Preserve the offsets of the original canvas.
  --no-offsets               Do not define offsets for any frames.
  --offset-x <amount>        Offset all frames horizontally by the given
                             amount. A positive value offsets the frames to
                             the right, and a negative value offsets the frames
                             to the left.
  --offset-y <amount>        Offset all frames vertically by the given amount.
                             A positive value offsets the frames downwards, and
                             a negative value offsets the frames upwards.
  -s, --split-by             How to split the input files.
                             Accepted options:
                               - none: Don't split.
                               - files: Export one sprite for each file.
                               - groups: Export one sprite for each group.
                             The default is 'none'.
  --group-split-sheets       Split spritesheets by groups.
  --frame-sort <mode>        How to sort the frames in the spritesheet.
                             Accepted options:
                               - none: Don't sort.
                               - area: Sort by the area of the frame.
                               - width: Sort by the width of the frame.
                               - height: Sort by the height of the frame.
                               - maxside: Sort by largest side of the frame.
                               - areaheight: Sort by area, then by height.
                             The default is 'areaheight'.
  --export-palette           Export .hpal palettes.
  --ignore-palette-mismatch  Keep sprites palettized even if the frames have
                             palettes that don't match. The spritesheets
                             will use the palette of the first frame.
  --no-sheets                Don't export spritesheets.
  --no-sprites               Don't export sprites.
  -f, --font                 Output a font sprite.
  -h, --help                 Show this message and exit.
""");
        }

        static bool ParseNoArgOption(string option) {
            switch (option) {
                case "--no-offsets":
                    ConverterOptions.NoOffsets = true;
                    return true;
                case "--keep-canvas-offsets":
                    ConverterOptions.KeepCanvasOffsets = true;
                    return true;
                case "--group-split-sheets":
                    GroupSplitSheets = true;
                    return true;
                case "--export-palette":
                    ConverterOptions.SavePalettes = true;
                    return true;
                case "--ignore-palette-mismatch":
                    IgnorePaletteMismatch = true;
                    return true;
                case "--no-sheets":
                    ConverterOptions.SaveSheets = false;
                    return true;
                case "--no-sprites":
                    ConverterOptions.SaveSprites = false;
                    return true;
                case "--font":
                case "-f":
                    ConverterOptions.IsFont = true;
                    return true;
                case "--verbose":
                    ConverterOptions.Verbose = true;
                    return true;
                default:
                    return false;
            }
        }

        static bool ParseSingleArgOption(string option, List<string> args, int index) {
            switch (option) {
                case "--output":
                case "-o":
                    OutputFilename = GetNextArg(args, index);
                    return true;
                case "--sheet-path":
                    ConverterOptions.SheetPath = GetNextArg(args, index);
                    return true;
                case "--max-sheet-width": {
                    string arg = GetNextArg(args, index);
                    int size = ParseNumericOption(option, arg);
                    if (size < 0) {
                        Console.WriteLine("Invalid argument for " + option);
                        Environment.Exit(1);
                        return false;
                    }
                    ConverterOptions.MaxSheetWidth = size;
                    return true;
                }
                case "--max-sheet-height": {
                    string arg = GetNextArg(args, index);
                    int size = ParseNumericOption(option, arg);
                    if (size < 0) {
                        Console.WriteLine("Invalid argument for " + option);
                        Environment.Exit(1);
                        return false;
                    }
                    ConverterOptions.MaxSheetHeight = size;
                    return true;
                }
                case "--offset-x": {
                    string arg = GetNextArg(args, index);
                    ConverterOptions.OffsetX = ParseNumericOption(option, arg);
                    return true;
                }
                case "--offset-y": {
                    string arg = GetNextArg(args, index);
                    ConverterOptions.OffsetY = ParseNumericOption(option, arg);
                    return true;
                }
                case "--split-by":
                case "-s": {
                    string arg = GetNextArg(args, index);
                    switch (arg.ToLower()) {
                    case "none":
                        ConverterOptions.SplitBy = Converter.SplitMode.None;
                        break;
                    case "files":
                        ConverterOptions.SplitBy = Converter.SplitMode.Files;
                        break;
                    case "groups":
                        ConverterOptions.SplitBy = Converter.SplitMode.Groups;
                        break;
                    default:
                        Console.WriteLine("Invalid argument for " + option);
                        Environment.Exit(1);
                        return false;
                    }
                    return true;
                }
                case "--frame-sort": {
                    SpritePacker.SortMode sortMode;

                    string arg = GetNextArg(args, index);
                    switch (arg.ToLower()) {
                    case "none":
                        sortMode = SpritePacker.SortMode.ID;
                        break;
                    case "area":
                        sortMode = SpritePacker.SortMode.Area;
                        break;
                    case "width":
                        sortMode = SpritePacker.SortMode.Width;
                        break;
                    case "height":
                        sortMode = SpritePacker.SortMode.Height;
                        break;
                    case "maxside":
                        sortMode = SpritePacker.SortMode.MaxSide;
                        break;
                    case "areaheight":
                        sortMode = SpritePacker.SortMode.AreaAndHeight;
                        break;
                    default:
                        Console.WriteLine("Invalid argument for " + option);
                        Environment.Exit(1);
                        return false;
                    }
                    ConverterOptions.SortBy = sortMode;
                    return true;
                }
                default:
                    return false;
            }
        }

        static string GetNextArg(List<string> args, int index) {
            if (index >= args.Count) {
                Console.WriteLine("Missing argument");
                Environment.Exit(1);
            }

            return args[index];
        }

        static bool ParseMultiArgOption(string option, List<string> args) {
            switch (option) {
                case "--input":
                case "-i":
                    ValidateMultiArgs(args);

                    InputFiles.Clear();

                    foreach (string arg in args) {
                        InputFiles.Add(arg);
                    }

                    return true;
                default:
                    return false;
            }
        }

        static void ValidateMultiArgs(List<string> args) {
            if (args.Count == 0) {
                Console.WriteLine("Missing arguments");
                Environment.Exit(1);
            }
        }

        static int ParseNumericOption(string option, string arg) {
            int offset = 0;

            if (!int.TryParse(arg, out offset)) {
                Console.WriteLine("Invalid argument for " + option);
                Environment.Exit(1);
            }

            return offset;
        }

        static bool ParseCommandLineArgs(List<string> args) {
            for (int i = 0; i < args.Count;) {
                if (ParseNoArgOption(args[i])) {
                    args.RemoveAt(i);
                }
                else if (args[i].StartsWith("-") || args[i].StartsWith("--")) {
                    string option = args[i];

                    args.RemoveAt(i);

                    if (ParseSingleArgOption(option, args, i)) {
                        args.RemoveAt(i);
                        continue;
                    }

                    List<string> optionArgs = new List<string>();

                    while (i < args.Count) {
                        if (args[i].StartsWith("-")) {
                            break;
                        }

                        optionArgs.Add(args[i]);
                        args.RemoveAt(i);
                    }

                    if (!ParseMultiArgOption(option, optionArgs)) {
                        Console.WriteLine("Unrecognized option " + option);
                        return false;
                    }
                }
                else {
                    i++;
                }
            }

            return true;
        }

        public static void LogVerbose(string text) {
            if (ConverterOptions.Verbose) {
                Console.WriteLine(text);
            }
        }

        public static void Warning(string text) {
            Console.WriteLine("Warning: " + text);
        }
    }
}
