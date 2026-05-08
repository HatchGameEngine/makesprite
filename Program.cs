using System.Text;
using System.Text.Json;

namespace makesprite {
    class Program {
        private static List<string> InputFiles = new List<string>();

        private static string OutputFilename = "";

        public static Converter.Options ConverterOptions = new Converter.Options();

        private static bool GroupSplitSheets = false;
        private static bool Depalettize = false;
        private static bool IgnorePaletteMismatch = false;

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

            switch (ConverterOptions.OutputFormat) {
            case Converter.SpriteFormat.RSDKv5:
                ConverterOptions.SpriteExtension = ".bin";
                break;
            case Converter.SpriteFormat.JSON:
                ConverterOptions.SpriteExtension = ".json";
                break;
            }

            List<Sprite> sprites = ReadInputFiles();

            Converter converter = new Converter();
            converter.CurrentOptions = ConverterOptions;

            string outFilename = OutputFilename;
            if (outFilename == "") {
                outFilename = Path.GetFileNameWithoutExtension(InputFiles[0]);
                outFilename += ConverterOptions.SpriteExtension;
            }

            try {
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
            }
            catch (InvalidOperationException ex) {
                Console.WriteLine(ex.Message);
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
                string format = "";

                LogVerbose("Reading file " + filename);

                try {
                    sprite = DetectAndReadSpriteFile(filename, out format);
                }
                catch (System.IO.FileNotFoundException) {
                    Console.WriteLine("Could not find file " + filename);
                    Environment.Exit(1);
                }

                if (sprite == null) {
                    Console.WriteLine("Unrecognized file format for " + filename);
                    Environment.Exit(1);
                }

                LogVerbose("Format: " + format);

                if (Depalettize) {
                    LogVerbose("Depalettizing " + filename);

                    sprite.MakeNonPalettized();
                }

                sprites.Add(sprite);
            }

            return sprites;
        }

