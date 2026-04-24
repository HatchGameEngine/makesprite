using System;
using System.IO;

using Aseprite;

namespace makesprite {
    class Program {
        static List<string> InputFiles = new List<string>();

        static string OutputFilename = "";

        public static Converter.Options ConverterOptions = new Converter.Options();

        static bool SplitGroups = false;
        static bool CombineSheets = false;

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
                outFilename += ".bin";
            }

            if (SplitGroups) {
                Dictionary<string, SpriteGroup> groups = SplitSpritesByGroups(sprites, InputFiles);

                sprites.Clear();
                InputFiles.Clear();

                foreach (var item in groups) {
                    SpriteGroup group = item.Value;
                    if (CombineSheets) {
                        foreach (var groupSprite in group.Sprites) {
                            sprites.Add(groupSprite);
                        }
                        foreach (var groupName in group.Filenames) {
                            InputFiles.Add(groupName);
                        }
                    }
                    else {
                        if (!converter.Convert(group.Sprites, group.Filenames, item.Key)) {
                            return 1;
                        }
                    }
                }

                if (!CombineSheets) {
                    return 0;
                }
            }

            if (!converter.Convert(sprites, InputFiles, outFilename)) {
                return 1;
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
                    using (FileStream stream = new FileStream(filename, FileMode.Open)) {
                        if (ASE.IsFile(stream)) {
                            stream.Seek(0, SeekOrigin.Begin);
                            sprite = new ASE(stream);

                            LogVerbose("  Format: Aseprite");
                        }
                    }
                }
                catch (System.IO.FileNotFoundException) {
                    Console.WriteLine("Could not find file " + filename);
                    Environment.Exit(1);
                }

                if (sprite == null) {
                    Console.WriteLine("Unrecognized file format for " + filename);
                    Environment.Exit(1);
                }

                sprites.Add(sprite);
            }

            return sprites;
        }

        private static Dictionary<string, SpriteGroup> SplitSpritesByGroups(List<Sprite> sprites, List<string> filenames) {
            Dictionary<string, SpriteGroup> groups = new Dictionary<string, SpriteGroup>();

            LogVerbose("Splitting sprites by groups...");

            for (int i = 0; i < sprites.Count; i++) {
                Sprite sprite = sprites[i];

                string outFilename;
                if (OutputFilename == "") {
                    outFilename = Path.GetFileNameWithoutExtension(filenames[i]);
                    outFilename += ".bin";
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

        static void PrintUsage() {
            Console.WriteLine("""
usage: makesprite -i | --input <file>... [-o | --output <path>]
       [--sheet-path <path>] [--max-sheet-width <size>] [--max-sheet-height <size>]
       [--keep-canvas-offsets] [--no-offsets] [--offset-x <amount>] [--offset-y <amount>]
       [-s | --split-groups] [--combine-sheets] [--frame-sort <mode>]
       [-f | --font] [-h | --help]

Options:
  -i, --input <file>...      A list of files to convert.
  -o, --output <file>        The name of the output file. If only converting a
                             single sprite, this option also defines the name
                             of the output spritesheets. However, if multiple
                             sprites are being converted, this option only
                             defines the name of the output spritesheets, and
                             the output sprites are named after the input file
                             names. When used with --split-groups, the output
                             filenames are suffixed with the names of the
                             groups.
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
  -s, --split-groups         Export a separate sprite for each group. The loop
                             frame layer, if any, is shared between the split
                             sprites. The spritesheets are split unless the
                             --combine-sheets option is passed.
  --combine-sheets           When used with --split-groups, all frames share
                             a spritesheet, instead of being split by groups.
  --frame-sort <mode>        How to sort the frames in the spritesheet.
                             Accepted options:
                               - none: Don't sort.
                               - area: Sort by the area of the frame.
                               - width: Sort by the width of the frame.
                               - height: Sort by the height of the frame.
                               - maxside: Sort by largest side of the frame.
                               - areaheight: Sort by area, then by height.
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
                case "--split-groups":
                case "-s":
                    SplitGroups = true;
                    return true;
                case "--combine-sheets":
                    CombineSheets = true;
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

        static bool ParseSingleArgOption(string option, string arg) {
            switch (option) {
                case "--output":
                case "-o":
                    OutputFilename = arg;
                    return true;
                case "--sheet-path":
                    ConverterOptions.SheetPath = arg;
                    return true;
                case "--max-sheet-width": {
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
                    int size = ParseNumericOption(option, arg);
                    if (size < 0) {
                        Console.WriteLine("Invalid argument for " + option);
                        Environment.Exit(1);
                        return false;
                    }
                    ConverterOptions.MaxSheetHeight = size;
                    return true;
                }
                case "--offset-x":
                    ConverterOptions.OffsetX = ParseNumericOption(option, arg);
                    return true;
                case "--offset-y":
                    ConverterOptions.OffsetY = ParseNumericOption(option, arg);
                    return true;
                case "--frame-sort": {
                    SpritePacker.SortMode sortMode;
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

        static bool ParseMultiArgOption(string option, List<string> args) {
            switch (option) {
                case "--input":
                case "-i":
                    if (args.Count == 0) {
                        return false;
                    }

                    InputFiles.Clear();

                    foreach (string arg in args) {
                        InputFiles.Add(arg);
                    }

                    return true;
                default:
                    Console.WriteLine("Unrecognized option " + option);
                    return false;
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
                    if (i + 1 == args.Count) {
                        Console.WriteLine("Missing argument");
                        return false;
                    }

                    string option = args[i];

                    args.RemoveAt(i);

                    if (ParseSingleArgOption(option, args[i])) {
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
