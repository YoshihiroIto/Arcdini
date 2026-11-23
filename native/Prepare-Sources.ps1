#Requires -Version 7
param([Parameter(Mandatory)] [string] $Artifacts)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$downloads = Join-Path $Artifacts downloads
$sources = Join-Path $Artifacts native-source
New-Item -ItemType Directory -Force $downloads, $sources | Out-Null
$curl = if ($IsWindows) { 'curl.exe' } else { 'curl' }
$tar = if ($IsWindows) { 'tar.exe' } else { 'tar' }

$dependencies = @(
    @{ Name = 'libarchive-3.8.11'; Url = 'https://codeload.github.com/libarchive/libarchive/tar.gz/refs/tags/v3.8.11'; Sha256 = '6E9560B2E0659FF688C5EFB7717A68BB06B28EF65A69ADDCD6C424EAF9C319B1' }
    @{ Name = 'zlib-1.3.2'; Url = 'https://codeload.github.com/madler/zlib/tar.gz/refs/tags/v1.3.2'; Sha256 = 'B99A0B86C0BA9360EC7E78C4F1E43B1CBDF1E6936C8FA0F6835C0CD694A495A1' }
    @{ Name = 'bzip2-1.0.8'; Url = 'https://sourceware.org/pub/bzip2/bzip2-1.0.8.tar.gz'; Sha256 = 'AB5A03176EE106D3F0FA90E381DA478DDAE405918153CCA248E682CD0C4A2269' }
    @{ Name = 'xz-5.8.3'; Url = 'https://codeload.github.com/tukaani-project/xz/tar.gz/refs/tags/v5.8.3'; Sha256 = '8EC1767FA517642ECB4CF08B891CE667BA6F143551E382B07C7EF437BDA335E2' }
    @{ Name = 'zstd-1.5.7'; Url = 'https://codeload.github.com/facebook/zstd/tar.gz/refs/tags/v1.5.7'; Sha256 = '37D7284556B20954E56E1CA85B80226768902E2EDABD3B649E9E72C0C9012EE3' }
)

foreach ($dependency in $dependencies) {
    $file = Join-Path $downloads "$($dependency.Name).tar.gz"
    if (!(Test-Path -LiteralPath $file)) {
        $partial = "$file.partial"
        & $curl --fail --silent --show-error --location --retry 2 --output $partial $dependency.Url
        if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $dependency.Sha256) {
            throw "SHA-256 mismatch for $($dependency.Name)."
        }
        Move-Item -LiteralPath $partial -Destination $file
    }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $dependency.Sha256) {
        throw "SHA-256 mismatch for $file."
    }
    & $tar -xf $file -C $sources
}

function Update-SourceText([string] $File, [string] $Pattern, [string] $Replacement = '') {
    $sourceText = [IO.File]::ReadAllText($File)
    $match = [regex]::Match($sourceText, $Pattern)
    if (!$match.Success) { throw "Expected source in $File was not found: $Pattern" }
    [IO.File]::WriteAllText($File, $sourceText.Remove($match.Index, $match.Length).Insert($match.Index, $Replacement))
}

$archive = Join-Path $sources libarchive-3.8.11
$libarchive = Join-Path $archive libarchive
$cmakeFile = Join-Path $libarchive CMakeLists.txt
Update-SourceText $cmakeFile '(?m)^  archive_read_support_format_rar\.c\r?\n  archive_read_support_format_rar5\.c\r?\n'
# The BLAKE2 reference implementation is only used by the RAR5 reader.
Update-SourceText $cmakeFile '(?ms)^IF\(ARCHIVE_BLAKE2\).*?^ENDIF\(ARCHIVE_BLAKE2\)\r?\n'
Update-SourceText (Join-Path $libarchive archive_read_support_format_all.c) `
    '(?m)^\tarchive_read_support_format_rar\(a\);\r?\n\tarchive_read_support_format_rar5\(a\);\r?\n'
Update-SourceText (Join-Path $libarchive archive_read_support_format_by_code.c) `
    '(?m)^\tcase ARCHIVE_FORMAT_RAR:\r?\n\t\treturn archive_read_support_format_rar\(a\);\r?\n\tcase ARCHIVE_FORMAT_RAR_V5:\r?\n\t\treturn archive_read_support_format_rar5\(a\);\r?\n'

# Older Filedini ZIPs written by .NET on Windows carry Unix link bits in DOS entries.
$zipSource = Join-Path $libarchive archive_read_support_format_zip.c
Update-SourceText $zipSource `
    '(?m)^\t\tif \(zip_entry->system == 3\) \{' `
    '        if (zip_entry->system == 3 || (zip_entry->system == 0 &&
            ((external_attributes >> 16) & AE_IFMT) == AE_IFLNK)) {'

# Filedini wrote the link payload in UTF-8 independently of the filename's encoding.
$legacyLinkCharset = @'
        if (zip_entry->system == 0) {
            const unsigned char *target = p;
            if (linkname_full_length >= 3 &&
                target[0] == 0xef && target[1] == 0xbb && target[2] == 0xbf) {
                p = target + 3;
                linkname_full_length -= 3;
            }
            if (zip->sconv_utf8 == NULL) {
                zip->sconv_utf8 = archive_string_conversion_from_charset(
                    &a->archive, "UTF-8", 1);
                if (zip->sconv_utf8 == NULL)
                    return ARCHIVE_FATAL;
            }
            sconv = zip->sconv_utf8;
        }
'@
Update-SourceText $zipSource `
    '(?m)(?<=^\t\t\tsconv = zip->sconv_default;\r?$)\r?\n(?=\t\tif \(archive_entry_copy_symlink_l)' `
    ("`n" + $legacyLinkCharset + "`n")

Add-Content -LiteralPath $cmakeFile -Value 'set_target_properties(archive PROPERTIES OUTPUT_NAME "arcdini-libarchive")'

$sources