        private static Sprite? DetectAndReadSpriteFile(string filename, out string format) {
            format = "unknown";

            using (FileStream stream = new FileStream(filename, FileMode.Open)) {
                // Detect .aseprite file
                if (Aseprite.File.IsValid(stream)) {
                    format = "Aseprite file";

                    stream.Seek(0, SeekOrigin.Begin);

                    Aseprite.File file = new Aseprite.File();
                    return file.Read(stream);
                }
                stream.Seek(0, SeekOrigin.Begin);

                // Detect RSDKv5 sprite
                if (Hatch.Sprite.IsValidRSDKv5File(stream)) {
                    format = "RSDKv5 sprite";

                    stream.Seek(0, SeekOrigin.Begin);

                    Hatch.Sprite sprite = new Hatch.Sprite();
                    sprite.Framerate = ConverterOptions.Framerate;
                    sprite.ReadRSDKv5(stream);
                    return sprite.ToIntermediateSprite(filename);
                }
                stream.Seek(0, SeekOrigin.Begin);

                // Detect GIF file
                if (GIF.File.IsValid(stream)) {
                    format = "GIF";

                    stream.Seek(0, SeekOrigin.Begin);

                    GIF.File file = new GIF.File(stream);
                    return new GIF.Sprite(file, Path.GetFileNameWithoutExtension(filename), IgnorePaletteMismatch);
                }
                stream.Seek(0, SeekOrigin.Begin);

                // Detect PNG file
                if (PNG.File.IsValid(stream)) {
                    format = "PNG";

                    stream.Seek(0, SeekOrigin.Begin);

                    PNG.File file = new PNG.File(stream);
                    return new PNG.Sprite(file, Path.GetFileNameWithoutExtension(filename));
                }
                stream.Seek(0, SeekOrigin.Begin);

                // Detect JSON file
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8)) {
                    string json = reader.ReadToEnd();

                    if (IsJSON(json)) {
                        format = "JSON";

                        return Hatch.Sprite.DeserializeFromJSON(json, filename);
                    }
                }
            }

            return null;
        }

        private static bool IsJSON(string json) {
            try {
                using (JsonDocument.Parse(json)) {
                    return true;
                }
            }
            catch (JsonException) {
                return false;
            }
        }

        private static Dictionary<string, SpriteGroup> SplitSpritesByGroups(List<Sprite> sprites, List<string> filenames) {
            Dictionary<string, SpriteGroup> groups = new Dictionary<string, SpriteGroup>();

            LogVerbose("Splitting sprites by groups...");

            for (int i = 0; i < sprites.Count; i++) {
                Sprite sprite = sprites[i];

                string outFilename;
                if (OutputFilename == "") {
                    outFilename = Path.GetFileNameWithoutExtension(filenames[i]);
                    outFilename += ConverterOptions.SpriteExtension;
                }
                else {
                    outFilename = OutputFilename;
                }

                string baseFilename = Path.GetFileNameWithoutExtension(outFilename);
                string fileExtension = Path.GetExtension(outFilename);

                // Now split by groups
                for (int l = 0; l < sprite.Layers.Count; l++) {
                    Sprite.Layer layer = sprite.Layers[l];
                    if (!layer.IsGroup || layer.Parent != null) {
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

                        groupSprite.Frames.Add(frame);
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
usage: makesprite -i | --input <file>... [-o | --output <path>] [--format <type>]
       [--font] [--sheet-path <path>] [--max-sheet-width <size>] [--max-sheet-height <size>]
       [--keep-canvas-offsets] [--no-offsets] [--offset-x <amount>] [--offset-y <amount>]
       [--no-frame-trim] [--keep-duplicate-frames] [-s | --split-by] [--group-split-sheets]
       [--sequence] [--import-frame-rate <fps>] [--frame-rate <fps>] [--frame-sort <mode>]
       [--export-palette] [--ignore-palette-mismatch] [--depalettize] [--no-sheets]
       [--no-sprites] [--overwrite] [--verbose] [-h | --help]

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
  --format <type>            The format of the output sprites.
                             Accepted options:
                               - rsdkv5: Export as a RSDKv5 sprite.
                               - json: Export as JSON.
                             The default is 'rsdkv5'.
  --font                     Output a font sprite.
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
  --no-frame-trim            Don't trim frames.
  --keep-duplicate-frames    Don't merge duplicate frames in the spritesheet.
  -s, --split-by             How to split the input files.
                             Accepted options:
                               - none: Don't split.
                               - files: Export one sprite for each file.
                               - groups: Export one sprite for each group.
                             The default is 'none'.
  --group-split-sheets       Split spritesheets by groups.
  --sequence                 Treat the input files as a sequence of frames,
                             rather than separate animations.
  --import-frame-rate <fps>  Define the frame rate of imported animations that
                             use frame rate based durations.
                             The default is 60 frames per second.
  --frame-rate <fps>         Define the frame rate of exported animations that
                             use frame rate based durations.
                             The default is 60 frames per second.
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
  --depalettize              Save spritesheets as RGBA.
  --no-sheets                Don't export spritesheets.
  --no-sprites               Don't export sprites.
  --overwrite                Replace files that already exist.
  --verbose                  Enable verbose output.
  -h, --help                 Show this message and exit.
""");
        }

        static bool ParseNoArgOption(string option) {
            switch (option) {
                case "--font":
                    ConverterOptions.IsFont = true;
                    return true;
                case "--no-offsets":
                    ConverterOptions.NoOffsets = true;
                    return true;
                case "--keep-canvas-offsets":
                    ConverterOptions.KeepCanvasOffsets = true;
                    return true;
                case "--no-frame-trim":
                    ConverterOptions.TrimFrames = false;
                    return true;
                case "--keep-duplicate-frames":
                    ConverterOptions.MergeDuplicateFrames = false;
                    return true;
                case "--group-split-sheets":
                    GroupSplitSheets = true;
                    return true;
                case "--sequence":
                    ConverterOptions.Sequence = true;
                    return true;
                case "--export-palette":
                    ConverterOptions.SavePalettes = true;
                    return true;
                case "--ignore-palette-mismatch":
                    IgnorePaletteMismatch = true;
                    return true;
                case "--depalettize":
                    Depalettize = true;
                    return true;
                case "--no-sheets":
                    ConverterOptions.SaveSheets = false;
                    return true;
                case "--no-sprites":
                    ConverterOptions.SaveSprites = false;
                    return true;
                case "--verbose":
                    ConverterOptions.Verbose = true;
                    return true;
                case "--overwrite":
                    ConverterOptions.CanOverwrite = true;
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
                case "--format": {
                    Converter.SpriteFormat format;

                    string arg = GetNextArg(args, index);
                    switch (arg.ToLower()) {
                    case "rsdkv5":
                        format = Converter.SpriteFormat.RSDKv5;
                        break;
                    case "json":
                        format = Converter.SpriteFormat.JSON;
                        break;
                    default:
                        Console.WriteLine("Invalid argument for " + option);
                        Environment.Exit(1);
                        return false;
                    }

                    ConverterOptions.OutputFormat = format;
                    ConverterOptions.UseRDSKCompatibleSheetPaths = format == Converter.SpriteFormat.RSDKv5;

                    return true;
                }
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
                case "--import-frame-rate": {
                    string arg = GetNextArg(args, index);
                    int framerate = ParseNumericOption(option, arg);
                    if (framerate < 1) {
                        Console.WriteLine("Invalid argument for " + option);
                        Environment.Exit(1);
                        return false;
                    }
                    ConverterOptions.Framerate = framerate;
                    return true;
                }
                case "--frame-rate": {
                    string arg = GetNextArg(args, index);
                    int framerate = ParseNumericOption(option, arg);
                    if (framerate < 1) {
                        Console.WriteLine("Invalid argument for " + option);
                        Environment.Exit(1);
                        return false;
                    }
                    ConverterOptions.ExportFramerate = framerate;
                    return true;
                }
                case "--frame-sort": {
                    SpritePacker.SortMode sortMode;

                    string arg = GetNextArg(args, index);
                    switch (arg.ToLower()) {
                    case "none":
                        sortMode = SpritePacker.SortMode.Index;
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
