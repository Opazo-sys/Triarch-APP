using System;
using System.Collections.Generic;
using System.Linq;

namespace Triarch;

public sealed class VdfNode
{
	public List<KeyValuePair<string, object>> Items { get; } = new List<KeyValuePair<string, object>>();

	public IEnumerable<KeyValuePair<string, VdfNode>> Children => from i in Items
		where i.Value is VdfNode
		select new KeyValuePair<string, VdfNode>(i.Key, (VdfNode)i.Value);

	public string? Get(string key)
	{
		return Items.FirstOrDefault((KeyValuePair<string, object> i) => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && i.Value is string).Value as string;
	}

	public VdfNode? Child(string key)
	{
		return Items.FirstOrDefault((KeyValuePair<string, object> i) => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && i.Value is VdfNode).Value as VdfNode;
	}

	public VdfNode GetOrAddChild(string key)
	{
		VdfNode c = Child(key);
		if (c != null)
		{
			return c;
		}
		c = new VdfNode();
		Items.Add(new KeyValuePair<string, object>(key, c));
		return c;
	}

	public void Set(string key, string value)
	{
		for (int i = 0; i < Items.Count; i++)
		{
			if (Items[i].Key.Equals(key, StringComparison.OrdinalIgnoreCase) && Items[i].Value is string)
			{
				Items[i] = new KeyValuePair<string, object>(Items[i].Key, value);
				return;
			}
		}
		Items.Add(new KeyValuePair<string, object>(key, value));
	}

	public void Remove(string key)
	{
		Items.RemoveAll((KeyValuePair<string, object> i) => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && i.Value is string);
	}
}
