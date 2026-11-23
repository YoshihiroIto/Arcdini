cmake_minimum_required(VERSION 3.20)
project(arcdini_bzip2 LANGUAGES C)

file(TO_CMAKE_PATH "${BZIP2_SOURCE}" BZIP2_SOURCE)
add_library(bz2 STATIC
    "${BZIP2_SOURCE}/blocksort.c"
    "${BZIP2_SOURCE}/huffman.c"
    "${BZIP2_SOURCE}/crctable.c"
    "${BZIP2_SOURCE}/randtable.c"
    "${BZIP2_SOURCE}/compress.c"
    "${BZIP2_SOURCE}/decompress.c"
    "${BZIP2_SOURCE}/bzlib.c")
target_compile_definitions(bz2 PRIVATE _CRT_SECURE_NO_WARNINGS)
install(TARGETS bz2 ARCHIVE DESTINATION lib)
install(FILES "${BZIP2_SOURCE}/bzlib.h" DESTINATION include)
