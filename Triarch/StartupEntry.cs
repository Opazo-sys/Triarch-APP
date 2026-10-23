namespace Triarch;

public sealed class StartupEntry
{
	public string Name { get; init; } = "";

	public string Command { get; init; } = "";

	public string Source { get; init; } = "";

	public bool Machine { get; init; }

	public bool Enabled { get; set; }

	public string Detail { get; init; } = "";

	public bool FileMissing { get; init; }

	internal string Kind { get; init; } = "";

	internal string? FilePath { get; init; }

	public string State
	{
		get
		{
			if (!FileMissing)
			{
				if (!Enabled)
				{
					return "DESATIVADO";
				}
				return "ATIVADO";
			}
			return "ARQUIVO NÃO EXISTE";
		}
	}
}
