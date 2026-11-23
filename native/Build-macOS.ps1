#Requires -Version 7
[CmdletBinding()]
param([Parameter(Mandatory)] [ValidateSet('arm64', 'x86_64')] [string] $Architecture)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if (!$IsMacOS) { throw 'Building the Arcdini dylib requires macOS and the Xcode command-line tools.' }

$rid = if ($Architecture -eq 'arm64') { 'osx-arm64' } else { 'osx-x64' }
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root artifacts
$builds = Join-Path $artifacts "native-build/$rid"
$prefix = Join-Path $builds dependencies
$output = Join-Path $artifacts "native/$rid"
New-Item -ItemType Directory -Force $builds, $prefix, $output | Out-Null
$sources = & (Join-Path $PSScriptRoot Prepare-Sources.ps1) -Artifacts $builds

function Configure-Native([string] $Name, [string] $Source, [string[]] $Options) {
    cmake -S $Source -B (Join-Path $builds $Name) "-DCMAKE_INSTALL_PREFIX=$prefix" `
        "-DCMAKE_OSX_ARCHITECTURES=$Architecture" '-DCMAKE_OSX_DEPLOYMENT_TARGET=12.0' `
        '-DCMAKE_BUILD_TYPE=Release' '-DCMAKE_POSITION_INDEPENDENT_CODE=ON' @Options
}

function Install-Native([string] $Name, [string] $Source, [string[]] $Options) {
    Configure-Native $Name $Source $Options
    cmake --build (Join-Path $builds $Name) --target install --parallel
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

Configure-Native libarchive (Join-Path $sources libarchive-3.8.11) @(
    "-DCMAKE_PREFIX_PATH=$prefix", '-DBUILD_SHARED_LIBS=ON',
    "-DZLIB_INCLUDE_DIR=$prefix/include", "-DZLIB_LIBRARY_RELEASE=$prefix/lib/libz.a",
    "-DBZIP2_INCLUDE_DIR=$prefix/include", "-DBZIP2_LIBRARY_RELEASE=$prefix/lib/libbz2.a",
    "-DLIBLZMA_INCLUDE_DIR=$prefix/include", "-DLIBLZMA_LIBRARY_RELEASE=$prefix/lib/liblzma.a",
    "-DZSTD_INCLUDE_DIR=$prefix/include", "-DZSTD_LIBRARY=$prefix/lib/libzstd.a",
    '-DENABLE_TEST=OFF', '-DENABLE_INSTALL=OFF', '-DENABLE_TAR=OFF', '-DENABLE_CPIO=OFF',
    '-DENABLE_CAT=OFF', '-DENABLE_UNZIP=OFF', '-DENABLE_OPENSSL=OFF', '-DENABLE_MBEDTLS=OFF',
    '-DENABLE_NETTLE=OFF', '-DENABLE_LIBB2=OFF', '-DENABLE_LZ4=OFF', '-DENABLE_LZO=OFF',
    '-DENABLE_LIBXML2=OFF', '-DENABLE_BSDXML=OFF', '-DENABLE_EXPAT=OFF',
    '-DENABLE_PCREPOSIX=OFF', '-DENABLE_PCRE2POSIX=OFF', '-DENABLE_ACL=OFF', '-DENABLE_XATTR=OFF')

$archiveBuild = Join-Path $builds libarchive
$config = Get-Content -LiteralPath (Join-Path $archiveBuild config.h) -Raw
foreach ($feature in 'HAVE_LIBZ', 'HAVE_LIBBZ2', 'HAVE_LIBLZMA', 'HAVE_LIBZSTD') {
    if ($config -notmatch "(?m)^#define $feature 1\s*$") { throw "libarchive is missing $feature." }
}
cmake --build $archiveBuild --target archive --parallel

$library = Join-Path $archiveBuild libarchive/libarcdini-libarchive.dylib
$slice = & lipo -archs $library
if ($slice -ne $Architecture) { throw "Unexpected dylib architecture: $slice." }
$symbols = & nm -gU $library
if ($symbols -match 'archive_read_support_format_rar5?$') { throw 'The dylib still exports a RAR reader.' }

& (Join-Path $PSScriptRoot Write-Notices.ps1) -SourceDirectory $sources -OutputFile (Join-Path $output THIRD-PARTY-NOTICES.txt)
# Install the regular file, even when CMake uses a versioned dylib and a symlink.
& /bin/cp -L $library (Join-Path $output libarcdini-libarchive.dylib)
