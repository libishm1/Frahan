using System;
using System.Drawing;
using System.Linq;
using System.Reflection;

namespace Frahan.GH.RubblePack
{
    // Minimal embedded-PNG loader for this sibling .gha (mirrors the main
    // plugin's IconProvider). Resources\*.png are embedded; load by file name.
    internal static class RubbleIconProvider
    {
        public static Bitmap Load(string fileName)
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
            if (name == null) return null;
            using (var stream = asm.GetManifestResourceStream(name))
            {
                if (stream == null) return null;
                using (var img = Image.FromStream(stream))
                    return new Bitmap(img);
            }
        }
    }
}
