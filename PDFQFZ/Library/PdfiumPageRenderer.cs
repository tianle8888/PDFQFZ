using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace PDFQFZ.Library
{
    /// <summary>
    /// Small PDF page renderer backed directly by the PDFium C API.
    /// Rendering is serialized because a single PDFium library instance is
    /// process-wide and is not exposed as thread-safe by this application.
    /// </summary>
    internal sealed class PdfiumPageRenderer : IDisposable
    {
        private static readonly object PdfiumSync = new object();
        private static bool pdfiumInitialized;
        private static IntPtr pdfiumModule;

        private IntPtr document;
        private readonly int pageCount;
        private bool disposed;

        private PdfiumPageRenderer(IntPtr documentHandle, int pages)
        {
            document = documentHandle;
            pageCount = pages;
        }

        public int PageCount
        {
            get { return pageCount; }
        }

        public static PdfiumPageRenderer Open(string path, string password = "")
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("PDF path cannot be empty.", nameof(path));
            }

            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("PDF file was not found.", fullPath);
            }

            EnsurePdfiumInitialized();

            lock (PdfiumSync)
            {
                IntPtr documentHandle = PdfiumNative.FPDF_LoadDocument(
                    fullPath,
                    string.IsNullOrEmpty(password) ? null : password);

                if (documentHandle == IntPtr.Zero)
                {
                    uint errorCode = PdfiumNative.FPDF_GetLastError();
                    throw new InvalidOperationException(
                        "PDFium could not open the document. Error code: " + errorCode);
                }

                int pages = PdfiumNative.FPDF_GetPageCount(documentHandle);
                if (pages <= 0)
                {
                    PdfiumNative.FPDF_CloseDocument(documentHandle);
                    throw new InvalidOperationException("The PDF does not contain any pages.");
                }

                return new PdfiumPageRenderer(documentHandle, pages);
            }
        }

        public Bitmap GetPageImage(int pageIndex, int dpi)
        {
            RectangleF pageBounds;
            return GetPageImage(pageIndex, dpi, out pageBounds);
        }

        public Bitmap GetPageImage(int pageIndex, int dpi, out RectangleF pageBounds)
        {
            if (pageIndex < 0 || pageIndex >= pageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pageIndex));
            }
            if (dpi <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dpi));
            }

            pageBounds = RectangleF.Empty;
            lock (PdfiumSync)
            {
                ThrowIfDisposed();

                IntPtr page = PdfiumNative.FPDF_LoadPage(document, pageIndex);
                if (page == IntPtr.Zero)
                {
                    throw new InvalidOperationException("PDFium could not load page " + (pageIndex + 1) + ".");
                }

                IntPtr bitmap = IntPtr.Zero;
                IntPtr pixels = IntPtr.Zero;
                try
                {
                    float pageWidth = PdfiumNative.FPDF_GetPageWidthF(page);
                    float pageHeight = PdfiumNative.FPDF_GetPageHeightF(page);
                    int rotation = PdfiumNative.FPDFPage_GetRotation(page);
                    if (rotation < 0 || rotation > 3)
                    {
                        rotation = 0;
                    }

                    bool swapDimensions = (rotation & 1) == 1;
                    float renderedWidth = swapDimensions ? pageHeight : pageWidth;
                    float renderedHeight = swapDimensions ? pageWidth : pageHeight;
                    int pixelWidth = ToPixels(renderedWidth, dpi);
                    int pixelHeight = ToPixels(renderedHeight, dpi);
                    int stride = checked(pixelWidth * 4);
                    int byteCount = checked(stride * pixelHeight);
                    pixels = Marshal.AllocHGlobal(byteCount);
                    pageBounds = new RectangleF(0, 0, renderedWidth, renderedHeight);

                    bitmap = PdfiumNative.FPDFBitmap_CreateEx(
                        pixelWidth,
                        pixelHeight,
                        PdfiumNative.FPDFBitmapFormat.BGRA,
                        pixels,
                        stride);
                    if (bitmap == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("PDFium could not create a page bitmap.");
                    }

                    PdfiumNative.FPDFBitmap_FillRect(
                        bitmap,
                        0,
                        0,
                        pixelWidth,
                        pixelHeight,
                        0xFFFFFFFF);

                    const int renderFlags =
                        PdfiumNative.FPDF_ANNOT |
                        PdfiumNative.FPDF_PRINTING;
                    PdfiumNative.FPDF_RenderPageBitmap(
                        bitmap,
                        page,
                        0,
                        0,
                        pixelWidth,
                        pixelHeight,
                        rotation,
                        renderFlags);

                    byte[] pixelBytes = new byte[byteCount];
                    Marshal.Copy(pixels, pixelBytes, 0, byteCount);
                    Bitmap copy = new Bitmap(
                        pixelWidth,
                        pixelHeight,
                        PixelFormat.Format32bppArgb);
                    try
                    {
                        BitmapData targetData = copy.LockBits(
                            new Rectangle(0, 0, pixelWidth, pixelHeight),
                            ImageLockMode.WriteOnly,
                            PixelFormat.Format32bppArgb);
                        try
                        {
                            int copyStride = Math.Min(stride, Math.Abs(targetData.Stride));
                            for (int row = 0; row < pixelHeight; row++)
                            {
                                Marshal.Copy(
                                    pixelBytes,
                                    row * stride,
                                    IntPtr.Add(targetData.Scan0, row * targetData.Stride),
                                    copyStride);
                            }
                        }
                        finally
                        {
                            copy.UnlockBits(targetData);
                        }

                        copy.SetResolution(dpi, dpi);
                        return copy;
                    }
                    catch
                    {
                        copy.Dispose();
                        throw;
                    }
                }
                finally
                {
                    if (bitmap != IntPtr.Zero)
                    {
                        PdfiumNative.FPDFBitmap_Destroy(bitmap);
                    }
                    if (pixels != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(pixels);
                    }
                    PdfiumNative.FPDF_ClosePage(page);
                }
            }
        }

        public void Dispose()
        {
            lock (PdfiumSync)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                if (document != IntPtr.Zero)
                {
                    PdfiumNative.FPDF_CloseDocument(document);
                    document = IntPtr.Zero;
                }
            }
        }

        private static void EnsurePdfiumInitialized()
        {
            if (Volatile.Read(ref pdfiumInitialized))
            {
                return;
            }

            lock (PdfiumSync)
            {
                if (pdfiumInitialized)
                {
                    return;
                }

                try
                {
                    LoadPdfiumNativeLibrary();
                    PdfiumNative.FPDF_InitLibrary();
                    Volatile.Write(ref pdfiumInitialized, true);
                }
                catch (Exception exception) when (
                    exception is DllNotFoundException ||
                    exception is BadImageFormatException ||
                    exception is EntryPointNotFoundException)
                {
                    throw new InvalidOperationException(
                        "PDFium could not be loaded. The application must deploy the " +
                        "matching x64 or x86 pdfium.dll native library.",
                        exception);
                }
            }
        }

        private static void LoadPdfiumNativeLibrary()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string architectureDirectory = GetProcessArchitectureDirectory();
            string architectureNative = Path.Combine(
                baseDirectory,
                architectureDirectory,
                "pdfium.dll");
            string rootNative = Path.Combine(baseDirectory, "pdfium.dll");

            if (File.Exists(architectureNative))
            {
                LoadNativeLibrary(architectureNative);
            }
            else if (File.Exists(rootNative))
            {
                LoadNativeLibrary(rootNative);
            }
            else
            {
                string resourceName =
                    "PDFQFZ.Native." + architectureDirectory + ".pdfium.dll";
                string extractedPath = ExtractEmbeddedNativeLibrary(resourceName);
                LoadNativeLibrary(extractedPath);
            }
        }

        private static string GetProcessArchitectureDirectory()
        {
            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.X86:
                    return "x86";
                case Architecture.Arm64:
                    return "arm64";
                default:
                    return "x64";
            }
        }

        private static string ExtractEmbeddedNativeLibrary(string resourceName)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream resource = assembly.GetManifestResourceStream(resourceName))
            {
                if (resource == null)
                {
                    throw new InvalidOperationException(
                        "The embedded PDFium library was not found: " + resourceName);
                }

                byte[] expectedBytes;
                using (var memory = new MemoryStream())
                {
                    resource.CopyTo(memory);
                    expectedBytes = memory.ToArray();
                }

                string expectedHash = ComputeSha256(expectedBytes);
                string architectureDirectory = resourceName.Split('.')[2];
                string destinationDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PDFQFZ",
                    "pdfium-158.0.8125",
                    architectureDirectory);
                Directory.CreateDirectory(destinationDirectory);

                string destinationPath = Path.Combine(
                    destinationDirectory,
                    "pdfium.dll");
                if (File.Exists(destinationPath) &&
                    string.Equals(
                        ComputeSha256(File.ReadAllBytes(destinationPath)),
                        expectedHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return destinationPath;
                }

                string temporaryPath =
                    destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temporaryPath, expectedBytes);
                try
                {
                    if (File.Exists(destinationPath))
                    {
                        File.Replace(temporaryPath, destinationPath, null);
                    }
                    else
                    {
                        File.Move(temporaryPath, destinationPath);
                    }
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }

                return destinationPath;
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return BitConverter.ToString(sha256.ComputeHash(bytes))
                    .Replace("-", string.Empty);
            }
        }

        private static void LoadNativeLibrary(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                PdfiumNative.SetDllDirectory(directory);
            }

            pdfiumModule = PdfiumNative.LoadLibrary(path);
            if (pdfiumModule == IntPtr.Zero)
            {
                int errorCode = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    "Windows could not load PDFium from " + path +
                    ". Win32 error: " + errorCode);
            }
        }

        private static int ToPixels(float points, int dpi)
        {
            return Math.Max(1, (int)Math.Ceiling(points * dpi / 72d));
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(PdfiumPageRenderer));
            }
        }

        private static class PdfiumNative
        {
            internal const int FPDF_ANNOT = 0x01;
            internal const int FPDF_PRINTING = 0x800;

            internal enum FPDFBitmapFormat
            {
                BGRA = 4
            }

            [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool SetDllDirectory(string pathName);

            [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern IntPtr LoadLibrary(string fileName);

            [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void FPDF_InitLibrary();

            [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr FPDF_LoadDocument(
                [MarshalAs(UnmanagedType.LPUTF8Str)] string filePath,
                [MarshalAs(UnmanagedType.LPUTF8Str)] string password);

            [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void FPDF_CloseDocument(IntPtr document);

            [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
            internal static extern uint FPDF_GetLastError();

            [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int FPDF_GetPageCount(IntPtr document);

            [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

            [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void FPDF_ClosePage(IntPtr page);

            [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
            internal static extern float FPDF_GetPageWidthF(IntPtr page);

            [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
            internal static extern float FPDF_GetPageHeightF(IntPtr page);

            [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int FPDFPage_GetRotation(IntPtr page);

            [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr FPDFBitmap_CreateEx(
                int width,
                int height,
                FPDFBitmapFormat format,
                IntPtr firstScan,
                int stride);

            [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void FPDFBitmap_FillRect(
                IntPtr bitmap,
                int left,
                int top,
                int width,
                int height,
                uint color);

            [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void FPDF_RenderPageBitmap(
                IntPtr bitmap,
                IntPtr page,
                int startX,
                int startY,
                int sizeX,
                int sizeY,
                int rotate,
                int flags);

            [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void FPDFBitmap_Destroy(IntPtr bitmap);
        }
    }
}
