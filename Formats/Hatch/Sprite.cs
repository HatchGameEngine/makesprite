using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Hatch {
    public class Sprite {
        public const uint RSDKV5_FILE_MAGIC = 0x00525053;

        public const int BASE_FRAMERATE = 60;

        public enum AnimationDirection {
            Forward,
            Reverse,

            [JsonStringEnumMemberName("ping-pong")]
            PingPong,

            [JsonStringEnumMemberName("ping-pong-reverse")]
            PingPongReverse
        };

        public enum RotationStyle {
            None,
            Full,

            [JsonStringEnumMemberName("45-degrees")]
            Degrees45,

            [JsonStringEnumMemberName("90-degrees")]
            Degrees90,

            [JsonStringEnumMemberName("180-degrees")]
            Degrees180,

            [JsonStringEnumMemberName("static-frames")]
            StaticFrames
        };

        public List<Animation> Animations = new List<Animation>();
        public List<string> SpritesheetNames = new List<string>();
        public List<string> HitboxNames = new List<string>();

        public int Framerate = BASE_FRAMERATE;

        public void AddAnimation(Animation animation) {
            animation.Framerate = Framerate;
            Animations.Add(animation);
        }

        // Converts this sprite to a makesprite.Sprite.
        // That is, the one that Converter uses.
        public makesprite.Sprite ToIntermediateSprite(string filename) {
            makesprite.Sprite sprite = new makesprite.Sprite();
            sprite.ColorDepth = 8;

            string? directoryPath = Path.GetDirectoryName(filename);
            string parentPath = "";

            PathHelper.GetSpritesFolder(filename, out parentPath, true);

            // Load sprite sheets
            List<makesprite.ImageFile> sheetImages = new List<makesprite.ImageFile>();
            List<uint[]> sheetPixels = new List<uint[]>();

            for (int i = 0; i < SpritesheetNames.Count; i++) {
                string sheetPath = SpritesheetNames[i];
                if (sheetPath.StartsWith("./")) {
                    sheetPath = directoryPath + "/" + sheetPath.Substring(2);
                }
                else {
                    sheetPath = parentPath + "/" + sheetPath;
                }

                makesprite.Program.LogVerbose("Loading spritesheet " + sheetPath);

                try {
                    using (FileStream stream = new FileStream(sheetPath, FileMode.Open)) {
                        makesprite.ImageFile? imageFile = makesprite.ImageFile.Load(stream);
                        if (imageFile == null) {
                            throw new Exception("Could not load spritesheet " + sheetPath);
                        }

                        sheetImages.Add(imageFile);

                        if (i == 0) {
                            sprite.Palette = imageFile.GetFramePaletteARGB(0);
                            sprite.TransparentPaletteIndex = imageFile.TransparentPaletteIndex;
                        }

                        int colorDepth;
                        sheetPixels.Add(imageFile.GetFramePixels(0, out colorDepth));

                        if (colorDepth > 8) {
                            sprite.ColorDepth = 32;
                        }
                    }
                }
                catch (System.IO.FileNotFoundException) {
                    throw new Exception("Could not find spritesheet " + sheetPath);
                }
                catch (System.IO.DirectoryNotFoundException) {
                    throw new Exception("Could not find spritesheet " + sheetPath);
                }
            }

            // Add the base layer
            makesprite.Sprite.Layer layer = new makesprite.Sprite.Layer(sprite, filename);
            sprite.Layers.Add(layer);

            // Add the animations
            for (int a = 0; a < Animations.Count; a++) {
                Animation anim = Animations[a];

                int numFrames = sprite.Frames.Count;

                for (int f = 0; f < anim.Frames.Count; f++) {
                    Animation.Frame animFrame = anim.Frames[f];

                    makesprite.Sprite.Frame frame = new makesprite.Sprite.Frame(sprite, animFrame.Width, animFrame.Height);
                    frame.SheetX = animFrame.X;
                    frame.SheetY = animFrame.Y;
                    frame.Offsets = new Vector2(animFrame.OffsetX, animFrame.OffsetY);
                    frame.Duration = animFrame.Duration;
                    frame.ID = animFrame.ID;

                    if (animFrame.SpritesheetIndex >= sheetPixels.Count) {
                        throw new Exception("Invalid spritesheet index " + animFrame.SpritesheetIndex);
                    }

                    makesprite.ImageFile sheetImage = sheetImages[animFrame.SpritesheetIndex];
                    frame.PixelDataWidth = sheetImage.Width;
                    frame.PixelDataHeight = sheetImage.Height;
                    frame.PixelData.Add(sheetPixels[animFrame.SpritesheetIndex]);

                    if (animFrame.Hitboxes != null) {
                        for (int h = 0; h < animFrame.Hitboxes.Count; h++) {
                            Animation.Frame.Hitbox hitbox = animFrame.Hitboxes[h];
                            makesprite.Sprite.Hitbox box = new makesprite.Sprite.Hitbox(
                                HitboxNames[h], hitbox.Left, hitbox.Top, hitbox.Right, hitbox.Bottom
                            );
                            frame.Hitboxes.Add(box);
                        }
                    }

                    sprite.Frames.Add(frame);
                }

                makesprite.Sprite.AnimRange animRange = new makesprite.Sprite.AnimRange(
                    anim.Name,
                    numFrames, numFrames + (anim.Frames.Count - 1),
                    makesprite.Sprite.AnimationDirection.Forward
                );

                sprite.AnimRanges.Add(animRange);
            }

            for (int h = 0; h < HitboxNames.Count; h++) {
                sprite.HitboxNames.Add(HitboxNames[h]);
            }

            return sprite;
        }

        public static JsonSerializerOptions GetSerializerOptions() {
            static void serializerModifier(JsonTypeInfo typeInfo) {
                foreach (var property in typeInfo.Properties) {
                    if (typeInfo.Type == typeof(Animation) && property.Name == "rotationStyle") {
                        property.ShouldSerialize = static (obj, val) => (RotationStyle?)val != RotationStyle.Full;
                    }
                }
            }

            return new JsonSerializerOptions{
                WriteIndented = true,
                Converters = {
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
                },
                TypeInfoResolver = new DefaultJsonTypeInfoResolver {
                    Modifiers = {
                        serializerModifier
                    }
                }
            };
        }

        public string SerializeAsJSON(JsonSerializerOptions serializerOptions) {
            var json = new JsonObject();

            // Add version
            json.Add("version", 1);

            // Add metadata
            json.Add("meta", new System.Text.Json.Nodes.JsonObject {
                ["exporter"] = "makesprite v1.0.0"
            });

            // Add spritesheets
            JsonArray spritesheets = new JsonArray();
            for (int i = 0; i < SpritesheetNames.Count; i++) {
                spritesheets.Add(new System.Text.Json.Nodes.JsonObject {
                    ["path"] = SpritesheetNames[i],
                    ["type"] = "image/png"
                });
            }
            json.Add("spritesheets", spritesheets);

            // Add animations
            json.Add("animations", JsonSerializer.SerializeToNode(Animations, serializerOptions));

            // Convert to string
            return json.ToJsonString(serializerOptions);
        }

        public static makesprite.Sprite DeserializeFromJSON(string jsonText, string filename) {
            Dictionary<string, object>? json = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonText);
            if (json == null) {
                throw new Exception("Could not parse JSON");
            }

            string GetString(JsonElement element, string fieldName) {
                JsonElement val;

                if (!element.TryGetProperty(fieldName, out val)) {
                    throw new Exception("Expected \"" + fieldName + "\" to exist but didn't");
                }
                if (val.ValueKind != JsonValueKind.String) {
                    throw new Exception("Expected \"" + fieldName + "\" to be string but was " + val.ValueKind + " instead");
                }

                return val.ToString();
            }

            long GetInteger(JsonElement element, string fieldName) {
                JsonElement val;

                long number;

                if (!element.TryGetProperty(fieldName, out val)) {
                    throw new Exception("Expected \"" + fieldName + "\" to exist but didn't");
                }
                if (!val.TryGetInt64(out number)) {
                    throw new Exception("Expected \"" + fieldName + "\" to be integer but was " + val.ValueKind + " instead");
                }

                return number;
            }

            JsonElement? GetOptionalElement(JsonElement element, string fieldName, JsonValueKind kind) {
                JsonElement val;

                if (!element.TryGetProperty(fieldName, out val)) {
                    return null;
                }
                if (val.ValueKind != kind) {
                    throw new Exception("Expected \"" + fieldName + "\" to be " + kind + " but was " + val.ValueKind + " instead");
                }

                return val;
            }

            long GetOptionalInteger(JsonElement element, string fieldName, long defaultValue) {
                JsonElement val;

                long number = defaultValue;

                if (element.TryGetProperty(fieldName, out val) && !val.TryGetInt64(out number)) {
                    throw new Exception("Expected \"" + fieldName + "\" to be integer but was " + val.ValueKind + " instead");
                }

                return number;
            }

            float GetOptionalDecimal(JsonElement element, string fieldName, float defaultValue) {
                JsonElement val;

                float number = defaultValue;

                if (element.TryGetProperty(fieldName, out val) && !val.TryGetSingle(out number)) {
                    throw new Exception("Expected \"" + fieldName + "\" to be decimal but was " + val.ValueKind + " instead");
                }

                return number;
            }

            JsonElement animations = (JsonElement)json["animations"];
            if (animations.ValueKind != JsonValueKind.Array) {
                throw new Exception("Expected \"animations\" to be array but was " + animations.ValueKind + " instead");
            }

            Sprite sprite = new Sprite();

            float defaultFrameDuration = Animation.Frame.GetDurationInMilliseconds(1, BASE_FRAMERATE);

            for (int a = 0; a < animations.GetArrayLength(); a++) {
                var animation = animations[a];

                string animationName = GetString(animation, "name");
                short speed = (short)GetOptionalInteger(animation, "speed", 1);
                byte loopFrame = (byte)GetOptionalInteger(animation, "loopFrame", 0);
                byte rotationStyle = (byte)GetOptionalInteger(animation, "rotationStyle", (long)RotationStyle.None);

                Hatch.Sprite.Animation animEntry = new Hatch.Sprite.Animation(animationName);
                animEntry.Speed = (ushort)speed;
                animEntry.LoopFrame = loopFrame;
                animEntry.RotationStyle = (RotationStyle)rotationStyle;
                sprite.AddAnimation(animEntry);

                JsonElement? valFrames = GetOptionalElement(animation, "frames", JsonValueKind.Array);
                if (valFrames == null) {
                    continue;
                }

                JsonElement frames = (JsonElement)valFrames;

                for (int f = 0; f < frames.GetArrayLength(); f++) {
                    var frame = frames[f];

                    short x = (short)GetInteger(frame, "sheetX");
                    short y = (short)GetInteger(frame, "sheetY");
                    short width = (short)GetInteger(frame, "width");
                    short height = (short)GetInteger(frame, "height");
                    short offsetX = (short)GetOptionalInteger(frame, "offsetX", 0);
                    short offsetY = (short)GetOptionalInteger(frame, "offsetY", 0);
                    float duration = GetOptionalDecimal(frame, "duration", defaultFrameDuration);
                    byte spritesheetIndex = (byte)GetOptionalInteger(frame, "spritesheetIndex", 0);
                    int id = (int)GetOptionalInteger(frame, "id", 0);

                    Hatch.Sprite.Animation.Frame fr = animEntry.AddFrame(
                        x, y,
                        width, height,
                        offsetX, offsetY,
                        duration,
                        spritesheetIndex,
                        id
                    );

                    JsonElement? valHitboxes = GetOptionalElement(frame, "hitboxes", JsonValueKind.Array);
                    if (valHitboxes != null) {
                        JsonElement hitboxes = (JsonElement)valHitboxes;
                        for (int h = 0; h < hitboxes.GetArrayLength(); h++) {
                            var hitbox = hitboxes[h];

                            string hitboxName = GetString(hitbox, "name");
                            int left = (int)GetInteger(hitbox, "left");
                            int top = (int)GetInteger(hitbox, "top");
                            int right = (int)GetInteger(hitbox, "right");
                            int bottom = (int)GetInteger(hitbox, "bottom");

                            if (!sprite.HitboxNames.Contains(hitboxName)) {
                                sprite.HitboxNames.Add(hitboxName);
                            }

                            fr.AddHitbox(hitboxName, left, top, right, bottom);
                        }
                    }
                }
            }

            if (json.ContainsKey("spritesheets")) {
                JsonElement spritesheets = (JsonElement)json["spritesheets"];
                if (spritesheets.ValueKind != JsonValueKind.Array) {
                    throw new Exception("Expected \"spritesheets\" to be array but was " + spritesheets.ValueKind + " instead");
                }

                for (int i = 0; i < spritesheets.GetArrayLength(); i++) {
                    var sheetEntry = spritesheets[i];

                    string sheetPath = GetString(sheetEntry, "path");

                    sprite.SpritesheetNames.Add(sheetPath);
                }
            }

            return sprite.ToIntermediateSprite(filename);
        }

        public static bool IsValidRSDKv5File(BinaryReader reader) {
            uint magic = reader.ReadUInt32();
            return magic == RSDKV5_FILE_MAGIC;
        }

        public static bool IsValidRSDKv5File(Stream stream) {
            return IsValidRSDKv5File(new BinaryReader(stream));
        }

        public void ReadRSDKv5(Stream stream) {
            ReadRSDKv5(new BinaryReader(stream));
        }

        public void ReadRSDKv5(BinaryReader reader) {
            if (!IsValidRSDKv5File(reader)) {
                throw new Exception("Not a RSDKv5 sprite");
            }

            reader.ReadUInt32(); // Frame count. Not needed

            byte numSpritesheetNames = reader.ReadByte();
            for (int i = 0; i < numSpritesheetNames; i++) {
                SpritesheetNames.Add(ReadStringRSDKv5(reader));
            }

            byte numHitboxNames = reader.ReadByte();
            for (int i = 0; i < numHitboxNames; i++) {
                HitboxNames.Add(ReadStringRSDKv5(reader));
            }

            ushort numAnimations = reader.ReadUInt16();
            for (int i = 0; i < numAnimations; i++) {
                AddAnimation(Animation.ReadRSDKv5(this, reader));
            }
        }

        public void WriteRSDKv5(BinaryWriter writer) {
            ushort numAnimations = (ushort)Animations.Count;
            byte numSpritesheetNames = (byte)SpritesheetNames.Count;
            byte numHitboxNames = (byte)HitboxNames.Count;

            writer.Write(RSDKV5_FILE_MAGIC);

            int frameCount = 0;
            for (int i = 0; i < numAnimations; i++) {
                frameCount += Animations[i].Frames.Count;
            }
            writer.Write(frameCount);

            writer.Write(numSpritesheetNames);
            for (int i = 0; i < numSpritesheetNames; i++) {
                WriteStringRSDKv5(writer, SpritesheetNames[i]);
            }

            writer.Write(numHitboxNames);
            for (int i = 0; i < numHitboxNames; i++) {
                WriteStringRSDKv5(writer, HitboxNames[i]);
            }

            writer.Write(numAnimations);
            for (int i = 0; i < numAnimations; i++) {
                Animations[i].WriteRSDKv5(writer);
            }
        }

        public class Animation {
            [JsonInclude]
            [JsonPropertyName("name")]
            public string Name;

            [JsonInclude]
            [JsonPropertyName("speed")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public ushort Speed = 0;

            [JsonInclude]
            [JsonPropertyName("loopFrame")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public byte LoopFrame = 0;

            [JsonInclude]
            [JsonPropertyName("direction")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public AnimationDirection Direction = AnimationDirection.Forward;

            [JsonInclude]
            [JsonPropertyName("rotationStyle")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public RotationStyle RotationStyle = RotationStyle.Full;

            [JsonInclude]
            [JsonPropertyName("frames")]
            public List<Frame> Frames = new List<Frame>();

            public int Framerate = BASE_FRAMERATE;

            public Animation(string name) {
                Name = name;
            }

            public Frame AddFrame(int x, int y, int width, int height, int offsetX, int offsetY, float duration, int sheet, int id) {
                Frame frame = new Frame(this, x, y, width, height, offsetX, offsetY, duration, sheet, id);
                frame.Animation = this;
                Frames.Add(frame);
                return frame;
            }

            public ushort GetSpeed() {
                if (Speed == 0) {
                    return 1;
                }

                return Speed;
            }

            public static Animation ReadRSDKv5(Sprite sprite, BinaryReader reader) {
                string name = ReadStringRSDKv5(reader);

                Animation animation = new Animation(name);
                ushort frameCount = reader.ReadUInt16();
                animation.Speed = reader.ReadUInt16();
                animation.LoopFrame = reader.ReadByte();
                animation.RotationStyle = (RotationStyle)reader.ReadByte();

                for (ushort i = 0; i < frameCount; i++) {
                    animation.Frames.Add(Frame.ReadRSDKv5(sprite, animation, reader));
                }

                return animation;
            }

            public void WriteRSDKv5(BinaryWriter writer) {
                WriteStringRSDKv5(writer, Name);
                writer.Write((ushort)Frames.Count);
                writer.Write(GetSpeed());
                writer.Write(LoopFrame);
                writer.Write((byte)RotationStyle);

                for (ushort i = 0; i < Frames.Count; i++) {
                    Frame frame = Frames[i];
                    frame.WriteRSDKv5(writer);
                }
            }

            public class Frame {
                [JsonInclude]
                [JsonPropertyName("sheetX")]
                public ushort X;

                [JsonInclude]
                [JsonPropertyName("sheetY")]
                public ushort Y;

                [JsonInclude]
                [JsonPropertyName("width")]
                public ushort Width;

                [JsonInclude]
                [JsonPropertyName("height")]
                public ushort Height;

                [JsonInclude]
                [JsonPropertyName("offsetX")]
                public short OffsetX;

                [JsonInclude]
                [JsonPropertyName("offsetY")]
                public short OffsetY;

                [JsonInclude]
                [JsonPropertyName("duration")]
                public float Duration;

                [JsonInclude]
                [JsonPropertyName("spritesheetIndex")]
                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
                public byte SpritesheetIndex;

                [JsonInclude]
                [JsonPropertyName("id")]
                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
                public int ID;

                [JsonInclude]
                [JsonPropertyName("hitboxes")]
                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                public List<Hitbox>? Hitboxes = null;

                public Animation Animation;

                public Frame(Animation anim, int x, int y, int width, int height, int offsetX, int offsetY, float duration, int sheet, int id) {
                    Animation = anim;
                    X = (ushort)x;
                    Y = (ushort)y;
                    Width = (ushort)width;
                    Height = (ushort)height;
                    OffsetX = (short)offsetX;
                    OffsetY = (short)offsetY;
                    Duration = duration;
                    SpritesheetIndex = (byte)sheet;
                    ID = id;
                }

                public void AddHitbox(string name, int left, int top, int right, int bottom) {
                    Hitbox hitbox = new Hitbox(name, left, top, right, bottom);

                    if (Hitboxes == null) {
                        Hitboxes = new List<Hitbox>();
                    }

                    Hitboxes.Add(hitbox);
                }

                public int GetDurationInFrames() {
                    return (int)((Duration * Animation.Framerate + 999) / 1000); // ceil
                }

                public static float GetDurationInMilliseconds(int duration, int framerate) {
                    return ((float)duration / framerate) * 1000;
                }

                public static Frame ReadRSDKv5(Sprite sprite, Animation anim, BinaryReader reader) {
                    byte spritesheetIndex = reader.ReadByte();
                    float duration = GetDurationInMilliseconds(reader.ReadUInt16(), sprite.Framerate);
                    ushort id = reader.ReadUInt16();
                    ushort x = reader.ReadUInt16();
                    ushort y = reader.ReadUInt16();
                    ushort width = reader.ReadUInt16();
                    ushort height = reader.ReadUInt16();
                    short offsetX = reader.ReadInt16();
                    short offsetY = reader.ReadInt16();

                    Frame frame = new Frame(anim, x, y, width, height, offsetX, offsetY, duration, spritesheetIndex, id);

                    if (sprite.HitboxNames.Count > 0) {
                        frame.Hitboxes = new List<Hitbox>();

                        for (int i = 0; i < sprite.HitboxNames.Count; i++) {
                            frame.Hitboxes.Add(Hitbox.ReadRSDKv5(sprite.HitboxNames[i], reader));
                        }
                    }

                    return frame;
                }

                public void WriteRSDKv5(BinaryWriter writer) {
                    writer.Write(SpritesheetIndex);
                    writer.Write((short)GetDurationInFrames());
                    writer.Write((ushort)ID);
                    writer.Write(X);
                    writer.Write(Y);
                    writer.Write(Width);
                    writer.Write(Height);
                    writer.Write(OffsetX);
                    writer.Write(OffsetY);

                    if (Hitboxes != null) {
                        for (int i = 0; i < Hitboxes.Count; i++) {
                            Hitbox hitbox = Hitboxes[i];
                            hitbox.WriteRSDKv5(writer);
                        }
                    }
                }

                public class Hitbox {
                    [JsonInclude]
                    [JsonPropertyName("name")]
                    public string Name;

                    [JsonInclude]
                    [JsonPropertyName("left")]
                    public short Left;

                    [JsonInclude]
                    [JsonPropertyName("top")]
                    public short Top;

                    [JsonInclude]
                    [JsonPropertyName("right")]
                    public short Right;

                    [JsonInclude]
                    [JsonPropertyName("bottom")]
                    public short Bottom;

                    public Hitbox(string name, int left, int top, int right, int bottom) {
                        Name = name;
                        Left = (short)left;
                        Top = (short)top;
                        Right = (short)right;
                        Bottom = (short)bottom;
                    }

                    public static Hitbox ReadRSDKv5(string name, BinaryReader reader) {
                        short left = reader.ReadInt16();
                        short top = reader.ReadInt16();
                        short right = reader.ReadInt16();
                        short bottom = reader.ReadInt16();

                        return new Hitbox(name, left, top, right, bottom);
                    }

                    public void WriteRSDKv5(BinaryWriter writer) {
                        writer.Write(Left);
                        writer.Write(Top);
                        writer.Write(Right);
                        writer.Write(Bottom);
                    }
                }
            }
        }

        public static string ReadStringRSDKv5(BinaryReader reader) {
            string text = "";
            byte length = reader.ReadByte();

            for (int i = 0; i < length; i++) {
                text += (char)reader.ReadByte();
            }

            return text;
        }

        public static void WriteStringRSDKv5(BinaryWriter writer, string text) {
            int length = text.Length;
            if (length > 255) {
                length = 255;
            }

            writer.Write((byte)length);
            writer.Write(new UTF8Encoding().GetBytes(text));
        }
    }
}
