# makesprite

makesprite is a tool that converts animated sprites into Hatch formats.

## Supported formats

### Input formats

- Aseprite v1.3 file format (.ase/.aseprite)
  - Not all features are supported. If something in an .ase is not preserved during conversion, assume it's unimplemented.

### Output formats

- RSDKv5 animation format
- PNG image
- Hatch palette (.hpal)

## Usage

Example usage:

```
makesprite -i player.ase
makesprite -i main_character.ase -o player.bin
makesprite -i font.ase --sheet-path "fonts/" -f
```

### Options
- -i, --input: A list of files to convert.
- -o, --output: The name of the output file. If converting a single file, this option also defines the name of the output spritesheets. If converting multiple files, this option only defines the name of the output spritesheets, and the output sprites are named after the input file names.
- --sheet-path: The parent path to use for the spritesheet filenames.
- --max-sheet-width: The maximum width of a spritesheet.
- --max-sheet-height: The maximum height of a spritesheet.
- --keep-canvas-offsets: Preserve the offsets of the original canvas.
- --no-offsets: Do not define offsets for any frames.
- --offset-x: Offset all frames horizontally by the given amount.
- --offset-y: Offset all frames vertically by the given amount.
- -s, --split-groups: Export a separate sprite for each group. The loop frame layer, if any, is shared between the split sprites. The spritesheets are split unless the --combine-sheets option is passed.
- --combine-sheets: When used with --split-groups, all frames share a spritesheet, instead of being split by groups.
- --frame-sort: How to sort the frames in the spritesheet. Accepted options:
  - none: Don't sort.
  - area: Sort by the area of the frame.
  - width: Sort by the width of the frame.
  - height: Sort by the height of the frame.
  - maxside: Sort by largest side of the frame.
  - areaheight: Sort by area, then by height.
- --export-palette: Export .hpal palettes.
- --no-sheets: Don't export spritesheets.
- --no-sprites: Don't export sprites.
- -f, --font: Output a font sprite.
- -h, --help: Show the usage text and exit.
- --verbose: Verbose output.
