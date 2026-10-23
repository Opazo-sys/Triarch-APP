namespace Triarch;

public sealed class LibGame
{
	public string Store { get; init; } = "";

	public string Id { get; init; } = "";

	public string Name { get; init; } = "";

	public string InstallDir { get; init; } = "";

	public string? ExePath { get; set; }

	public string? LaunchUri { get; init; }

	public string Args { get; init; } = "";

	public string Key => Store + ":" + Id;

	public string Subtitle => Store;

	public bool IsSteam => Store == "Steam";
}
