using System;
using System.ComponentModel;

namespace Triarch;

public sealed class GameRow : INotifyPropertyChanged
{
	private double _fps;

	private double _low;

	private string _source = "";

	private string _detail = "";

	public GameInfo Game { get; }

	public string Name => Game.Name;

	public string Genre => Game.Genre;

	public double Fps => _fps;

	public string FpsText
	{
		get
		{
			if (!(_fps > 0.0))
			{
				return "—";
			}
			return $"{_fps:0}";
		}
	}

	public string LowText
	{
		get
		{
			if (!(_low > 0.0))
			{
				return "";
			}
			return $"1% low {_low:0}";
		}
	}

	public string Source => _source;

	public string Detail => _detail;

	public string Rating
	{
		get
		{
			double fps = _fps;
			if (!(fps <= 0.0))
			{
				if (!(fps >= 144.0))
				{
					if (!(fps >= 90.0))
					{
						if (!(fps >= 60.0))
						{
							if (fps >= 30.0)
							{
								return "FRACO";
							}
							return "INJOGÁVEL";
						}
						return "JOGÁVEL";
					}
					return "ÓTIMO";
				}
				return "EXCELENTE";
			}
			return "";
		}
	}

	public double Bar => Math.Clamp(_fps / 240.0, 0.0, 1.0);

	public event PropertyChangedEventHandler? PropertyChanged;

	public GameRow(GameInfo game)
	{
		Game = game;
	}

	public void Set(double fps, double low, string source, string detail)
	{
		_fps = fps;
		_low = low;
		_source = source;
		_detail = detail;
		string[] array = new string[6] { "FpsText", "LowText", "Source", "Detail", "Rating", "Bar" };
		foreach (string p in array)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
		}
	}
}
