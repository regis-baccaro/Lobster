Convert and edit files by dragging them while holding Shift: a circular menu appears under the cursor, release on an action. Everything runs locally.

## Downloads

| Platform | File | Notes |
|---|---|---|
| **Windows 10/11** | `Lobster-@VERSION@-windows-x64.exe` | Self-contained. Run it; it installs itself for the current user (no admin rights). |
| Windows (light) | `Lobster-@VERSION@-windows-x64-light.zip` | Smaller build for machines that already have the .NET 8 Desktop runtime. Unzip and run `Lobster.exe`. |
| **Linux x64** | `Lobster-@VERSION@-linux-x64.tar.gz` | `tar xzf Lobster-@VERSION@-linux-x64.tar.gz && cd lobster-@VERSION@ && ./install.sh` (no sudo). |
| **macOS (Apple Silicon)** | `Lobster-@VERSION@-macos-arm64.zip` | Unzip and move **Lobster.app** to Applications. |

## First launch

- **Windows:** the exe is not code-signed yet, so Windows may warn you. Either:
  - before launching, right-click the downloaded file > **Properties** > tick **Unblock** at the bottom of the General tab > **OK** (or in PowerShell: `Unblock-File .\Lobster-@VERSION@-windows-x64.exe`), or
  - on the "Windows protected your PC" screen, click **More info**, then **Run anyway**.

  If *Smart App Control* is on (Windows 11), unsigned apps cannot be started at all; a Microsoft Store version is on its way. Audio and video need ffmpeg, which Lobster offers to install through winget.
- **macOS:** the app is ad-hoc signed but not notarized. Right-click > **Open** the first time (on macOS 15: *System Settings > Privacy & Security > Open Anyway*). For Shift + drag, allow Lobster under *Input Monitoring* if asked. Engines: `brew install ffmpeg poppler`.
- **Linux:** Shift + drag works on X11 sessions; on Wayland, use the Lobster bubble. Engines: `ffmpeg`, `poppler-utils`, optionally `libreoffice` — `install.sh` prints the command for your distribution.

## Highlights

- Windows, Linux and macOS from one shared core
- English or French interface
- 8 radial menu palettes plus a custom color
- In-place update on Windows when launching a newer version
- Image, video, audio, PDF and document conversions, all local
