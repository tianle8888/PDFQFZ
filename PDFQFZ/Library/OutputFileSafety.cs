using System;
using System.IO;

namespace PDFQFZ.Library
{
    internal static class OutputFileSafety
    {
        public static string FindWritableOutputPath(string preferredPath)
        {
            if (string.IsNullOrWhiteSpace(preferredPath))
            {
                throw new ArgumentException(
                    "The output path cannot be empty.",
                    nameof(preferredPath));
            }

            string fullPath = Path.GetFullPath(preferredPath);
            if (IsWritable(fullPath))
            {
                return fullPath;
            }

            string directory = Path.GetDirectoryName(fullPath);
            string extension = Path.GetExtension(fullPath);
            string baseName = Path.GetFileNameWithoutExtension(fullPath);
            for (int index = 1; index < 10000; index++)
            {
                string candidate = Path.Combine(
                    directory,
                    baseName + "_" + index + extension);
                if (IsWritable(candidate))
                {
                    return candidate;
                }
            }

            throw new IOException(
                "No writable output filename is available in " + directory);
        }

        public static string CreateTemporaryPath(string finalPath)
        {
            string fullPath = Path.GetFullPath(finalPath);
            string directory = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileName(fullPath);
            return Path.Combine(
                directory,
                "." + fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        }

        public static void CommitTemporaryFile(
            string temporaryPath,
            ref string finalPath)
        {
            string selectedPath = FindWritableOutputPath(finalPath);
            if (File.Exists(selectedPath))
            {
                File.Replace(temporaryPath, selectedPath, null);
            }
            else
            {
                File.Move(temporaryPath, selectedPath);
            }

            finalPath = selectedPath;
        }

        public static void TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static bool IsWritable(string path)
        {
            try
            {
                using (new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None))
                {
                    return true;
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
