# Retro Engine v5 Sprite Format (with Hatch extensions)

Byte order is little-endian.

Fields in **bold** are different from the file format used by the Retro Engine.

## Types

- `uint8`: An unsigned 8-bit number.
- `uint16`: An unsigned 16-bit number.
- `uint32`: An unsigned 32-bit number.
- `int16`: A signed 16-bit number.
- `string`: A string. Not null-terminated.
  - `uint8`: The length of the string.
  - `uint8[n]`: The characters of the string.

## Header

- Magic number: `uint32`
  - Must be `SPR\0`, or `0x00525053`.
- Total frame count: `uint32`
  - Hatch ignores this field.
- Spritesheet count: `uint8`
- **List of spritesheet paths:** `string[]`
  - In RSDKv5, all spritesheet paths are relative to `Sprites/`. In Hatch, this may be relative to `Sprites/`, `Resources/`, or `./` - the latter means the paths are relative to the directory the sprite file is located.
- Hitbox count: `uint8`
- List of hitbox names: `string[]`
- Animation count: `uint16`
- List of animations: See the following.

## Animation

- Name: `string`
- Frame count: `uint16`
- Speed: `uint16`
- Loop frame index: `uint8`
- Rotation style: `uint8`. Must be one of the following:
  - 0: No rotation
  - 1: Full rotation
  - 2: Snap to 45 degrees
  - 3: Snap to 90 degrees
  - 4: Snap to 180 degrees
  - 5: Use extra frames in animation
    - This is not fully supported by Hatch.
- List of frames: See the following.

## Frame

- Spritesheet index: `byte`
- Duration: `uint16`
- ID: `uint16`
- X: `uint16`
- Y: `uint16`
- Width: `uint16`
- Height: `uint16`
- X offset: `int16`
- Y offset: `int16`
- List of hitboxes: See the following. The amount of hitboxes in all frames is defined in the header.

## Hitbox

- Left side: `int16`
- Top side: `int16`
- Right side: `int16`
- Bottom side: `int16`
