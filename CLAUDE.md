# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

clawPDF is a Windows virtual printer that converts print jobs to PDF, PDF/A, PDF/X, JPEG, PNG, TIFF, SVG, TXT (OCR), XPS, and DOCX. It combines a native C++ print port monitor (`clawmon`) with a .NET Framework WPF application. Licensed under AGPL v3.

## Build

- **IDE**: Visual Studio 2022
- **Framework**: .NET Framework 4.6.2
- **Solution**: `clawPDF.sln` (root)
- **Build**: Open solution in VS2022, build with `Debug|Any CPU` or `Release|Any CPU` for the C# projects. The `clawmon` C++ projects require `Win32`, `x64`, or `ARM64` platform targets.
- **Installer**: `src/clawPDF_setup/clawPDF_setup.vdproj` (VS Deployment Project)
- **No test runner configured** — `SystemWrapper.Tests` is the only test project

## Architecture

### Data Flow

```
Print Job (PostScript/XPS)
  → clawmon port monitor (C++ DLL, src/clawmon/)
  → .INF + .PS/.XPS files written to spool folder
  → JobInfoQueue detects new files (event-driven)
  → WorkflowFactory selects AutoSaveWorkflow or InteractiveWorkflow
  → GhostscriptJob or XpsJob runs Ghostscript via DLL API (not exe)
  → PDFProcessor applies encryption, signing, backgrounds, metadata
  → IAction chain: open file, email, SMTP, FTP, print, run script
  → Final output files
```

### Key Projects

| Project | Role |
|---------|------|
| `src/clawPDF/` | WPF app: entry point (`App.xaml.cs`), MVVM UI, workflows, job runner |
| `src/clawPDF.Core/` | Core abstractions: IJob, AbstractJob, GhostscriptJob, XpsJob, OutputDevice hierarchy, Actions, Ghostscript wrapper |
| `src/clawPDF.Settings/` | Settings model: `clawPDFSettings` root with `ConversionProfiles[]`, `ApplicationSettings`, INI-based persistence via `pdfforge.DataStorage` |
| `src/PDFProcessor/` | PDF post-processing: encryption, digital signing, XMP metadata, background pages, stamping (uses iTextSharp) |
| `src/clawmon/` | C++ Windows print port monitor DLL implementing MONITOR2 interface |
| `src/clawPDF.Bridge/` | Console app bridging network printer jobs — handles credential management and user impersonation |
| `src/clawPDF.Shared/` | Shared WPF helpers, converters, theming |
| `src/clawPDF.Utilities/` | Utility classes |
| `src/clawPDF.Mail/` | MAPI email client integration |
| `src/SetupHelper/` | CLI tool for adding/removing printers |
| `src/ftplib/` | FTP client library |
| `src/SystemWrapper/` | System.IO and process abstraction layer for testability |
| `src/lib/` | Third-party DLLs: DataStorage.dll, DynamicTranslator.dll |
| `src/BaySenseEmail/` | **BaySense extension** — standalone C# console app called by clawPDF ScriptAction; extracts metadata from OCR'd ServiceCenter PDFs and opens Thunderbird compose |

### Design Patterns

- **Singletons**: `JobInfoQueue`, `JobRunner`, `ThreadManager`, `SettingsHelper`
- **Factory**: `AppStartFactory` (startup mode), `WorkflowFactory` (auto vs interactive), `TokenReplacerFactory`
- **Strategy**: `OutputDevice` subclasses per format (PdfDevice, JpegDevice, PngDevice, etc.)
- **MVVM**: ViewModels in `src/clawPDF/ViewModels/`, Views as XAML windows/user controls
- **Event-driven**: `JobInfoQueue.OnNewJobInfo` triggers job processing

### Profile Selection Order

1. Printer-specific mapping (`ApplicationSettings.PrinterMappings`)
2. Last-used profile
3. Default profile
4. First profile in list

### Startup Modes (AppStartFactory)

