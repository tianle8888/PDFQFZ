using System;
using System.IO;
using PDFQFZ.Library;
using Xunit;

namespace PDFQFZ.Tests
{
    public sealed class OutputFileSafetyTests
    {
        [Fact]
        public void FindWritableOutputPath_UsesSuffixWhenTargetIsLocked()
        {
            string directory = CreateTemporaryDirectory();
            string target = Path.Combine(directory, "contract.pdf");
            File.WriteAllText(target, "existing");

            try
            {
                using (new FileStream(
                    target,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None))
                {
                    string selected =
                        OutputFileSafety.FindWritableOutputPath(target);

                    Assert.Equal(
                        Path.Combine(directory, "contract_1.pdf"),
                        selected);
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void CommitTemporaryFile_ReplacesExistingTargetAtomically()
        {
            string directory = CreateTemporaryDirectory();
            string target = Path.Combine(directory, "contract.pdf");
            string temporary =
                OutputFileSafety.CreateTemporaryPath(target);
            File.WriteAllText(target, "old");
            File.WriteAllText(temporary, "new");

            try
            {
                string selected = target;
                OutputFileSafety.CommitTemporaryFile(
                    temporary,
                    ref selected);

                Assert.Equal(target, selected);
                Assert.Equal("new", File.ReadAllText(target));
                Assert.False(File.Exists(temporary));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string CreateTemporaryDirectory()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "pdfqfz-output-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
