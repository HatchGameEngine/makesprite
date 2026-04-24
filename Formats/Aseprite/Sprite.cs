namespace Aseprite {
    public class Sprite : makesprite.Sprite {
        public void AddLayer(Layer layer, int childLevel) {
            layer.ChildLevel = childLevel;

            for (int i = Layers.Count - 1; i >= 0; i--) {
                Aseprite.Sprite.Layer otherLayer = (Aseprite.Sprite.Layer)Layers[i];
                if (otherLayer.ChildLevel == childLevel - 1) {
                    otherLayer.AddChild(layer);
                    break;
                }
            }

            Layers.Add(layer);
        }

        public new class Layer : makesprite.Sprite.Layer {
            public int Flags;
            public int Type;
            public int ChildLevel = 0;

            public enum LayerTypes {
                Image = 0,
                Group = 1,
                Tilemap = 2
            }

            public enum FlagTypes {
                Visible = 1,
                Editable = 2,
                MovementLocked = 4,
                Background = 8,
                PreferLinkedCels = 16,
                Collapsed = 32,
                Reference = 64
            }

            public Layer(makesprite.Sprite owner, string name, int flags, int type, int blendMode) : base(owner, name, true, blendMode) {
                Flags = flags;
                Type = type;

                if (Type == (int)LayerTypes.Group) {
                    IsGroup = true;
                }
                if (IsGroup || (Flags & (int)FlagTypes.Visible) == 0) {
                    Visible = false;
                }
                if ((Flags & (int)FlagTypes.Background) != 0) {
                    IsBackground = true;
                }
            }
        }

        public new class Frame : makesprite.Sprite.Frame {
            public List<int> LinkedFrameIndices = new List<int>();

            public Frame(Aseprite.Sprite ase) : base((makesprite.Sprite)ase) {}
        }
    }
}
