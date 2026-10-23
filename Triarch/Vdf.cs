using System.Collections.Generic;
using System.Text;

namespace Triarch;

public static class Vdf
{
	public static VdfNode Parse(string text)
	{
		int i = 0;
		return Block(text, ref i);
	}

	private static VdfNode Block(string s, ref int i)
	{
		VdfNode node = new VdfNode();
		while (true)
		{
			string key = Token(s, ref i, out var close);
			if (close || key == null)
			{
				break;
			}
			SkipSpace(s, ref i);
			if (i < s.Length && s[i] == '{')
			{
				i++;
				node.Items.Add(new KeyValuePair<string, object>(key, Block(s, ref i)));
			}
			else
			{
				string val = Token(s, ref i, out var _);
				node.Items.Add(new KeyValuePair<string, object>(key, val ?? ""));
			}
		}
		return node;
	}

	private static void SkipSpace(string s, ref int i)
	{
		while (i < s.Length)
		{
			if (char.IsWhiteSpace(s[i]))
			{
				i++;
				continue;
			}
			if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
			{
				while (i < s.Length && s[i] != '\n')
				{
					i++;
				}
				continue;
			}
			break;
		}
	}

	private static string? Token(string s, ref int i, out bool close)
	{
		close = false;
		SkipSpace(s, ref i);
		if (i >= s.Length)
		{
			return null;
		}
		if (s[i] == '}')
		{
			i++;
			close = true;
			return null;
		}
		if (s[i] == '"')
		{
			i++;
			StringBuilder sb = new StringBuilder();
			while (i < s.Length && s[i] != '"')
			{
				if (s[i] == '\\' && i + 1 < s.Length)
				{
					i++;
					StringBuilder stringBuilder = sb;
					char c = s[i];
					stringBuilder.Append(c switch
					{
						'n' => '\n', 
						't' => '\t', 
						'\\' => '\\', 
						'"' => '"', 
						_ => c, 
					});
				}
				else
				{
					sb.Append(s[i]);
				}
				i++;
			}
			i++;
			return sb.ToString();
		}
		int start = i;
		while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '{' && s[i] != '}' && s[i] != '"')
		{
			i++;
		}
		int num = start;
		return s.Substring(num, i - num);
	}

	private static string Esc(string v)
	{
		return v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")
			.Replace("\t", "\\t");
	}

	public static string Serialize(VdfNode root)
	{
		StringBuilder stringBuilder = new StringBuilder();
		Write(stringBuilder, root, 0);
		return stringBuilder.ToString();
	}

	private static void Write(StringBuilder sb, VdfNode n, int depth)
	{
		string ind = new string('\t', depth);
		foreach (var (k, v) in n.Items)
		{
			if (v is string str)
			{
				sb.Append(ind).Append('"').Append(Esc(k))
					.Append("\"\t\t\"")
					.Append(Esc(str))
					.Append("\"\n");
			}
			else if (v is VdfNode child)
			{
				sb.Append(ind).Append('"').Append(Esc(k))
					.Append("\"\n")
					.Append(ind)
					.Append("{\n");
				Write(sb, child, depth + 1);
				sb.Append(ind).Append("}\n");
			}
		}
	}
}