The app entry point (`App.xaml.cs`) uses a global mutex and determines mode from command-line args:
- `/PrintFile=` — batch printing
- `/INFODATAFILE=` — network printer job (via Bridge)
- `/ManagePrintJobs` — queue management window
- `/Config=` — deploy INI configuration
- Default — main interactive window with tray icon

### Settings Storage

Settings serialize to INI format. Registry path `HKCU\Software\clawSoft\clawPDF\Settings` stores profile GUIDs for batch processing. Profiles contain output format, PDF security, signatures, auto-save paths, email/FTP/script actions.

### Threading

- Global mutex prevents multiple instances
- STA thread for WPF UI
- `ThreadManager` spawns processing threads
- Lock objects in `AbstractJob` for thread safety

## BaySense Extension — ServiceCenter Email Workflow

This fork adds `src/BaySenseEmail/`, a C# console app that integrates clawPDF with ALLDATA ServiceCenter (Manage) for the BaySense platform. See `/docs/baysense_extensions/` for sample documents.

### How It Works

```
ServiceCenter → Print to "ServiceCenter Email" printer
  → clawPDF generates OCR'd PDF (PdfOCR24 output format)
  → ScriptAction calls BaySenseEmail.exe with PDF path
  → BaySenseEmail extracts text via iText7 PdfTextExtractor (same library clawPDF uses)
  → Regex parses: RO number, customer email/name, vehicle, document type, total
  → Renames PDF to RO-{number}-{type}.pdf (e.g., RO-52476-RepairOrder.pdf)
  → Opens Thunderbird compose via MAPI with recipient, subject, body template, PDF attached
  → Service advisor reviews and sends
```

### clawPDF Profile Configuration

- OutputFormat: `PdfOCR24` (searchable PDF with OCR text layer)
- SkipPrintDialog: `true`
- AutoSave.Enabled: `true`
- Scripting.Enabled: `true`, ScriptFile: path to `BaySenseEmail.exe`
- Scripting.WaitForScript: `false`, HideProcess: `true`
- Printer mapping: "ServiceCenter Email" → this profile

### BaySenseEmail.exe

- Standalone console app, no project references to clawPDF
- Only dependency: iText7 7.2.5 (same version as PDFProcessing project)
- Configured via `config.ini` next to the exe (shop name, phone, email templates)
- Template placeholders: `{ro_number}`, `{document_type}`, `{document_type_lower}`, `{customer_first_name}`, `{customer_last_name}`, `{customer_email}`, `{vehicle}`, `{total}`, `{shop_name}`, `{shop_phone}`
- Logs to configurable path (default `C:\temp\baysense_email.log`)
- Opens email via MAPI (MAPISendMail) — supports Thunderbird and other MAPI clients
- Renames output PDF to `RO-{number}-{type}.pdf` before attaching (timestamp suffix on collision)
- Missing customer email sets `ENTER-CUSTOMER-EMAIL` placeholder in To: field
- `config.ini` has `[api]` section for future baysense-api integration (disabled by default)

### Supported Document Types

| ServiceCenter Document | Header Pattern | Total Pattern | Tested |
|------------------------|---------------|---------------|--------|
| Repair Order | `HRM Automotive Repair Order` | `Repair Order Total:` | Yes |
| Estimate | `HRM Automotive Estimate` | `Estimate Total:` | Yes |
| Invoice | `Unit C Invoice` (different layout) | `Invoice Total:` | Yes |

### OCR Text Parsing Notes

- OCR layout differs significantly from native PDF text extraction (field ordering, whitespace, artifacts)
- Customer names support apostrophes (e.g., O'Connell)
- Vehicle year restricted to 19xx/20xx to avoid matching model numbers (e.g., Sierra 2500)
- RO number location varies: after "Unit C" (RO/Estimate) or after ZIP code (Invoice)
- Invoice layout has different header structure — document type detected via fallback patterns
