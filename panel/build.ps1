# Build the single-folder desktop app: embeds frontend + backend into a C# WinForms/WebView2 host.
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1
#
# Dependency (auto-downloaded once into .cache\, not committed):
#   Microsoft.Web.WebView2 SDK - provides Core.dll / WinForms.dll and WebView2Loader.dll
#   The runtime itself is NOT bundled: we use the system Evergreen WebView2 Runtime.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }

# ---------------------------------------------------------------- WebView2 SDK
$cache = Join-Path $PSScriptRoot '.cache\webview2'
$coreDll = Join-Path $cache 'Microsoft.Web.WebView2.Core.dll'
$wfDll = Join-Path $cache 'Microsoft.Web.WebView2.WinForms.dll'
$loaderDll = Join-Path $cache 'WebView2Loader.dll'

if (-not (Test-Path $coreDll) -or -not (Test-Path $wfDll) -or -not (Test-Path $loaderDll)) {
    Write-Host ' - downloading WebView2 SDK ...'
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    $nupkg = Join-Path $cache 'webview2.nupkg'
    # pinned version: upstream layout changes should not break our build silently
    $ver = '1.0.4314.35'
    $url = "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$ver/microsoft.web.webview2.$ver.nupkg"
    try {
        Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing -TimeoutSec 120
    } catch {
        throw "downloading WebView2 SDK failed (needs network once): $($_.Exception.Message)   manual: $url -> $cache"
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($nupkg)
    $map = @{
        'lib/net462/Microsoft.Web.WebView2.Core.dll'     = $coreDll
        'lib/net462/Microsoft.Web.WebView2.WinForms.dll' = $wfDll
        'runtimes/win-x64/native/WebView2Loader.dll'     = $loaderDll
    }
    foreach ($k in $map.Keys) {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq $k }
        if (-not $entry) { $zip.Dispose(); throw "nupkg is missing $k" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $map[$k], $true)
    }
    $zip.Dispose()
    Get-ChildItem $cache -File | Unblock-File -ErrorAction SilentlyContinue
    Write-Host ' - WebView2 SDK ready'
}

if (-not (Test-Path 'icon.ico')) { throw 'icon.ico missing (generate it with mkicon.cs)' }

# ---------------------------------------------------------------- compile
New-Item -ItemType Directory -Force -Path 'dist' | Out-Null
Remove-Item 'dist\MC-AI-Control.exe' -ErrorAction SilentlyContinue

$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/win32icon:icon.ico',
    '/out:dist\MC-AI-Control.exe',
    '/r:System.Windows.Forms.dll',
    '/r:System.Drawing.dll',
    "/r:$coreDll",
    "/r:$wfDll",
    '/resource:server.js,server.js',
    '/resource:renderer\index.html,index.html',
    '/resource:renderer\styles.css,styles.css',
    '/resource:renderer\app.js,app.js',
    '/resource:README.txt,README.txt',
    # WebView2 binaries are embedded too, so the shipped artifact is a single exe
    "/resource:$coreDll,wv2.core",
    "/resource:$wfDll,wv2.winforms",
    "/resource:$loaderDll,wv2.loader",
    'Program.cs',
    'AssemblyInfo.cs'
)

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "compile failed, exit code $LASTEXITCODE" }

# WebView2 SDK assemblies + native loader are embedded as resources (see Program.cs),
# so nothing else has to sit next to MC-AI-Control.exe.
Remove-Item 'dist\WebView2Loader.dll','dist\Microsoft.Web.WebView2.Core.dll','dist\Microsoft.Web.WebView2.WinForms.dll' -ErrorAction SilentlyContinue

# Ship node.exe too, so the target machine does not need a separate Node install.
$nodeSrc = (Get-Command node -ErrorAction SilentlyContinue).Source
if ($nodeSrc -and (Test-Path $nodeSrc)) {
    Copy-Item $nodeSrc 'dist\node.exe' -Force
    Write-Host ('   bundled node.exe: ' + $nodeSrc)
} else {
    Write-Host '   WARNING: node.exe not found, dist will not be portable (install Node.js on the target)'
}

$exe = Get-Item 'dist\MC-AI-Control.exe'
Write-Host ("BUILD OK: {0}  ({1:N0} KB)" -f $exe.FullName, ($exe.Length / 1KB))
Write-Host ("   SHA256: {0}" -f (Get-FileHash $exe.FullName -Algorithm SHA256).Hash)
Write-Host '   dist: MC-AI-Control.exe (single file, + optional node.exe for portability)'
