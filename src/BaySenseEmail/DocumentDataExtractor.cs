using System.Text;
using System.Text.RegularExpressions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace BaySenseSystems.BaySenseEmail
{
    public static class DocumentDataExtractor
    {
        /// <summary>
        /// Extract text from all pages of a PDF and parse ServiceCenter fields.
        /// Uses the same iText7 PdfTextExtractor pattern as clawPDF's EMailClientAction.
        /// </summary>
        public static DocumentData Extract(string pdfPath)
        {
            var text = ExtractText(pdfPath);
            return Parse(text);
        }

        public static string ExtractText(string pdfPath)
        {
            var sb = new StringBuilder();
            using (var reader = new PdfReader(pdfPath))
            using (var doc = new PdfDocument(reader))
            {
                for (int i = 1; i <= doc.GetNumberOfPages(); i++)
                {
                    var page = doc.GetPage(i);
                    var pageText = PdfTextExtractor.GetTextFromPage(page, new SimpleTextExtractionStrategy());
                    sb.AppendLine(pageText);
                }
            }
            return sb.ToString();
        }

        public static DocumentData Parse(string text)
        {
            var data = new DocumentData();

            // --- Document type ---
            // Repair Order/Estimate: "HRM Automotive Repair Order" or "HRM Automotive Estimate"
            // Invoice: header is "HRM Automotive Page 1 of 1", "Invoice" on address line or total line
            var typeMatch = Regex.Match(text, @"HRM Automotive\s+(Estimate|Invoice|Repair Order)");
            if (typeMatch.Success)
            {
                data.DocumentType = typeMatch.Groups[1].Value;
            }
            else if (Regex.IsMatch(text, @"Invoice Total:|Unit C\s+Invoice", RegexOptions.IgnoreCase))
            {
                data.DocumentType = "Invoice";
            }
            else if (Regex.IsMatch(text, @"Estimate Total:", RegexOptions.IgnoreCase))
            {
                data.DocumentType = "Estimate";
            }

            // --- RO number ---
            // Appears on the address line after the shop address:
            //   Native PDF: "Alban Road, Unit C\n     52476"
            //   OCR:        "8086 Alban Road, Unit C 52476"
            // It's a 4-6 digit number near "Alban Road" or after the shop address.
            var roMatch = Regex.Match(text, @"Alban Road[,\s]+Unit C\s+(\d{4,6})");
            if (!roMatch.Success)
            {
                // Invoice layout: "Unit C Invoice" on address line, number after ZIP on next line
                roMatch = Regex.Match(text, @"Springfield.*?22150\s+(\d{4,6})");
            }
            if (!roMatch.Success)
            {
                // Fallback: indented standalone number (native PDF layout)
                roMatch = Regex.Match(text, @"Alban Road.*?\n\s+(\d{4,6})", RegexOptions.Singleline);
            }
            if (roMatch.Success)
            {
                data.RONumber = roMatch.Groups[1].Value;
            }

            // --- Customer name ---
            // Two layouts observed:
            //   OCR:        "Wilt, Jim 2008 HYUNDAI SANTA FE LIMITED..."  (name BEFORE vehicle)
            //   Native PDF: "...GAS N E Wilt, Jim"  (name AFTER vehicle)
            // Try OCR layout first: "Last, First" followed by a 4-digit year
            var nameMatch = Regex.Match(text,
                @"^([A-Z][a-zA-Z']+),\s+([A-Z][a-zA-Z]+)\s+(?:19|20)\d{2}\s+[A-Z]",
                RegexOptions.Multiline);
            if (!nameMatch.Success)
            {
                // Native PDF layout: name at end of vehicle line after engine spec
                nameMatch = Regex.Match(text,
                    @"(?:GAS|FIGAS|DIESEL)\s+[A-Z].*?([A-Z][a-zA-Z']+),\s+([A-Z][a-zA-Z]+)");
            }
            if (!nameMatch.Success)
            {
                // Broadest fallback: "Last, First" on a line that also contains a year
                nameMatch = Regex.Match(text,
                    @"([A-Z][a-zA-Z']+),\s+([A-Z][a-zA-Z]+).*(?:19|20)\d{2}\s+[A-Z]");
            }
            if (nameMatch.Success)
            {
                data.CustomerLastName = nameMatch.Groups[1].Value.Trim();
                data.CustomerFirstName = nameMatch.Groups[2].Value.Trim();
            }

            // --- Customer email ---
            // Two layouts:
            //   OCR:        "Cell: Email: james.wilt6@gmail.com DOM: Save Old Parts: No"
            //   Native PDF: "Cell: Save Old Parts: No\nDOM:\nEmail: james.wilt6@gmail.com"
            // Common pattern: "Cell:" somewhere before "Email:" with the customer address.
            // The shop email ("Tim@hrmautomotive.com") appears on a line starting with "Email:"
            // but NOT preceded by "Cell:" on the same or nearby line.
            var emailMatch = Regex.Match(text,
                @"Cell:.*?Email:\s*(\S+@\S+)",
                RegexOptions.Singleline);
            if (emailMatch.Success)
            {
                data.CustomerEmail = emailMatch.Groups[1].Value.TrimEnd('.', ',');
            }

            // --- Vehicle ---
            // Both layouts have: "2008 HYUNDAI SANTA FE LIMITED V6 3.3L..."
            // Capture year + make + model words, stopping before engine spec.
            // Engine spec starts with V followed by digit, or a digit followed by dot (3.3L).
            // OCR may merge "FI GAS" into "FIGAS" — doesn't affect our capture.
            var vehicleMatch = Regex.Match(text,
                @"((?:19|20)\d{2})\s+([A-Z]{2,})\s+((?:[A-Z0-9]+\s+)*?[A-Z]+)\s+V\d");
            if (vehicleMatch.Success)
            {
                var year = vehicleMatch.Groups[1].Value;
                var make = ToTitleCase(vehicleMatch.Groups[2].Value.Trim());
                var model = ToTitleCase(vehicleMatch.Groups[3].Value.Trim());
                data.Vehicle = $"{year} {make} {model}";
            }

            // --- Total ---
            // "Repair Order Total: $736.76" or "Estimate Total: $463.60"
            var totalMatch = Regex.Match(text,
                @"(?:Repair Order|Estimate|Invoice) Total:\s*\$?([\d,]+\.\d{2})");
            if (!totalMatch.Success)
            {
                // Native PDF fallback: first standalone $amount line near end of document
                var allDollarLines = Regex.Matches(text,
                    @"^\$([\d,]+\.\d{2})\s*$", RegexOptions.Multiline);
                if (allDollarLines.Count > 0)
                {
                    data.Total = "$" + allDollarLines[0].Groups[1].Value;
                }
            }
            else
            {
                data.Total = "$" + totalMatch.Groups[1].Value;
            }

            return data;
        }

        private static string ToTitleCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var sb = new StringBuilder();
            bool capitalizeNext = true;
            foreach (char c in input)
            {
                if (c == ' ')
                {
                    sb.Append(c);
                    capitalizeNext = true;
                }
                else if (capitalizeNext)
                {
                    sb.Append(char.ToUpper(c));
                    capitalizeNext = false;
                }
                else
                {
                    sb.Append(char.ToLower(c));
                }
            }
            return sb.ToString();
        }
    }
}
