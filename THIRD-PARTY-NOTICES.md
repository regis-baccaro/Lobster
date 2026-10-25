# Composants tiers · Third-party components

## Inclus dans Lobster · Bundled

| Composant | Usage | Licence |
|---|---|---|
| Magick.NET (ImageMagick et ses délégués) | images | Apache 2.0 (Magick.NET), licence ImageMagick |
| PDFsharp **6.2.4** | fusion, pages, rotation de PDF | MIT |
| PdfPig | extraction de texte PDF | Apache 2.0 |
| Markdig | Markdown → HTML | BSD-2-Clause |
| Avalonia 11 (Linux, macOS) | interface | MIT |
| SkiaSharp, HarfBuzzSharp (Linux, macOS) | rendu | MIT |
| Inter (Linux, macOS) | police | SIL Open Font License 1.1 |
| .NET 8 runtime | exécution | MIT |

PDFsharp est volontairement figé en 6.2.4, dernière version publiée sous MIT
(les versions 6.5+ sont sous AGPL-3.0 ou licence commerciale).

## Outils externes, non inclus · External tools, not bundled

Lobster les appelle comme programmes séparés, s'ils sont installés :

- **ffmpeg** — audio et vidéo (LGPL/GPL selon la compilation)
- **Ghostscript** — compression PDF optimale, rendu PDF de secours (AGPL)
- **Poppler** (`pdftoppm`) — PDF → images sous Linux et macOS (GPL)
- **Microsoft Office** ou **LibreOffice** — conversions de documents
