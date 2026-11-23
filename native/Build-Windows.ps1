#Requires -Version 7
[CmdletBinding()]
param([string] $Generator = 'Visual Studio 18 2026')

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root artifacts
$builds = Join-Path $artifacts native-build
$prefix = Join-Path $artifacts native-dependencies
$output = Join-Path $artifacts native/win-x64
New-Item -ItemType Directory -Force $builds, $prefix, $output | Out-Null

$sources = & (Join-Path $PSScriptRoot Prepare-Sources.ps1) -Artifacts $artifacts

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

$archive = Join-Path $sources libarchive-3.8.11
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
