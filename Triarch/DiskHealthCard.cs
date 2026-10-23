using System.Collections.Generic;

namespace Triarch;

public sealed class DiskHealthCard
{
	public int Index { get; init; }

	public string Title { get; init; } = "";

	public string Meta { get; init; } = "";

	public string Serial { get; init; } = "";

	public DiskVerdict Verdict { get; init; } = DiskVerdict.Unknown;

	public string? Label { get; init; }

	public string VerdictText
	{
		get
		{
			string text = Label;
			if (text == null)
			{
				text = Verdict switch
				{
					DiskVerdict.Good => "SAUDÁVEL", 
					DiskVerdict.Warning => "ATENÇÃO", 
					DiskVerdict.Critical => "CRÍTICO", 
					_ => "SEM DADOS", 
				};
			}
			return text;
		}
	}

	public bool IsAlert => Verdict != DiskVerdict.Good;

	public string Reasons { get; init; } = "";

	public List<KeyValuePair<string, string>> Metrics { get; init; } = new List<KeyValuePair<string, string>>();
}
