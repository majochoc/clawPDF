using System;
using System.IO;
using System.Linq;

namespace BaySenseSystems.BaySenseEmail
{
    class Program
    {
        static int Main(string[] args)
        {
            // Config lives next to the exe
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var configPath = Path.Combine(exeDir, "config.ini");
            var config = Config.Load(configPath);

            // Set up logging
            var logPath = config.LogFile;
            if (!Path.IsPathRooted(logPath))
                logPath = Path.Combine(exeDir, logPath);

            // clawPDF ScriptAction passes: [ParameterString...] "path1.pdf" "path2.pdf"
            var pdfPaths = args
                .Where(a => a.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (pdfPaths.Length == 0)
            {
                Log(logPath, "ERROR", "No PDF file paths in arguments: " + string.Join(" ", args));
                return 1;
            }

            var pdfPath = pdfPaths[0];
            Log(logPath, "INFO", "Processing: " + pdfPath);

            if (!File.Exists(pdfPath))
            {
                Log(logPath, "ERROR", "PDF not found: " + pdfPath);
                return 1;
            }

            // Extract metadata from OCR'd PDF
            DocumentData data;
            try
            {
                var rawText = DocumentDataExtractor.ExtractText(pdfPath);
                data = DocumentDataExtractor.Parse(rawText);
            }
            catch (Exception ex)
            {
                Log(logPath, "ERROR", "Failed to extract text from PDF: " + ex.Message);
                return 1;
            }

            Log(logPath, "INFO",
                $"Extracted: RO={data.RONumber} Type={data.DocumentType} " +
                $"Email={data.CustomerEmail} Name={data.CustomerFirstName} {data.CustomerLastName} " +
                $"Vehicle={data.Vehicle} Total={data.Total}");

            if (string.IsNullOrEmpty(data.CustomerEmail))
            {
                Log(logPath, "WARN", "No customer email found — using placeholder");
                data.CustomerEmail = "ENTER-CUSTOMER-EMAIL";
            }

            if (string.IsNullOrEmpty(data.RONumber))
            {
                Log(logPath, "WARN", "No RO number found — using filename as fallback");
                data.RONumber = Path.GetFileNameWithoutExtension(pdfPath);
            }

            // Rename PDF to include RO number and document type
            var dir = Path.GetDirectoryName(pdfPath);
            var typeTag = data.DocumentType.Replace(" ", "");
            var newName = $"RO-{data.RONumber}-{typeTag}.pdf";
            var newPath = Path.Combine(dir, newName);

            // Avoid collision: append suffix if file exists
            if (File.Exists(newPath) && !string.Equals(pdfPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                var timestamp = DateTime.Now.ToString("HHmmss");
                newName = $"RO-{data.RONumber}-{typeTag}-{timestamp}.pdf";
                newPath = Path.Combine(dir, newName);
            }

            try
            {
                File.Move(pdfPath, newPath);
                Log(logPath, "INFO", $"Renamed: {Path.GetFileName(pdfPath)} -> {newName}");
                pdfPath = newPath;
            }
            catch (Exception ex)
            {
                Log(logPath, "WARN", "Could not rename PDF: " + ex.Message + " — using original path");
            }

            // Render email from templates
            var subject = EmailComposer.Render(config.SubjectTemplate, data, config);
            var body = EmailComposer.Render(config.BodyTemplate, data, config);

            // Open email client via MAPI (Thunderbird)
            try
            {
                EmailComposer.OpenMailCompose(
                    to: data.CustomerEmail,
                    subject: subject,
                    body: body,
                    attachmentPath: pdfPath);

                Log(logPath, "INFO",
                    $"Thunderbird compose opened: RO {data.RONumber} -> {data.CustomerEmail}");
            }
            catch (Exception ex)
            {
                Log(logPath, "ERROR", "Failed to open Thunderbird: " + ex.Message);
                return 1;
            }

            return 0;
        }

        private static void Log(string logPath, string level, string message)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}";
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch
            {
                // Logging failure should not crash the app
            }
        }
    }
}
