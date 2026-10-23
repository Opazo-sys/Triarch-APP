using System.Collections.Generic;

namespace Triarch;

public sealed record ReportGpu(string Name, string Subtitle, List<ReportNode> Sections);
