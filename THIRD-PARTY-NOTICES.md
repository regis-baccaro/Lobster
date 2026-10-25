# Third-party components

## Bundled

| Component | Used for | License |
|---|---|---|
| Magick.NET (ImageMagick and its delegates) | images | Apache 2.0 (Magick.NET), ImageMagick license |
| PDFsharp **6.2.4** | PDF merge, pages, rotation | MIT |
| PdfPig | PDF text extraction | Apache 2.0 |
| Markdig | Markdown → HTML | BSD-2-Clause |
| Avalonia 11 (Linux, macOS) | user interface | MIT |
| SkiaSharp, HarfBuzzSharp (Linux, macOS) | rendering | MIT |
| Inter (Linux, macOS) | font | SIL Open Font License 1.1 |
| .NET 8 runtime | runtime | MIT |

PDFsharp is deliberately pinned to 6.2.4, the last release published under MIT
(6.5+ is AGPL-3.0 or commercial).

## External tools, not bundled

Lobster runs them as separate programs when they are installed:

- **ffmpeg** — audio and video (LGPL/GPL depending on the build)
- **Ghostscript** — best PDF compression, fallback PDF rendering (AGPL)
- **Poppler** (`pdftoppm`) — PDF → images on Linux and macOS (GPL)
- **Microsoft Office** or **LibreOffice** — document conversions
