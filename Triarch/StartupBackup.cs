namespace Triarch;

public sealed class StartupBackup
{
	public string Kind { get; set; } = "";

	public bool Machine { get; set; }

	public string Name { get; set; } = "";

	public string? Command { get; set; }

	public string? Backup { get; set; }

	public string? Original { get; set; }
}
