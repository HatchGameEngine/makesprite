namespace makesprite {
    public class SpritePacker {
        public const int MIN_SHEET_WIDTH = 128;
        public const int MAX_SHEET_WIDTH = 16384;
        public const int MIN_SHEET_HEIGHT = 128;
        public const int MAX_SHEET_HEIGHT = 16384;

        public enum SortMode {
            ID,
            Area,
            Width,
            Height,
            MaxSide,
            AreaAndHeight
        }

        public class Rect {
            public int X;
            public int Y;
            public int Width;
            public int Height;

            public Rect(int x, int y, int width, int height) {
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }
        }

        public class Box {
            public int ID;
            public int PackageID;
            public int OffX;
            public int OffY;
            public Rect Rect;

            public Box(int id, Rect rect) {
                ID = id;
                Rect = rect;
            }
        }

        public class Package {
            public int Width;
            public int Height;
            public List<Box> Boxes = new List<Box>();
        }

        public class PackageNode {
            public Box? Box;
            public bool Used = false;
            public PackageNode? Right = null;
            public PackageNode? Bottom = null;
        }

        private int DefaultWidth = 64;
        private int DefaultHeight = 64;
        private int MaxWidth = 2048;
        private int MaxHeight = 2048;
        private SortMode SortBy = SortMode.AreaAndHeight;
        private bool DoResize = true;

        private static int LastWidth = -1;
        private static int LastHeight = -1;

        public SpritePacker(int defaultWidth, int defaultHeight, int maxWidth, int maxHeight, bool doResize, SortMode sortMode) {
            if (maxWidth > MAX_SHEET_WIDTH || maxHeight > MAX_SHEET_HEIGHT ||
                maxWidth < MIN_SHEET_WIDTH || maxHeight < MIN_SHEET_HEIGHT) {
                string message = "";
                bool showWarning = false;

                if (maxWidth != LastWidth || maxHeight != LastHeight) {
                    message = String.Format(
                        "Maximum spritesheet size of {0:D}x{1:D} was not valid. ",
                        maxWidth,
                        maxHeight
                    );

                    showWarning = true;

                    LastWidth = maxWidth;
                    LastHeight = maxHeight;
                }

                if (maxWidth > MAX_SHEET_WIDTH) {
                    maxWidth = MAX_SHEET_WIDTH;
                }
                else if (maxWidth < MIN_SHEET_WIDTH) {
                    maxWidth = MIN_SHEET_WIDTH;
                }

                if (maxHeight > MAX_SHEET_HEIGHT) {
                    maxHeight = MAX_SHEET_HEIGHT;
                }
                else if (maxHeight < MIN_SHEET_HEIGHT) {
                    maxHeight = MIN_SHEET_HEIGHT;
                }

                if (showWarning) {
                    message += String.Format(
                        "Using {0:D}x{1:D} instead.",
                        maxWidth,
                        maxHeight
                    );

                    Program.Warning(message);
                }
            }

            DefaultWidth = defaultWidth;
            DefaultHeight = defaultHeight;
            MaxWidth = maxWidth;
            MaxHeight = maxHeight;
            DoResize = doResize;
            SortBy = sortMode;
        }

        static PackageNode? FindNode(PackageNode? root, int width, int height) {
            if (root == null || root.Box == null) {
                return null;
            }

            if (root.Used) {
                PackageNode? space;

                space = FindNode(root.Right, width, height);
                if (space != null) {
                    return space;
                }

                space = FindNode(root.Bottom, width, height);
                if (space != null) {
                    return space;
                }
            }
            else if (width <= root.Box.Rect.Width && height <= root.Box.Rect.Height) {
                return root;
            }

            return null;
        }
        static Box? InsertNode(PackageNode? parent, int w, int h) {
            if (parent == null || parent.Box == null || parent.Box.Rect == null) {
                return null;
            }

            parent.Used = true;

            parent.Right = new PackageNode();
            parent.Right.Box = new Box(-1, new Rect(parent.Box.Rect.X + w, parent.Box.Rect.Y, parent.Box.Rect.Width - w, h));

            parent.Bottom = new PackageNode();
            parent.Bottom.Box = new Box(-1, new Rect(parent.Box.Rect.X, parent.Box.Rect.Y + h, parent.Box.Rect.Width, parent.Box.Rect.Height - h));

            return parent.Box;
        }

        public static List<Box> SortBoxesByID(List<Box> boxes) {
            return boxes.OrderBy(o => o.ID).ToList();
        }
        public static List<Box> SortBoxesByArea(List<Box> boxes) {
            return boxes.OrderBy(o => -(o.Rect.Width * o.Rect.Height)).ToList();
        }
        public static List<Box> SortBoxesByWidth(List<Box> boxes) {
            return boxes.OrderBy(o => -(o.Rect.Width)).ToList();
        }
        public static List<Box> SortBoxesByHeight(List<Box> boxes) {
            return boxes.OrderBy(o => -(o.Rect.Height)).ToList();
        }
        public static List<Box> SortBoxesByMaxSide(List<Box> boxes) {
            return boxes.OrderBy(o => -(o.Rect.Width > o.Rect.Height ? o.Rect.Width : o.Rect.Height)).ToList();
        }

        public List<Package>? PackBoxes(ref List<Box> boxe) {
            List<Box> boxes;

            switch (SortBy) {
            case SortMode.ID:
                boxes = SortBoxesByID(boxe);
                break;
            case SortMode.Area:
                boxes = SortBoxesByArea(boxe);
                break;
            case SortMode.Width:
                boxes = SortBoxesByWidth(boxe);
                break;
            case SortMode.Height:
                boxes = SortBoxesByHeight(boxe);
                break;
            case SortMode.MaxSide:
                boxes = SortBoxesByMaxSide(boxe);
                break;
            case SortMode.AreaAndHeight:
                boxes = SortBoxesByArea(boxe);
                boxes = SortBoxesByHeight(boxe);
                break;
            default:
                boxes = boxe;
                break;
            }

            List<Package> packageList = new List<Package>();

            Package package = new Package();
            package.Width = DefaultWidth;
            package.Height = DefaultHeight;

            PackageNode root = new PackageNode();
            root.Box = new Box(-1, new Rect(0, 0, package.Width, package.Height));

            packageList.Add(package);

            bool contin = false;
            bool increaseSide = MaxWidth > MaxHeight;
            int startBox = 0;

            while (true) {
                contin = false;

                int i = startBox;
                for (; i < boxes.Count; i++) {
                    PackageNode? node = FindNode(root, boxes[i].Rect.Width, boxes[i].Rect.Height);
                    if (node != null) {
                        Box? where = InsertNode(node, boxes[i].Rect.Width, boxes[i].Rect.Height);
                        if (where != null) {
                            boxes[i].PackageID = packageList.Count - 1;
                            boxes[i].Rect.X = where.Rect.X;
                            boxes[i].Rect.Y = where.Rect.Y;
                        }
                    }
                    else {
                        if (DoResize) {
                            if (package.Width >= MaxWidth && package.Height >= MaxHeight) {
                                package = new Package();
                                package.Width = DefaultWidth;
                                package.Height = DefaultHeight;
                                packageList.Add(package);

                                root = new PackageNode();
                                root.Box = new Box(-1, new Rect(0, 0, package.Width, package.Height));

                                contin = false;
                                startBox = i;
                                continue;
                            }

                            if (increaseSide) {
                                package.Width <<= 1;
                            }
                            else {
                                package.Height <<= 1;
                            }
                            increaseSide = !increaseSide;

                            if (package.Width == 0) {
                                break;
                            }

                            // resize current package
                            contin = true;
                            root = new PackageNode();
                            root.Box = new Box(-1, new Rect(0, 0, package.Width, package.Height));
                            break;
                        }

                        return null;
                    }
                }
                if (contin) {
                    continue;
                }

                break;
            }

            return packageList;
        }
    }
}
