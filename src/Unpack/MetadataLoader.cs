using System.IO;
using XRayBuilderGUI.Unpack.Mobi;

namespace XRayBuilderGUI.Unpack
{
    public static class MetadataLoader
    {
        public static IMetadata Load(string file)
        {
            var fs = new FileStream(file, FileMode.Open, FileAccess.Read);

            return new Metadata(fs);
        }
    }
}
