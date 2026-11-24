using System;
using System.IO;
using System.Text;
using iTextSharp.text;
using iTextSharp.text.pdf;
using PDFQFZ.Library;
using Xunit;

namespace PDFQFZ.Tests
{
    public sealed class PdfAnnotationFlattenerTests
    {
        [Fact]
        public void CreateFlattenedCopyForRendering_DoesNotCopyPlainPdf()
        {
            string directory = CreateTemporaryDirectory();
            string source = Path.Combine(directory, "plain.pdf");
            CreatePlainPdf(source);

            try
            {
                bool created;
                string result =
                    PdfAnnotationFlattener.CreateFlattenedCopyForRendering(
                        source,
                        "",
                        out created);

                Assert.False(created);
                Assert.Equal(
                    Path.GetFullPath(source),
                    Path.GetFullPath(result));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void CreateFlattenedCopyForRendering_PaintsAnnotationAppearance()
        {
            string directory = CreateTemporaryDirectory();
            string source = Path.Combine(directory, "stamped.pdf");
            CreatePdfWithRectangleAnnotation(source);
            string result = null;

            try
            {
                bool created;
                result = PdfAnnotationFlattener.CreateFlattenedCopyForRendering(
                    source,
                    "",
                    out created);

                Assert.True(created);
                Assert.NotEqual(
                    Path.GetFullPath(source),
                    Path.GetFullPath(result));

                using (PdfReader reader = new PdfReader(result))
                {
                    PdfDictionary resources =
                        reader.GetPageN(1).GetAsDict(PdfName.RESOURCES);
                    PdfDictionary xobjects =
                        resources.GetAsDict(PdfName.XOBJECT);
                    Assert.NotNull(xobjects);
                    Assert.True(xobjects.Size > 0);
                }
            }
            finally
            {
                if (result != null)
                {
                    PdfAnnotationFlattener.TryDelete(result);
                }
                Directory.Delete(directory, true);
            }
        }

        private static void CreatePlainPdf(string path)
        {
            using (var document = new Document())
            using (PdfWriter.GetInstance(
                document,
                new FileStream(path, FileMode.Create)))
            {
                document.Open();
                document.Add(new Paragraph(
                    "plain pdf",
                    FontFactory.GetFont(BaseFont.HELVETICA)));
            }
        }

        private static void CreatePdfWithRectangleAnnotation(string path)
        {
            using (var document = new Document())
            using (var stream = new FileStream(path, FileMode.Create))
            {
                PdfWriter writer = PdfWriter.GetInstance(document, stream);
                document.Open();
                document.Add(new Paragraph(
                    "annotated pdf",
                    FontFactory.GetFont(BaseFont.HELVETICA)));

                PdfAppearance appearance =
                    writer.DirectContent.CreateAppearance(80, 30);
                appearance.SetColorFill(BaseColor.RED);
                appearance.Rectangle(0, 0, 80, 30);
                appearance.Fill();
                PdfAnnotation annotation = PdfAnnotation.CreateSquare(
                    writer,
                    new Rectangle(10, 10, 90, 40),
                    "red stamp",
                    BaseColor.RED);
                annotation.SetAppearance(
                    PdfAnnotation.APPEARANCE_NORMAL,
                    appearance);
                writer.AddAnnotation(annotation);
            }
        }

        private static string CreateTemporaryDirectory()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "pdfqfz-flattener-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
