#Requires -Version 7
[CmdletBinding()]
param([string] $Generator = 'Visual Studio 18 2026')

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root artifacts
$downloads = Join-Path $artifacts downloads
$sources = Join-Path $artifacts native-source
$builds = Join-Path $artifacts native-build
$prefix = Join-Path $artifacts native-dependencies
$output = Join-Path $artifacts native/win-x64
New-Item -ItemType Directory -Force $downloads, $sources, $builds, $prefix, $output | Out-Null

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
        curl.exe --fail --silent --show-error --location --retry 2 --output $partial $dependency.Url
        if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $dependency.Sha256) {
            throw "SHA-256 mismatch for $($dependency.Name)."
        }
        Move-Item -LiteralPath $partial -Destination $file
    }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $dependency.Sha256) {
        throw "SHA-256 mismatch for $file."
    }
    tar.exe -xf $file -C $sources
}

function Configure-Native([string] $Name, [string] $Source, [string[]] $Options) {
    $build = Join-Path $builds $Name
    cmake -S $Source -B $build -G $Generator -A x64 "-DCMAKE_INSTALL_PREFIX=$prefix" `
        '-DCMAKE_POLICY_DEFAULT_CMP0091=NEW' '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded' @Options
}

function Install-Native([string] $Name, [string] $Source, [string[]] $Options) {
    Configure-Native $Name $Source $Options
    cmake --build (Join-Path $builds $Name) --config Release --target INSTALL --parallel
}

Install-Native zlib (Join-Path $sources zlib-1.3.2) @(
    '-DZLIB_BUILD_SHARED=OFF', '-DZLIB_BUILD_TESTING=OFF')
Install-Native bzip2 (Join-Path $PSScriptRoot bzip2) @(
    "-DBZIP2_SOURCE=$(Join-Path $sources bzip2-1.0.8)")
Install-Native xz (Join-Path $sources xz-5.8.3) @(
    '-DBUILD_SHARED_LIBS=OFF', '-DBUILD_TESTING=OFF', '-DXZ_TOOL_XZ=OFF',
    '-DXZ_TOOL_XZDEC=OFF', '-DXZ_TOOL_LZMADEC=OFF', '-DXZ_TOOL_LZMAINFO=OFF',
    '-DXZ_TOOL_SCRIPTS=OFF', '-DXZ_NLS=OFF', '-DXZ_DOC=OFF')
Install-Native zstd (Join-Path $sources zstd-1.5.7/build/cmake) @(
    '-DZSTD_BUILD_SHARED=OFF', '-DZSTD_BUILD_PROGRAMS=OFF', '-DZSTD_BUILD_TESTS=OFF')

function Remove-SourceText([string] $File, [string] $Pattern) {
    $sourceText = [IO.File]::ReadAllText($File)
    $match = [regex]::Match($sourceText, $Pattern)
    if (!$match.Success) { throw "Expected source in $File was not found: $Pattern" }
    [IO.File]::WriteAllText($File, $sourceText.Remove($match.Index, $match.Length))
}

$archive = Join-Path $sources libarchive-3.8.11
$libarchive = Join-Path $archive libarchive
$cmakeFile = Join-Path $libarchive CMakeLists.txt
Remove-SourceText $cmakeFile '(?m)^  archive_read_support_format_rar\.c\r?\n  archive_read_support_format_rar5\.c\r?\n'
# The BLAKE2 reference implementation is only used by the RAR5 reader.
Remove-SourceText $cmakeFile '(?ms)^IF\(ARCHIVE_BLAKE2\).*?^ENDIF\(ARCHIVE_BLAKE2\)\r?\n'
Remove-SourceText (Join-Path $libarchive archive_read_support_format_all.c) `
    '(?m)^\tarchive_read_support_format_rar\(a\);\r?\n\tarchive_read_support_format_rar5\(a\);\r?\n'
Remove-SourceText (Join-Path $libarchive archive_read_support_format_by_code.c) `
    '(?m)^\tcase ARCHIVE_FORMAT_RAR:\r?\n\t\treturn archive_read_support_format_rar\(a\);\r?\n\tcase ARCHIVE_FORMAT_RAR_V5:\r?\n\t\treturn archive_read_support_format_rar5\(a\);\r?\n'

Add-Content -LiteralPath $cmakeFile -Value 'set_target_properties(archive PROPERTIES OUTPUT_NAME "arcdini-libarchive")'
Configure-Native libarchive $archive @(
    "-DCMAKE_PREFIX_PATH=$prefix", '-DBUILD_SHARED_LIBS=ON', '-DMSVC_USE_STATIC_CRT=ON',
    "-DZLIB_INCLUDE_DIR=$prefix/include", "-DZLIB_LIBRARY_RELEASE=$prefix/lib/zs.lib",
    "-DBZIP2_INCLUDE_DIR=$prefix/include", "-DBZIP2_LIBRARY_RELEASE=$prefix/lib/bz2.lib",
    "-DLIBLZMA_INCLUDE_DIR=$prefix/include", "-DLIBLZMA_LIBRARY_RELEASE=$prefix/lib/lzma.lib",
    "-DZSTD_INCLUDE_DIR=$prefix/include", "-DZSTD_LIBRARY=$prefix/lib/zstd_static.lib",
    '-DENABLE_TEST=OFF', '-DENABLE_INSTALL=OFF', '-DENABLE_TAR=OFF', '-DENABLE_CPIO=OFF',
    '-DENABLE_CAT=OFF', '-DENABLE_UNZIP=OFF', '-DENABLE_OPENSSL=OFF', '-DENABLE_LIBB2=OFF',
    '-DENABLE_LZ4=OFF', '-DENABLE_LIBXML2=OFF', '-DENABLE_BSDXML=OFF', '-DENABLE_EXPAT=OFF',
    '-DENABLE_WIN32_XMLLITE=OFF', '-DENABLE_PCREPOSIX=OFF', '-DENABLE_PCRE2POSIX=OFF',
    '-DENABLE_ICONV=OFF', '-DENABLE_ACL=OFF', '-DENABLE_XATTR=OFF', '-DENABLE_CNG=ON')

$archiveBuild = Join-Path $builds libarchive
$project = Join-Path $archiveBuild libarchive/archive.vcxproj
$compileUnits = Select-Xml -Path $project -XPath "//*[local-name()='ClCompile']/@Include"
if ($compileUnits.Node.Value -match 'archive_read_support_format_rar5?\.c$') {
    throw 'The native project still compiles a RAR reader.'
}
$config = Get-Content -LiteralPath (Join-Path $archiveBuild config.h) -Raw
foreach ($feature in 'HAVE_LIBZ', 'HAVE_LIBBZ2', 'HAVE_LIBLZMA', 'HAVE_LIBZSTD', 'HAVE_BCRYPT_H') {
    if ($config -notmatch "(?m)^#define $feature 1\s*$") { throw "libarchive is missing $feature." }
}
cmake --build $archiveBuild --config Release --target archive --parallel

& (Join-Path $PSScriptRoot Write-Notices.ps1) -SourceDirectory $sources -OutputFile (Join-Path $output THIRD-PARTY-NOTICES.txt)
Copy-Item -LiteralPath (Join-Path $archiveBuild bin/Release/arcdini-libarchive.dll) -Destination $output
