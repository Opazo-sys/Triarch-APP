using System;
using System.ComponentModel;

namespace Triarch;

public sealed class Tweak : INotifyPropertyChanged
{
	private bool _isOn;

	private string _status = "";

	public string Id { get; init; } = "";

	public string Title { get; init; } = "";

	public string Description { get; init; } = "";

	public string Effect { get; init; } = "";

	public bool NeedsRestart { get; init; }

	internal Func<bool?> Read { get; init; } = () => (bool?)null;

	internal Action<bool, AppSettings> Write { get; init; } = (bool _, AppSettings _) =>
	{
	};

	internal Action<AppSettings> Restore { get; init; } = (AppSettings _) =>
	{
	};

	public bool IsOn
	{
		get
		{
			return _isOn;
		}
		set
		{
			_isOn = value;
			Changed("IsOn");
		}
	}

	public string Status
	{
		get
		{
			return _status;
		}
		set
		{
			_status = value;
			Changed("Status");
		}
	}

	public string Badge
	{
		get
		{
			if (!NeedsRestart)
			{
				return "";
			}
			return "REQUER REINICIAR";
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public void Refresh()
	{
		IsOn = Read() == true;
	}

	private void Changed(string p)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
	}
}
