using System.Collections.Generic;

namespace Triarch;

public sealed record CloseResult(List<string> Closed, List<string> NotClosed);
