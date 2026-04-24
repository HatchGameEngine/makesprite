using System;
using System.Globalization;
using System.IO;

namespace makesprite {
    public static class PathHelper {
        static CultureInfo cultureInfo = new CultureInfo("en-US");

        public static bool EndsWith(string path, string endsWith, bool ignoreCase) {
            if (path == "")
                return false;

            if (path.EndsWith(endsWith, ignoreCase, cultureInfo))
                return true;
            else if (path.EndsWith(endsWith + Path.DirectorySeparatorChar, ignoreCase, cultureInfo))
                return true;
            else if (path.EndsWith(endsWith + '\\', ignoreCase, cultureInfo))
                return true;

            return false;
        }

        public static bool GetSpritesFolder(string path, out string outPath) {
            if (path == "") {
                outPath = "";
                return false;
            }

            while (true) {
                System.IO.DirectoryInfo? parent = Directory.GetParent(path);
                if (parent == null) {
                    outPath = "";
                    return false;
                }

                path = parent.FullName;

                if (EndsInRootPath(path)) {
                    outPath = path;
                    return true;
                }
            }
        }

        public static bool EndsInRootPath(string path) {
            return EndsWith(path, "Sprites", true) || EndsWith(path, "Resources", true);
        }
    }
}
