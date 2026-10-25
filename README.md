# Lobster — Freia tech

**FR** · Convertir et retoucher des fichiers **sans ouvrir d'application** : commencez à glisser
des fichiers en maintenant **Maj**, un menu circulaire apparaît sous le curseur, relâchez sur une
action. Tout se fait sur votre ordinateur, rien n'est envoyé sur Internet.
Windows · Linux · macOS (Apple Silicon) — interface en français ou en anglais.

**EN** · Convert and edit files **without opening an app**: start dragging files while holding
**Shift**, a circular menu appears under the cursor, release on an action. Everything runs
locally, nothing is uploaded. Windows · Linux · macOS (Apple Silicon) — French or English UI.

---

## Ce que Lobster sait faire · What it does

| | |
|---|---|
| **Images** | PNG, JPG, WebP, AVIF, GIF, BMP, TIFF, ICO, PDF ← aussi HEIC, SVG, PSD, RAW · compresser · redimensionner · rogner (formats 1:1, 16:9…) · pivoter, miroir · N&B · supprimer l'EXIF · fusionner en PDF / GIF animé / côte à côte / empilées |
| **Vidéo** | MP4 (H.264/H.265), WebM, MKV, MOV, AVI, GIF · compresser (qualité ou taille cible 10/25/50 Mo) · 2160p→480p · rogner · découper avec aperçu · pivoter · extraire l'audio · muet · normaliser · vitesse · capture · mettre bout à bout |
| **Audio** | MP3, M4A, WAV, FLAC, OGG, Opus, AIFF, WMA · compresser · découper · normaliser, mono, fondu, vitesse · fusionner |
| **PDF** | → PNG/JPG par page, Word (Windows + Word), texte · compresser · séparer, extraire, supprimer, inverser des pages · pivoter · fusionner |
| **Documents** | Word, Excel, PowerPoint, ODF, RTF, HTML, Markdown → PDF, DOCX, ODT, XLSX, CSV, PPTX, images · tout mélange → un seul PDF |
| **Tout** | ZIP · copier les chemins |

Lobster n'écrase jamais un fichier : `photo (compressé).jpg`, `photo (2).webp`…

## Ouvrir le menu · Opening the menu

| | Windows | macOS | Linux |
|---|---|---|---|
| **Maj + glisser** | ✓ Explorateur, Bureau (ou partout) | ✓ partout (autorisation « Surveillance de l'entrée ») | ✓ sessions X11 · ✗ Wayland |
| **Bulle Lobster** (glisser dessus) | — | ✓ | ✓ |
| **Clic droit** | « Lobster… », « Envoyer vers » | Actions rapides › Lobster, Ouvrir avec | Nautilus, Nemo, Caja, Dolphin, Ouvrir avec |
| **Icône système** | zone de notification | barre des menus | zone de notification |

La touche (Maj, Ctrl/Control, Alt/Option), la langue, la palette du menu radial (8 palettes +
couleur personnalisée) et l'emplacement des fichiers créés se règlent dans les Paramètres.

## Installer · Install

**Windows 10/11** — lancez `Lobster-…-windows-x64.exe`, il s'installe dans
`%LOCALAPPDATA%\Programs\Lobster` (sans droits administrateur). Relancer une version plus récente
propose de **mettre à jour** la version installée. Variante « light » (21 Mo) si le runtime
.NET 8 Desktop est déjà présent. Audio/vidéo : ffmpeg, proposé via winget à la première utilisation.

**Linux (x64)** — `tar xzf Lobster-…-linux-x64.tar.gz && cd lobster-* && ./install.sh`
(sans sudo ; `./install.sh --uninstall` pour retirer). Moteurs : `ffmpeg`, `poppler-utils`,
`libreoffice` (facultatif) — le script indique la commande pour votre distribution.

**macOS (Apple Silicon, 11+)** — dézippez, glissez **Lobster.app** dans Applications, puis
clic droit › **Ouvrir** la première fois (application non notariée). Autorisez Lobster dans
*Réglages Système › Confidentialité et sécurité › Surveillance de l'entrée* pour Maj + glisser.
Moteurs : `brew install ffmpeg poppler` et `brew install --cask libreoffice` (facultatif).

## Compiler · Build

```bash
./build.sh            # → dist/ : Windows exe + light zip, Linux tar.gz, macOS .app zip
./build.sh linux      # ou windows / macos
```

.NET 8 SDK, depuis n'importe quel système (`EnableWindowsTargeting`). Pour macOS, `build.sh`
signe l'app ad hoc avec [`rcodesign`](https://github.com/indygreg/apple-platform-rs) s'il est
installé (`cargo install apple-codesign`) — indispensable sur Apple Silicon.

## Architecture

```
src/
├─ Lobster.Core      bibliothèque partagée (net8.0)
│  ├─ Engines/       ffmpeg · ImageMagick (Magick.NET) · PDF (PDFsharp, PdfPig) · Office/LibreOffice · Markdown, ZIP
│  ├─ Menu/          catalogue des actions par type de fichier · prérequis
│  └─ Core/          IHost (contrat avec l'interface) · tâches · réglages · langues (L("fr","en")) · palettes · instance unique
├─ Lobster.Windows   WPF : hook souris global, menu radial, fenêtres, Explorateur (registre, Envoyer vers), rendu PDF Windows
└─ Lobster.Desktop   Avalonia (Linux + macOS) : menu radial, bulle, CGEventTap (macOS), XQueryPointer (X11), intégrations
packaging/           Info.plist + icône macOS · install.sh Linux
```

Chaque texte affiché est écrit `L("français", "English")` : les deux langues restent côte à côte.
Journal : `lobster.log` dans `%LOCALAPPDATA%\Lobster` · `~/.local/share/Lobster` · `~/Library/Application Support/Lobster`.

© 2026 Freia tech
