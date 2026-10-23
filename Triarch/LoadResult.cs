namespace Triarch;

public sealed record LoadResult(double SeqReadMBs, double SmallFilesMBs, double FilesPerSec, double MapLoadSeconds, string Drive);
