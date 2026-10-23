using System;

namespace Triarch;

public sealed record BenchRun(DateTime Date, double? Multi, double? Single, bool Sustained, string Cpu);
