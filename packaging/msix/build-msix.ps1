# Builds the Microsoft Store package (Windows, .NET 8 SDK + Windows SDK).
#   ./packaging/msix/build-msix.ps1 -Identity 12345FreiaTech.Lobster -Publisher "CN=XXXXXXXX-..." -PublisherDisplay "Freia tech"
# The three values are on Partner Center › Lobster › Product identity. The Store signs the package itself.
param(
  [string]$Identity = $(if ($env:MSIX_IDENTITY) { $env:MSIX_IDENTITY } else { 'FreiaTech.Lobster' }),
  [string]$Publisher = $(if ($env:MSIX_PUBLISHER) { $env:MSIX_PUBLISHER } else { 'CN=Freia tech' }),
  [string]$PublisherDisplay = $(if ($env:MSIX_PUBLISHER_DISPLAY) { $env:MSIX_PUBLISHER_DISPLAY } else { 'Freia tech' })
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/../.."
Set-Location $root
$version = ([xml](Get-Content src/Lobster.Core/Lobster.Core.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$layout = Join-Path $root '.publish/msix'
Remove-Item -Recurse -Force $layout -ErrorAction SilentlyContinue

dotnet publish src/Lobster.Windows -c Release --nologo -v quiet -p:Store=true -o $layout
Copy-Item -Recurse packaging/msix/Assets (Join-Path $layout 'Assets')

# "Open with › Lobster" for every type Lobster handles (one group per kind; MultiSelectModel=Player sends the whole selection).
$groups = [ordered]@{
  'lobster-image'    = 'png jpg jpeg jfif webp avif heic heif gif bmp tif tiff ico svg psd tga jxl cr2 cr3 nef arw dng raf orf rw2'
  'lobster-video'    = 'mp4 m4v mov mkv webm avi wmv flv mpg mpeg ts mts m2ts 3gp ogv'
  'lobster-audio'    = 'mp3 m4a aac wav flac ogg oga opus wma aiff aif amr m4b'
  'lobster-document' = 'pdf doc docx odt rtf txt html htm md xls xlsx ods csv ppt pptx odp'
}
$ft = foreach ($g in $groups.GetEnumerator()) {
  $types = ($g.Value -split ' ' | ForEach-Object { "                <uap:FileType>.$_</uap:FileType>" }) -join "`n"
@"
        <uap3:Extension Category="windows.fileTypeAssociation">
          <uap3:FileTypeAssociation Name="$($g.Key)" desktop4:MultiSelectModel="Player">
            <uap:DisplayName>Lobster</uap:DisplayName>
            <uap:Logo>Assets\Square44x44Logo.png</uap:Logo>
            <uap:SupportedFileTypes>
$types
            </uap:SupportedFileTypes>
          </uap3:FileTypeAssociation>
        </uap3:Extension>
"@
}
$esc = [System.Security.SecurityElement]
(Get-Content packaging/msix/AppxManifest.xml -Raw) `
  -replace '@IDENTITY@', $esc::Escape($Identity) -replace '@PUBLISHER@', $esc::Escape($Publisher) `
  -replace '@PUBLISHER_DISPLAY@', $esc::Escape($PublisherDisplay) -replace '@VERSION@', $version `
  -replace '@FILE_TYPES@', ($ft -join "`n") |
  Set-Content (Join-Path $layout 'AppxManifest.xml') -Encoding utf8

$kits = 'C:\Program Files (x86)\Windows Kits\10\bin'
$makeappx = Get-ChildItem $kits -Recurse -Filter makeappx.exe | Where-Object FullName -match '\\x64\\' | Sort-Object FullName -Descending | Select-Object -First 1
$makepri = Join-Path $makeappx.DirectoryName 'makepri.exe'
Push-Location $layout
& $makepri createconfig /cf priconfig.xml /dq en-US_fr-FR /o | Out-Null
& $makepri new /pr . /cf priconfig.xml /mn AppxManifest.xml /of resources.pri /o | Out-Null
Remove-Item priconfig.xml
Pop-Location

New-Item -ItemType Directory -Force dist | Out-Null
$out = "dist/Lobster-$version-windows-x64.msix"
& $makeappx.FullName pack /d $layout /p $out /o
if ($LASTEXITCODE) { throw "makeappx failed ($LASTEXITCODE)" }
Write-Host "✓ $out"
