using System.Collections.Generic;

namespace Triarch;

public sealed class GameProfile
{
	public string Priority { get; set; } = "normal";

	public bool HighPerfPlan { get; set; }

	public bool GpuHigh { get; set; }

	public bool DisableFso { get; set; }

	public string? ExePath { get; set; }

	public string WinMode { get; set; } = "default";

	public string Res { get; set; } = "default";

	public int Hz { get; set; }

	public string Style { get; set; } = "source";

	public string Extra { get; set; } = "";

	public List<HelperProgram> Helpers { get; set; } = new List<HelperProgram>();
}
