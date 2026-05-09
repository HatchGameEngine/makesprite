# makesprite

makesprite is a tool that converts animated sprites into Hatch formats.

Written in C# 11.0. Requires .NET 9.

## Supported formats

### Input formats

#### Animation formats
- JSON sprite format
- Retro Engine (RSDKv5) animation format
- Aseprite file (.ase/.aseprite)
  - Not all features are supported. If something in an .ase is not preserved during conversion, assume it's unimplemented.
- PNG
  - APNG is not currently supported.
- GIF

#### Spritesheet formats
- PNG
- GIF

### Output formats

#### Animation formats
- JSON sprite format
- Retro Engine (RSDKv5) animation format

#### Spritesheet formats
- PNG

#### Palette formats
- Hatch palette (.hpal)

## Usage

Example usage:

```
makesprite -i idle.gif walk.gif jump.gif -o player.bin
makesprite -i player.ase --export-palette
makesprite -i font.json --sheet-path "fonts/" --font
```

### Options
- -i, --input: A list of files to convert.
- -o, --output: The name of the output. This option also defines the name of the output spritesheets. If splitting by files, this option only defines the name of the output spritesheets, and the output sprites are named after the input file names. If splitting by groups, the output sprites are named after the group names, prefixed by the name passed to this option.
- --format: The format of the output sprites. Accepted options:
  - rsdkv5: Export as a RSDKv5 sprite.
  - json: Export as JSON.
  The default is 'rsdkv5'.
- --sheet-path: The parent path to use for the spritesheet filenames.
- --max-sheet-width: The maximum width of a spritesheet.
- --max-sheet-height: The maximum height of a spritesheet.
- --keep-canvas-offsets: Preserve the offsets of the original canvas.
- --no-offsets: Don't define offsets for any frames.
- --offset-x: Offset all frames horizontally by the given amount.
- --offset-y: Offset all frames vertically by the given amount.
- --no-frame-trim: Don't trim frames.
- --keep-duplicate-frames: Don't merge duplicate frames in the spritesheet.
- -s, --split-by: How to split the input files. Accepted options:
  - none: Don't split.
  - files: Export one sprite for each file.
  - groups: Export one sprite for each group.
  The default is 'none'.
- --group-split-sheets: Split spritesheets by groups.
- --sequence: Treat the input files as a sequence of frames, rather than separate animations.
- --input-frame-rate: Define the frame rate of imported animations that use frame rate based durations. The default is 60 frames per second.
- --frame-rate: Define the frame rate of exported animations that use frame rate based durations. The default is 60 frames per second.
- --frame-sort: How to sort the frames in the spritesheet. Accepted options:
  - none: Don't sort.
  - area: Sort by the area of the frame.
  - width: Sort by the width of the frame.
  - height: Sort by the height of the frame.
  - maxside: Sort by largest side of the frame.
  - areaheight: Sort by area, then by height.
  The default is 'areaheight'.
- --export-palette: Export .hpal palettes.
- --ignore-palette-mismatch: Keep sprites palettized even if the frames have palettes that don't match. The spritesheets will use the palette of the first frame.
- --depalettize: Save spritesheets as RGBA.
- --no-sheets: Don't export spritesheets.
- --no-sprites: Don't export sprites.
- --font: Output a font sprite.
- --overwrite: Replace files that already exist.
- --verbose: Enable verbose output.
- -h, --help: Show the usage text and exit.

## Which format to use

### For importing or exporting

The following formats can be imported by or exported from makesprite as a sprite.

#### JSON sprite format

This format is based on the Retro Engine animation format, with a few improvements.

##### Pros
- Extensible.
- Can be parsed by any tool or engine that can read JSON.
- Can be directly modified with any text editor.

##### Cons
- Not supported by Hatch.
- Cannot be opened by the Animation Editor tool.
- No quick way to preview the animations.

#### Retro Engine (RSDKv5) animation format

This is the format used by the Retro Engine, which Hatch also supports.

##### Pros
- Supported by Hatch.
- Can be opened by the Animation Editor tool.

##### Cons
- Not extensible.
- Cannot represent all fields of the JSON sprite format.

### For importing

The following formats can be imported by makesprite as a sprite.

#### Aseprite file (.ase/.aseprite)

Multiple animations and hitboxes can be imported from an Aseprite file.

Tags are used to contain frames into their own animations. The name of the tag defines the name of the animation.

To define hitboxes for frames, create one layer for each hitbox, and draw rectangles on them to define their shapes. They don't have to be hidden. Their names must begin with `Hitbox:`, and the part after `Hitbox:` defines the name of the hitbox. For example, a layer named `Hitbox: Body` will define a hitbox named `Body`.

To define loop frames for animations, create a layer named `Loop Frame`. It doesn't have to be hidden. The non-empty frame of said layer within a tag will define the loop frame index for the corresponding animation. Only one layer in the file can be named `Loop Frame`.

##### Pros
- Supports truecolor and indexed color modes.
- Can define hitboxes.
- Can define loop frames.

##### Cons
- Not all features are supported by makesprite.

#### PNG

PNG images can be imported as a sprite. To import a sequence of PNGs as a single animation, use `--sequence`.

##### Pros
- Easy way to import a sequence of frames.

##### Cons
- APNG is not supported by makesprite.
- Can't define hitboxes.
- Can't define loop frames.

#### GIF

GIF images can be imported as a sprite. Each input GIF directly maps to its own animation.

##### Pros
- Easy way to import an animation.

##### Cons
- Can only be indexed.
- Can't define hitboxes.
- Can't define loop frames.

## Building

### Windows

Open the `makesprite.csproj` project in Visual Studio, and build it. Alternatively, run `dotnet build` or `dotnet publish`.

### Linux

Run `dotnet build` or `dotnet publish`.
