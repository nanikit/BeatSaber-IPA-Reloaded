using IPA.Logging;
using IPA.Logging.Printers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace IPA.Loader.Tests
{
    [TestClass]
    public class LogFileSharingTests
    {
        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ReadingLogsDoesNotInterruptProtectedLogging(bool useLatestLink)
        {
            string directory = Path.Combine(Path.GetTempPath(), "BSIPA-LogSharing-" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "session.log");
                string readPath = useLatestLink ? Path.Combine(directory, "_latest.log") : path;
                using var printer = new TemporaryLogPrinter(path);
                printer.StartPrint();
                printer.Print(Logger.Level.Info, DateTime.Now, "Test", "Before reader");
                printer.EndPrint();

                using var reader = File.Open(readPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                printer.StartPrint();
                printer.Print(Logger.Level.Info, DateTime.Now, "Test", "During reader");
                using var concurrentReader = File.Open(readPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                Assert.ThrowsException<IOException>(() =>
                {
                    using var writer = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                }, "Other writers must not modify a log while logging is active.");
                Assert.ThrowsException<IOException>(() => File.Delete(path),
                    "Other programs must not delete a log while logging is active.");
                printer.EndPrint();

                using var textReader = new StreamReader(reader);
                string contents = textReader.ReadToEnd();
                StringAssert.Contains(contents, "Before reader", "Reopening the log must preserve earlier messages.");
                StringAssert.Contains(contents, "During reader", "New messages must be recorded while a reader is open.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private sealed class TemporaryLogPrinter : GlobalLogFilePrinter
        {
            private readonly string path;

            public TemporaryLogPrinter(string path) => this.path = path;

            protected override FileInfo GetFileInfo() => new FileInfo(path);
        }
    }
}
