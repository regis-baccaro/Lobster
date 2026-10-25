# Lobster — Freia tech

Convert and edit files **without opening an app**: start dragging files while holding **Shift**,
a circular menu appears under the cursor, release on an action. Everything runs locally —
nothing is uploaded.

Windows · Linux · macOS (Apple Silicon) — French or English interface.

---

## What it does

| | |
|---|---|
| **Images** | PNG, JPG, WebP, AVIF, GIF, BMP, TIFF, ICO, PDF (also reads HEIC, SVG, PSD, RAW) · compress · resize · crop (free, 1:1, 16:9…) · rotate, mirror · black & white · strip EXIF · merge into a PDF / animated GIF / side by side / stacked |
| **Video** | MP4 (H.264/H.265), WebM, MKV, MOV, AVI, GIF · compress (quality or target size 10/25/50 MB) · 2160p→480p · crop · trim with preview · rotate · extract audio · mute · normalize · speed · frame grab · join end to end |
| **Audio** | MP3, M4A, WAV, FLAC, OGG, Opus, AIFF, WMA · compress · trim · normalize, mono, fade, speed · merge |
| **PDF** | → PNG/JPG per page, Word (Windows with Word), text · compress · split, extract, delete, reverse pages · rotate · merge |
| **Documents** | Word, Excel, PowerPoint, ODF, RTF, HTML, Markdown → PDF, DOCX, ODT, XLSX, CSV, PPTX, images · any mix → one PDF |
| **Anything** | ZIP · copy paths |

Lobster never overwrites a file: results get a new name — `photo (compressed).jpg`, `photo (2).webp`…

## Opening the menu

| | Windows | macOS | Linux |
|---|---|---|---|
| **Shift + drag** | ✓ Explorer, Desktop (or everywhere) | ✓ everywhere ("Input Monitoring" permission) | ✓ X11 sessions · ✗ Wayland |
| **Lobster bubble** (drag files onto it) | — | ✓ | ✓ |
| **Right-click** | "Lobster…", "Send to" | Quick Actions › Lobster, Open With | Nautilus, Nemo, Caja, Dolphin, Open With |
| **System icon** | notification area | menu bar | notification area |

The modifier key (Shift, Ctrl/Control, Alt/Option), the language, the radial menu palette
(8 palettes + a custom color) and where created files go are all set in Settings.

## Download

Get the latest version from the **[Releases page](https://github.com/regis-baccaro/Lobster/releases/latest)**:
`…-windows-x64.exe` · `…-linux-x64.tar.gz` · `…-macos-arm64.zip`.

## Install

**Windows 10/11** — run `Lobster-…-windows-x64.exe`; it installs itself into
`%LOCALAPPDATA%\Programs\Lobster` (no admin rights). Launching a newer version offers to
**update** the installed one. A "light" build (21 MB) is available when the .NET 8 Desktop
runtime is already installed. Audio/video needs ffmpeg, offered through winget on first use.

**Linux (x64)** — `tar xzf Lobster-…-linux-x64.tar.gz && cd lobster-* && ./install.sh`
(no sudo; `./install.sh --uninstall` to remove). Engines: `ffmpeg`, `poppler-utils`,
`libreoffice` (optional) — the script prints the right command for your distribution.

**macOS (Apple Silicon, 11+)** — unzip and drag **Lobster.app** into **Applications**
(important: launched from Downloads, macOS runs it from a temporary copy).
The app is ad-hoc signed but not notarized: on first launch, right-click › **Open**;
on macOS 15, *System Settings › Privacy & Security › Open Anyway*
(or `xattr -dr com.apple.quarantine /Applications/Lobster.app`).
For Shift + drag, allow Lobster under *Input Monitoring* if macOS asks.
Engines: `brew install ffmpeg poppler` and `brew install --cask libreoffice` (optional) —
Lobster finds them in `/opt/homebrew/bin` even when started from Finder.

## Build

```bash
./build.sh            # → dist/: Windows exe + light zip, Linux tar.gz, macOS .app zip
./build.sh linux      # or windows / macos
```

Releases are built and published by GitHub Actions (`.github/workflows/release.yml`) whenever a
version tag is pushed: `git tag v1.2.0 && git push origin v1.2.0`.

Requires the .NET 8 SDK and works from any OS (`EnableWindowsTargeting`). For macOS, `build.sh`
ad-hoc signs the app with [`rcodesign`](https://github.com/indygreg/apple-platform-rs) when it is
installed (`cargo install apple-codesign`) — required on Apple Silicon.

## Architecture

```
src/
├─ Lobster.Core      shared library (net8.0)
│  ├─ Engines/       ffmpeg · ImageMagick (Magick.NET) · PDF (PDFsharp, PdfPig) · Office/LibreOffice · Markdown, ZIP
│  ├─ Menu/          action catalog per file type · prerequisites
│  └─ Core/          IHost (contract with the UI) · jobs · settings · languages (L("fr","en")) · palettes · single instance
├─ Lobster.Windows   WPF: global mouse hook, radial menu, windows, Explorer integration (registry, Send to), Windows PDF renderer
└─ Lobster.Desktop   Avalonia (Linux + macOS): radial menu, bubble, CGEventTap (macOS), XQueryPointer (X11), integrations
packaging/           macOS Info.plist + icon · Linux install.sh
```

Every user-facing string is written as `L("français", "English")`, so both languages stay side by side.
Log file: `lobster.log` in `%LOCALAPPDATA%\Lobster` · `~/.local/share/Lobster` · `~/Library/Application Support/Lobster`.

© 2026 Freia tech
