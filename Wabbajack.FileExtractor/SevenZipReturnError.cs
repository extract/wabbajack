using System;
using Wabbajack.Paths;
using Wabbajack.Paths.IO;

namespace Wabbajack.FileExtractor;

public class SevenZipReturnError : Exception
{
    private int ExitCode { get; }
    private new AbsolutePath SourcePath { get; }
    private TemporaryPath Dest { get; }

    public SevenZipReturnError(int exitCode, AbsolutePath source, TemporaryPath dest) : 
        base($"7Zip Extraction error, got: {exitCode} while extracting {source} to {dest}")
    {
        ExitCode = exitCode;
        SourcePath = source;
        Dest = dest;
    }
}