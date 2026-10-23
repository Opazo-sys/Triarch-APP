using System.Collections.Generic;

namespace Triarch;

public sealed record ReportNode(string Name, string? Value, List<ReportNode>? Children);
