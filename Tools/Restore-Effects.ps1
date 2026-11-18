param([string]$PackagePath)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$target = Join-Path $project 'Assets/ShadowClaw/Resources/ImportedVFX/FreeQuickEffectsVol1'
$lock = Get-Content (Join-Path $PSScriptRoot 'effects-lock.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$files = @($lock.files.PSObject.Properties)
function Matches($file, $hash) {
    (Test-Path -LiteralPath $file -PathType Leaf) -and ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -eq $hash)
}
$utf8 = New-Object System.Text.UTF8Encoding($false)
foreach ($folder in $lock.folderMetadata.PSObject.Properties) {
    $meta = Join-Path $target $folder.Name
    [IO.Directory]::CreateDirectory((Split-Path $meta -Parent)) | Out-Null
    if (!(Test-Path -LiteralPath $meta)) { [IO.File]::WriteAllText($meta, $folder.Value, $utf8) }
}
$missing = @($files | Where-Object { !(Matches (Join-Path $target $_.Name) $_.Value.sha256) })
if (!$missing.Count) { Write-Output 'Effects verified.'; return }
$work = Join-Path ([IO.Path]::GetTempPath()) ('shadowclaw-effects-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($work) | Out-Null
if (!$PackagePath) {
    $PackagePath = Join-Path $work 'effects.unitypackage'
    Invoke-WebRequest -Uri $lock.url -OutFile $PackagePath -UseBasicParsing
}
if (!(Matches $PackagePath $lock.packageSHA256)) { throw 'The package does not match Free Quick Effects Vol. 1 BIRP v1.0.' }
& tar -xf $PackagePath -C $work
if ($LASTEXITCODE) { throw 'Could not extract the effects archive.' }
foreach ($entry in $missing) {
    $file = Join-Path $target $entry.Name
    if (Test-Path -LiteralPath $file) { throw "Existing file has local changes: $file" }
    [IO.Directory]::CreateDirectory((Split-Path $file -Parent)) | Out-Null
    $content = Join-Path $work $entry.Value.member
    $suffix = $lock.materialSuffix.PSObject.Properties[$entry.Name]
    if ($suffix) { [IO.File]::AppendAllText($content, $suffix.Value, $utf8) }
    if (!(Matches $content $entry.Value.sha256)) { throw "Unexpected resource: $($entry.Name)" }
    Copy-Item -LiteralPath $content -Destination $file
}
Write-Output "Verified and restored $($files.Count) effect files."
