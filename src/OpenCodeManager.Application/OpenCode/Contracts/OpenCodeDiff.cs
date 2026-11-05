namespace OpenCodeManager.Application;
public sealed record OpenCodeDiff(string File, string Patch, int Additions, int Deletions, string Status);
