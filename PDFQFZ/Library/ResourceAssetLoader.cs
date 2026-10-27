using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace PDFQFZ.Library
{
    internal static class ResourceAssetLoader
    {
        public static Bitmap LoadBitmap(string resourceName)
        {
            using (Stream stream = OpenResource(resourceName))
            {
                return new Bitmap(stream);
            }
        }

        public static Icon LoadIcon(string resourceName)
        {
            using (Stream stream = OpenResource(resourceName))
            {
                return new Icon(stream);
            }
        }

        private static Stream OpenResource(string resourceName)
        {
            Stream stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new InvalidOperationException(
                    "Embedded UI resource was not found: " + resourceName);
            }

            return stream;
        }
    }
}
