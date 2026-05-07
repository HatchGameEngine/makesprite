using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RSDKv5 {
    public class Sprite {
        [JsonInclude]
        [JsonPropertyName("spritesheets")]
        public List<string> SpritesheetNames = new List<string>();

        [JsonInclude]
        [JsonPropertyName("animations")]
        public List<Animation> Animations = new List<Animation>();

        public List<string> HitboxNames = new List<string>();

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
                    0
                );

                sprite.AnimRanges.Add(animRange);
            }

            for (int h = 0; h < HitboxNames.Count; h++) {
                sprite.HitboxNames.Add(HitboxNames[h]);
            }

            return sprite;
        }

        public string SerializeAsJSON(JsonSerializerOptions serializerOptions) {
            JsonNode? root = JsonSerializer.SerializeToNode(this, serializerOptions);
            if (root == null) {
                return "";
            }

            JsonObject json = root.AsObject();
            json.Add("version", 1);
            json.Add("meta", new System.Text.Json.Nodes.JsonObject {
                ["exporter"] = "makesprite v1.0.0"
            });

            return root.ToJsonString(serializerOptions);
        }

        public static makesprite.Sprite DeserializeFromJSON(string jsonText, string filename) {
            Dictionary<string, object>? json = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonText);
            if (json == null) {
                throw new Exception("Could not parse JSON");
            }

            /*JsonElement GetElement(JsonElement element, string fieldName, JsonValueKind kind) {
                JsonElement val;

                if (!element.TryGetProperty(fieldName, out val)) {
                    throw new Exception("Expected \"" + fieldName + "\" to exist but didn't");
                }

                if (val.ValueKind != kind) {
                    throw new Exception("Expected \"" + fieldName + "\" to be " + kind + " but was " + val.ValueKind + " instead");
                }

                return val;
            }*/

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

            long GetOptionalInteger(JsonElement element, string fieldName, short defaultValue) {
                JsonElement val;

                long number = defaultValue;

                if (element.TryGetProperty(fieldName, out val) && !val.TryGetInt64(out number)) {
                    throw new Exception("Expected \"" + fieldName + "\" to be integer but was " + val.ValueKind + " instead");
                }

                return number;
            }

            JsonElement animations = (JsonElement)json["animations"];
            if (animations.ValueKind != JsonValueKind.Array) {
                throw new Exception("Expected \"animations\" to be array but was " + animations.ValueKind + " instead");
            }

            Sprite sprite = new Sprite();

            for (int a = 0; a < animations.GetArrayLength(); a++) {
                var animation = animations[a];

                string animationName = GetString(animation, "name");
                short speed = (short)GetOptionalInteger(animation, "speed", 1);
                byte loopFrame = (byte)GetOptionalInteger(animation, "loopFrame", 0);
                byte rotationStyle = (byte)GetOptionalInteger(animation, "rotationStyle", 0);

                RSDKv5.Sprite.Animation animEntry = new RSDKv5.Sprite.Animation(animationName);
                animEntry.Speed = (ushort)speed;
                animEntry.LoopFrame = loopFrame;
                animEntry.RotationStyle = rotationStyle;
                sprite.Animations.Add(animEntry);

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
                    short duration = (short)GetOptionalInteger(frame, "duration", 1);
                    byte spritesheetIndex = (byte)GetOptionalInteger(frame, "spritesheetIndex", 0);
                    int id = (int)GetOptionalInteger(frame, "id", 0);

                    RSDKv5.Sprite.Animation.Frame fr = animEntry.AddFrame(
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
                    var sheetPath = spritesheets[i];
                    if (sheetPath.ValueKind != JsonValueKind.String) {
                        throw new Exception("Expected spritesheet path to be string but was " + sheetPath.ValueKind + " instead");
                    }

                    sprite.SpritesheetNames.Add(sheetPath.ToString());
                }
            }

            return sprite.ToIntermediateSprite(filename);
        }

        public void Write(BinaryWriter writer) {
            ushort numAnimations = (ushort)Animations.Count;
            byte numSpritesheetNames = (byte)SpritesheetNames.Count;
            byte numHitboxNames = (byte)HitboxNames.Count;

            writer.Write((uint)0x00525053);

            int frameCount = 0;
            for (ushort i = 0; i < numAnimations; i++) {
                frameCount += Animations[i].Frames.Count;
            }
            writer.Write(frameCount);

            writer.Write(numSpritesheetNames);
            for (byte i = 0; i < numSpritesheetNames; i++) {
                WriteString(writer, SpritesheetNames[i]);
            }

            writer.Write(numHitboxNames);
            for (byte i = 0; i < numHitboxNames; i++) {
                WriteString(writer, HitboxNames[i]);
            }

            writer.Write(numAnimations);
            for (ushort i = 0; i < numAnimations; i++) {
                Animations[i].Write(writer);
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
            [JsonPropertyName("rotationStyle")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public byte RotationStyle = 0;

            [JsonInclude]
            [JsonPropertyName("frames")]
            public List<Frame> Frames = new List<Frame>();

            public int Framerate = 60;

            public Animation(string name) {
                Name = name;
            }

            public Frame AddFrame(int x, int y, int width, int height, int centerX, int centerY, int duration, int sheetIndex, int id) {
                Frame frame = new Frame(x, y, width, height, centerX, centerY, duration, sheetIndex, id);
                frame.Framerate = Framerate;
                Frames.Add(frame);
                return frame;
            }

            public ushort GetSpeed() {
                if (Speed == 0) {
                    return 1;
                }

                return Speed;
            }

            public void Write(BinaryWriter writer) {
                WriteString(writer, Name);
                writer.Write((ushort)Frames.Count);
                writer.Write(GetSpeed());
                writer.Write(LoopFrame);
                writer.Write(RotationStyle);

                for (ushort i = 0; i < Frames.Count; i++) {
                    Frame frame = Frames[i];
                    frame.Write(writer);
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
                public int Duration;

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

                public int Framerate = 60;

                public Frame(int x, int y, int width, int height, int offsetX, int offsetY, int duration, int spritesheetIndex, int id) {
                    X = (ushort)x;
                    Y = (ushort)y;
                    Width = (ushort)width;
                    Height = (ushort)height;
                    OffsetX = (short)offsetX;
                    OffsetY = (short)offsetY;
                    Duration = duration;
                    SpritesheetIndex = (byte)spritesheetIndex;
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
                    return (Duration * Framerate + 999) / 1000; // ceil
                }

                public int GetDurationInMilliseconds(int duration) {
                    float time = (float)duration / Framerate;
                    return (int)(time * 1000);
                }

                public void Write(BinaryWriter writer) {
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
                            hitbox.Write(writer);
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

                    public void Write(BinaryWriter writer) {
                        writer.Write(Left);
                        writer.Write(Top);
                        writer.Write(Right);
                        writer.Write(Bottom);
                    }
                }
            }
        }

        public static void WriteString(BinaryWriter writer, string text) {
            int length = text.Length;
            if (length > 255) {
                length = 255;
            }

            writer.Write((byte)length);
            writer.Write(new UTF8Encoding().GetBytes(text));
        }
    }
}
