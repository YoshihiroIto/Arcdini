#Requires -Version 7
param(
    [Parameter(Mandatory)] [string] $SourceDirectory,
    [Parameter(Mandatory)] [string] $OutputFile
)

$ErrorActionPreference = 'Stop'
$notice = [Text.StringBuilder]::new()
[void] $notice.AppendLine('Third-party notices for the Arcdini native library')

function Add-License([string] $Name, [string] $File) {
    [void] $notice.AppendLine().AppendLine($Name).AppendLine(('=' * $Name.Length))
    [void] $notice.AppendLine([IO.File]::ReadAllText((Join-Path $SourceDirectory $File)).TrimEnd())
}

function Add-SourceNotices([string] $Directory, [string] $Exclude = '^$') {
    $directoryPath = Join-Path $SourceDirectory $Directory
    $byNotice = [ordered]@{}
    foreach ($file in (Get-ChildItem -LiteralPath $directoryPath -Recurse -File | Sort-Object FullName)) {
        $relative = [IO.Path]::GetRelativePath($directoryPath, $file.FullName).Replace('\', '/')
        if ($file.Extension -notin '.c', '.h', '.S' -or $relative -match $Exclude) { continue }
        $sourceText = [IO.File]::ReadAllText($file.FullName)
        foreach ($comment in [regex]::Matches($sourceText, '(?s)/\*.*?\*/|(?m)(?:^[ \t]*//[^\n]*(?:\n|$))+')) {
            if ($comment.Value -notmatch '(?i)\bcopyright\b|SPDX-License-Identifier|redistribution and use in source|public domain|permission to use, copy') { continue }
            $header = ($comment.Value -replace '\r\n', "`n").Trim()
            if (!$byNotice.Contains($header)) {
                $byNotice[$header] = [Collections.Generic.List[string]]::new()
            }
            $byNotice[$header].Add($relative)
        }
    }
    foreach ($header in $byNotice.Keys) {
        [void] $notice.AppendLine().AppendLine(($byNotice[$header] -join ', ')).AppendLine($header)
    }
}

Add-License 'libarchive 3.8.11' 'libarchive-3.8.11/COPYING'
Add-SourceNotices 'libarchive-3.8.11/libarchive' '^(test/|archive_read_support_format_rar5?\.c$|archive_blake2)'
Add-License 'zlib 1.3.2' 'zlib-1.3.2/LICENSE'
Add-SourceNotices 'zlib-1.3.2' '(/|^example\.c$|^minigzip\.c$)'
Add-License 'bzip2 1.0.8' 'bzip2-1.0.8/LICENSE'
Add-License 'liblzma from XZ Utils 5.8.3' 'xz-5.8.3/COPYING.0BSD'
Add-SourceNotices 'xz-5.8.3/src/liblzma'
Add-SourceNotices 'xz-5.8.3/src/common'
Add-License 'Zstandard 1.5.7 (BSD license)' 'zstd-1.5.7/LICENSE'
Add-SourceNotices 'zstd-1.5.7/lib' '^dll/'
[IO.File]::WriteAllText($OutputFile, $notice.ToString())
