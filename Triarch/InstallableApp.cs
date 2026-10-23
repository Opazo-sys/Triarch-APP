using System.ComponentModel;
using System.Windows.Media;

namespace Triarch;

public sealed class InstallableApp : INotifyPropertyChanged
{
	private bool _selected;

	private bool _installed;

	private string _status = "";

	public string Id { get; init; } = "";

	public string WingetId => Id;

	public bool PreSelect { get; init; }

	public bool CanSelect => !_installed;

	public string Name { get; init; } = "";

	public string Category { get; init; } = "";

	public string Description { get; init; } = "";

	public string Publisher { get; init; } = "";

	public string WingetName { get; init; } = "";

	public string IconUrl { get; init; } = "";

	public string[] RegistryHints { get; init; } = new string[0];

	public string[] PathHints { get; init; } = new string[0];

	public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();

	public Brush CategoryBadgeBrush => Category switch
	{
		"Navegador" => Brush("#1E3A8A", 40), 
		"Comunicação" => Brush("#0284C7", 40), 
		"Jogos" => Brush("#059669", 40), 
		"Infra & Redes" => Brush("#0EA5E9", 45), 
		"SysAdmin & Dev" => Brush("#0D9488", 45), 
		"Mídia" => Brush("#DB2777", 40), 
		"Utilitários" => Brush("#D97706", 40), 
		_ => Brush("#475569", 40), 
	};

	public Brush CategoryBadgeFg => Category switch
	{
		"Navegador" => Brush("#60A5FA"), 
		"Comunicação" => Brush("#38BDF8"), 
		"Jogos" => Brush("#34D399"), 
		"Infra & Redes" => Brush("#7DD3FC"), 
		"SysAdmin & Dev" => Brush("#2DD4BF"), 
		"Mídia" => Brush("#F472B6"), 
		"Utilitários" => Brush("#FBBF24"), 
		_ => Brush("#94A3B8"), 
	};

	public bool Selected
	{
		get
		{
			return _selected;
		}
		set
		{
			_selected = value;
			OnChanged("Selected");
		}
	}

	public bool Installed
	{
		get
		{
			return _installed;
		}
		set
		{
			_installed = value;
			OnChanged("Installed");
			OnChanged("CanSelect");
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
			OnChanged("Status");
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private static SolidColorBrush Brush(string hex, byte? alpha = null)
	{
		Color c = (Color)ColorConverter.ConvertFromString(hex);
		if (alpha.HasValue)
		{
			c = Color.FromArgb(alpha.Value, c.R, c.G, c.B);
		}
		SolidColorBrush b = new SolidColorBrush(c);
		b.Freeze();
		return b;
	}

	private void OnChanged(string n)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
	}
}
