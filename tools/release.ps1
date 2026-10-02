# Builds the self-contained zip and publishes it as a GitHub release for the version in the csproj.
# The release notes are that version's section of CHANGELOG.md. Requires the GitHub CLI (gh), logged in.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

[xml]$project = Get-Content 'src/LastEpochHelper/LastEpochHelper.csproj'
$version = ($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'No <Version> in the csproj.' }
$tag = "v$version"

if (git status --porcelain) { throw 'Uncommitted changes - commit and push first.' }
if (git tag --list $tag) { throw "Tag $tag already exists - bump <Version>." }

# The changelog section for this version becomes the release text.
$lines = Get-Content 'CHANGELOG.md'
$start = ($lines | Select-String -Pattern "^##\s+v?$([regex]::Escape($version))(\s|$)" | Select-Object -First 1).LineNumber
if (-not $start) { throw "CHANGELOG.md has no '## $version' section." }
$rest = $lines[$start..($lines.Count - 1)]
$end = ($rest | Select-String -Pattern '^##\s' | Select-Object -First 1).LineNumber
$notes = if ($end) { $rest[0..($end - 2)] } else { $rest }
$title = ($lines[$start - 1] -replace '^##\s+', '').Trim()

dotnet test
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

# "Report a bug" can only send when the address file is present at build time (it is not in git).
if (Test-Path 'report-endpoint.local.txt') { Write-Host 'Bug reports: address found, this release can send them.' }
else { Write-Warning 'Bug reports: no report-endpoint.local.txt - this release will save reports to the desktop instead of sending.' }
if (-not (Test-Path 'usage-endpoint.local.txt')) { Write-Warning 'Country: no usage-endpoint.local.txt - this release will not send the country code.' }

$out = 'release/LastEpochHelper'
if (Test-Path $out) { Get-ChildItem $out -Recurse | Remove-Item -Recurse -Force }
dotnet publish src/LastEpochHelper -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item README.md $out -Force

$zip = "release/LastEpochHelper-$version.zip"
Compress-Archive $out $zip -Force

$notesFile = Join-Path ([IO.Path]::GetTempPath()) "leh-notes-$version.md"
($notes -join "`n").Trim() | Set-Content $notesFile -Encoding utf8
git push
gh release create $tag $zip --title $title --notes-file $notesFile
Write-Host "Released $tag"
