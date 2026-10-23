using System.Collections.Generic;

namespace Triarch;

public sealed record FrameStats(int ProcessId, string ProcessName, double Fps, double FrameTimeMs, double Low1Fps, IReadOnlyList<double> RecentFrameTimes);
