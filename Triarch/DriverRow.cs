namespace Triarch;

public sealed class DriverRow
{
	public string Device { get; init; } = "";

	public string Class { get; init; } = "";

	public string Vendor { get; init; } = "";

	public string Version { get; init; } = "";

	public string Date { get; init; } = "";

	public bool Signed { get; set; }

	public string SignedText
	{
		get
		{
			if (!Signed)
			{
				return "sem assinatura";
			}
			return "assinado";
		}
	}
}
