# bzip2.net
A pure C# implementation of the bzip2 compressor

Based on the Java implementation by Matthew Francis: https://github.com/MateuszBartosiewicz/bzip2

The compression algorithm doesn't generate randomized blocks, which is already a deprecated option and may not be decoded by modern bzip2 libraries. Other popular .net compression libraries do generate randomized blocks.

Nuget package: [nuget.org/packages/bzip2.net](https://www.nuget.org/packages/bzip2.net/), or simply look for bzip2.net in Visual Studio's nuget package manager
