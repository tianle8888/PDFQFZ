using System;
using System.IO;
using System.Text;
using iTextSharp.text.pdf;

namespace PDFQFZ.Library
{
    /// <summary>
    /// Prepares a temporary copy whose annotation appearances are painted into
    /// the page content before PDFium renders it. The source PDF is not changed.
    /// </summary>
    internal static class PdfAnnotationFlattener
    {
        public static string CreateFlattenedCopyForRendering(
            string sourcePath,
            string password,
            out bool createdCopy)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                !File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    "The PDF file was not found.",
                    sourcePath);
            }

            createdCopy = false;
            PdfReader reader = OpenReader(sourcePath, password);
            try
            {
                if (!HasAnnotations(reader))
                {
                    return sourcePath;
                }

                string temporaryDirectory = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "PDFQFZ",
                    "render-temp");
                Directory.CreateDirectory(temporaryDirectory);
                string temporaryPath = Path.Combine(
                    temporaryDirectory,
                    Guid.NewGuid().ToString("N") + ".pdf");

                try
                {
                    using (FileStream output = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
                    using (PdfStamper stamper = new PdfStamper(reader, output))
                    {
                        PaintAnnotationAppearances(reader, stamper);
                    }

                    createdCopy = true;
                    return temporaryPath;
                }
                catch
                {
                    TryDelete(temporaryPath);
                    throw;
                }
            }
            finally
            {
                reader.Close();
            }
        }

        public static void TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static PdfReader OpenReader(string path, string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return new PdfReader(path);
            }

            return new PdfReader(
                path,
                new UTF8Encoding().GetBytes(password));
        }

        private static bool HasAnnotations(PdfReader reader)
        {
            for (int page = 1; page <= reader.NumberOfPages; page++)
            {
                PdfDictionary pageDictionary = reader.GetPageN(page);
                PdfArray annotations =
                    pageDictionary.GetAsArray(PdfName.ANNOTS);
                if (annotations != null && annotations.Size > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void PaintAnnotationAppearances(
            PdfReader reader,
            PdfStamper stamper)
        {
            for (int pageNumber = 1;
                 pageNumber <= reader.NumberOfPages;
                 pageNumber++)
            {
                PdfDictionary page = reader.GetPageN(pageNumber);
                PdfArray annotations = page.GetAsArray(PdfName.ANNOTS);
                if (annotations == null || annotations.Size == 0)
                {
                    continue;
                }

                PdfContentByte content =
                    stamper.GetOverContent(pageNumber);
                for (int index = 0; index < annotations.Size; index++)
                {
                    PdfDictionary annotation =
                        annotations.GetAsDict(index);
                    if (annotation == null)
                    {
                        continue;
                    }

                    PdfArray rect = annotation.GetAsArray(PdfName.RECT);
                    PdfDictionary appearances =
                        annotation.GetAsDict(PdfName.AP);
                    if (rect == null || appearances == null)
                    {
                        continue;
                    }

                    PdfStream appearance =
                        appearances.GetAsStream(PdfName.N);
                    if (appearance == null &&
                        appearances.GetAsDict(PdfName.N) != null)
                    {
                        PdfName state =
                            annotation.GetAsName(PdfName.AS);
                        if (state != null)
                        {
                            appearance = appearances
                                .GetAsDict(PdfName.N)
                                .GetAsStream(state);
                        }
                    }
                    PdfArray box =
                        appearance == null
                            ? null
                            : appearance.GetAsArray(PdfName.BBOX);
                    if (appearance == null ||
                        box == null ||
                        rect.Size < 4 ||
                        box.Size < 4)
                    {
                        continue;
                    }

                    float left = GetNumber(rect, 0);
                    float bottom = GetNumber(rect, 1);
                    float right = GetNumber(rect, 2);
                    float top = GetNumber(rect, 3);
                    float boxLeft = GetNumber(box, 0);
                    float boxBottom = GetNumber(box, 1);
                    float boxWidth =
                        GetNumber(box, 2) - boxLeft;
                    float boxHeight =
                        GetNumber(box, 3) - boxBottom;
                    float rectWidth = right - left;
                    float rectHeight = top - bottom;
                    if (rectWidth <= 0 ||
                        rectHeight <= 0 ||
                        boxWidth <= 0 ||
                        boxHeight <= 0)
                    {
                        continue;
                    }

                    float scaleX = rectWidth / boxWidth;
                    float scaleY = rectHeight / boxHeight;
                    float translateX = left - boxLeft * scaleX;
                    float translateY = bottom - boxBottom * scaleY;
                    content.AddFormXObj(
                        appearance,
                        PdfName.XOBJECT,
                        scaleX,
                        0,
                        0,
                        scaleY,
                        translateX,
                        translateY);
                }
            }
        }

        private static float GetNumber(PdfArray array, int index)
        {
            PdfNumber number = array.GetAsNumber(index);
            return number == null ? 0f : number.FloatValue;
        }
    }
}
