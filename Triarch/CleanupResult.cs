namespace Triarch;

public sealed record CleanupResult(long FreedBytes, int Trimmed, bool StandbyPurged, string? StandbyError);
