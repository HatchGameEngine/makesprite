using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RSDKv5 {
    public class Sprite {
        public List<Animation> Animations = new List<Animation>();
        public List<string> SpritesheetNames = new List<string>();
        public List<string> HitboxNames = new List<string>();

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
            public string Name;
            public ushort Speed = 1;
            public byte LoopIndex = 0;
            public byte RotationStyle = 0;

            public List<Frame> Frames = new List<Frame>();

            public Animation(string name) {
                Name = name;
            }

            public Frame AddFrame(int x, int y, int width, int height, int centerX, int centerY, int duration, int sheetIndex) {
                Frame frame = new Frame(x, y, width, height, centerX, centerY, duration, sheetIndex, Frames.Count + 1);
                Frames.Add(frame);
                return frame;
            }

            public void Write(BinaryWriter writer) {
                WriteString(writer, Name);
                writer.Write((ushort)Frames.Count);
                writer.Write(Speed);
                writer.Write(LoopIndex);
                writer.Write(RotationStyle);

                for (ushort i = 0; i < Frames.Count; i++) {
                    Frame frame = Frames[i];
                    frame.Write(writer);
                }
            }

            public class Frame {
                public List<Hitbox> Hitboxes = new List<Hitbox>();
                public byte SpritesheetIndex;
                public short Duration;
                public ushort ID;
                public ushort X;
                public ushort Y;
                public ushort Width;
                public ushort Height;
                public short CenterX;
                public short CenterY;

                public Frame(int x, int y, int width, int height, int centerX, int centerY, int duration, int sheetIndex, int id) {
                    SpritesheetIndex = (byte)sheetIndex;
                    Duration = (short)duration;
                    ID = (ushort)id;
                    X = (ushort)x;
                    Y = (ushort)y;
                    Width = (ushort)width;
                    Height = (ushort)height;
                    CenterX = (short)centerX;
                    CenterY = (short)centerY;
                }

                public void AddHitbox(int left, int top, int right, int bottom) {
                    Hitbox hitbox = new Hitbox(left, top, right, bottom);
                    Hitboxes.Add(hitbox);
                }

                public void Write(BinaryWriter writer) {
                    writer.Write(SpritesheetIndex);
                    writer.Write(Duration);
                    writer.Write(ID);
                    writer.Write(X);
                    writer.Write(Y);
                    writer.Write(Width);
                    writer.Write(Height);
                    writer.Write(CenterX);
                    writer.Write(CenterY);

                    for (int i = 0; i < Hitboxes.Count; i++) {
                        Hitbox hitbox = Hitboxes[i];
                        hitbox.Write(writer);
                    }
                }

                public class Hitbox {
                    public short Left;
                    public short Top;
                    public short Right;
                    public short Bottom;

                    public Hitbox(int left, int top, int right, int bottom) {
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
