#!/bin/bash
# Builds every Lobster package from any OS with the .NET 8 SDK.
#   ./build.sh            → dist/ : Windows exe (+ light build), Linux tar.gz, macOS .app zip
#   ./build.sh windows|linux|macos
set -euo pipefail
cd "$(dirname "$0")"
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/Lobster.Core/Lobster.Core.csproj | head -1)
DIST="$PWD/dist"
OUT="$PWD/.publish"
what="${1:-all}"
mkdir -p "$DIST"

pub() { dotnet publish "$@" --nologo -v quiet -c Release; }

if [[ $what == all || $what == windows ]]; then
  echo "▸ Windows $VERSION"
  rm -rf "$OUT/win" "$OUT/win-light"
  pub src/Lobster.Windows -o "$OUT/win"
  pub src/Lobster.Windows -p:Light=true -o "$OUT/win-light"
  cp "$OUT/win/Lobster.exe" "$DIST/Lobster-$VERSION-windows-x64.exe"
  (cd "$OUT/win-light" && rm -f "$DIST/Lobster-$VERSION-windows-x64-light.zip" && zip -q -9 -r "$DIST/Lobster-$VERSION-windows-x64-light.zip" .)
fi

if [[ $what == all || $what == linux ]]; then
  echo "▸ Linux $VERSION"
  rm -rf "$OUT/linux" "$OUT/lobster-linux"
  pub src/Lobster.Desktop -r linux-x64 --self-contained true -p:PublishSingleFile=true \
      -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "$OUT/linux"
  mkdir -p "$OUT/lobster-linux"
  cp "$OUT/linux/lobster" packaging/linux/install.sh README.md THIRD-PARTY-NOTICES.md "$OUT/lobster-linux/"
  chmod +x "$OUT/lobster-linux/lobster" "$OUT/lobster-linux/install.sh"
  tar -C "$OUT" -czf "$DIST/Lobster-$VERSION-linux-x64.tar.gz" --transform "s|^lobster-linux|lobster-$VERSION|" lobster-linux
fi

if [[ $what == all || $what == macos ]]; then
  echo "▸ macOS $VERSION (Apple Silicon)"
  APP="$OUT/mac/Lobster.app"
  rm -rf "$OUT/mac" "$OUT/osx"
  pub src/Lobster.Desktop -r osx-arm64 --self-contained true -p:UseAppHost=true -o "$OUT/osx"
  mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
  cp -R "$OUT/osx/." "$APP/Contents/MacOS/"
  sed "s/@VERSION@/$VERSION/g" packaging/macos/Info.plist > "$APP/Contents/Info.plist"
  cp packaging/macos/lobster.icns "$APP/Contents/Resources/"
  chmod +x "$APP/Contents/MacOS/lobster"
  # Apple Silicon refuses to run unsigned code: ad-hoc sign the whole bundle (no Apple ID needed).
  if command -v rcodesign >/dev/null 2>&1; then
    rcodesign sign --config-file /dev/null "$APP" >/dev/null
  elif [[ "$(uname)" == Darwin ]]; then
    codesign --force --deep --sign - "$APP"
  else
    echo "  ⚠ rcodesign introuvable : l'app ne sera pas signée (cargo install apple-codesign)" >&2
  fi
  (cd "$OUT/mac" && rm -f "$DIST/Lobster-$VERSION-macos-arm64.zip" && zip -q -9 -r -y "$DIST/Lobster-$VERSION-macos-arm64.zip" Lobster.app)
fi

echo "✓ dist/"; ls -lh "$DIST"
