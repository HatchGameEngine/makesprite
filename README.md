# makesprite

makesprite is a tool that converts animated sprites into Hatch formats.

## Supported formats

### Input formats

#### Animation formats
- RSDKv5 animation format
- JSON schema
- Aseprite v1.3 file format (.ase/.aseprite)
  - Not all features are supported. If something in an .ase is not preserved during conversion, assume it's unimplemented.

#### Spritesheet formats
- PNG image
- GIF animation

### Output formats

#### Animation formats
- RSDKv5 animation format
- JSON schema

#### Spritesheet formats
- PNG image

#### Palette formats
- Hatch palette (.hpal)

## Usage

Example usage:

```
makesprite -i idle.gif walk.gif jump.gif -o player.bin
makesprite -i player.ase --export-palette
makesprite -i font.ase --sheet-path "fonts/" -f
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
- --verbose: Verbose output.
- -h, --help: Show the usage text and exit.
