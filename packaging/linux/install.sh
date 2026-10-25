#!/bin/sh
# Lobster — Freia tech · installation Linux (utilisateur courant, sans sudo)
# Lobster — Freia tech · Linux install (current user, no sudo)
set -e

HERE="$(cd "$(dirname "$0")" && pwd)"
DEST="${XDG_DATA_HOME:-$HOME/.local/share}/lobster"
BIN="$HOME/.local/bin"

case "${LANG:-}" in fr*) FR=1 ;; *) FR=0 ;; esac
say() { if [ "$FR" = 1 ]; then echo "$1"; else echo "$2"; fi; }

if [ "$1" = "--uninstall" ]; then
  pkill -x lobster 2>/dev/null || true
  "$DEST/lobster" --quit 2>/dev/null || true
  rm -rf "$DEST" "$BIN/lobster"
  D="${XDG_DATA_HOME:-$HOME/.local/share}"; C="${XDG_CONFIG_HOME:-$HOME/.config}"
  rm -f "$D/applications/lobster.desktop" "$C/autostart/lobster.desktop" "$D/nautilus/scripts/Lobster" \
        "$C/caja/scripts/Lobster" "$D/nemo/actions/lobster.nemo_action" "$D/kio/servicemenus/lobster.desktop" \
        "$D/kservices5/ServiceMenus/lobster.desktop" "$D/icons/hicolor/256x256/apps/lobster.png"
  say "Lobster a été désinstallé (vos réglages restent dans ~/.local/share/Lobster)." \
      "Lobster was uninstalled (your settings remain in ~/.local/share/Lobster)."
  exit 0
fi

# Stop a running copy before replacing it.
"$DEST/lobster" --quit 2>/dev/null || true
sleep 1

mkdir -p "$DEST" "$BIN"
cp "$HERE/lobster" "$DEST/lobster"
chmod +x "$DEST/lobster"
ln -sf "$DEST/lobster" "$BIN/lobster"

say "✓ Lobster installé dans $DEST" "✓ Lobster installed in $DEST"

# Optional engines: tell the user what is missing, with the right command for this distribution.
pm=""
if command -v apt >/dev/null 2>&1; then pm="sudo apt install"; pkgs_ff=ffmpeg; pkgs_pdf=poppler-utils; pkgs_lo=libreoffice
elif command -v dnf >/dev/null 2>&1; then pm="sudo dnf install"; pkgs_ff=ffmpeg; pkgs_pdf=poppler-utils; pkgs_lo=libreoffice
elif command -v pacman >/dev/null 2>&1; then pm="sudo pacman -S"; pkgs_ff=ffmpeg; pkgs_pdf=poppler; pkgs_lo=libreoffice-fresh
elif command -v zypper >/dev/null 2>&1; then pm="sudo zypper install"; pkgs_ff=ffmpeg; pkgs_pdf=poppler-tools; pkgs_lo=libreoffice
fi
missing=""
command -v ffmpeg >/dev/null 2>&1 || missing="$missing ${pkgs_ff:-ffmpeg}"
command -v pdftoppm >/dev/null 2>&1 || missing="$missing ${pkgs_pdf:-poppler-utils}"
if [ -n "$missing" ]; then
  say "" ""
  say "Pour l'audio, la vidéo et PDF → images, installez :" "For audio, video and PDF → images, install:"
  echo "    $pm$missing"
fi
if ! command -v soffice >/dev/null 2>&1 && ! command -v libreoffice >/dev/null 2>&1; then
  say "Facultatif, pour les documents Office : $pm ${pkgs_lo:-libreoffice}" "Optional, for Office documents: $pm ${pkgs_lo:-libreoffice}"
fi

# First launch sets up the file-manager actions, "Open with" and autostart.
nohup "$DEST/lobster" >/dev/null 2>&1 &
say "" ""
say "Lobster est lancé. Glissez des fichiers sur la bulle, ou faites clic droit › Lobster dans votre gestionnaire de fichiers." \
    "Lobster is running. Drag files onto the bubble, or right-click › Lobster in your file manager."
