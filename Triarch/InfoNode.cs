using System.Collections.Generic;

namespace Triarch;

public class InfoNode
{
	public string Header { get; set; } = "";

	public string Value { get; set; } = "";

	public List<InfoNode> Children { get; } = new List<InfoNode>();

	public string Display
	{
		get
		{
			if (!string.IsNullOrEmpty(Value))
			{
				return Header + ": " + Value;
			}
			return Header;
		}
	}

	public bool IsGroup => Children.Count > 0;

	public InfoNode Add(string header, string value = "")
	{
		InfoNode n = new InfoNode
		{
			Header = header,
			Value = value
		};
		Children.Add(n);
		return n;
	}
}
