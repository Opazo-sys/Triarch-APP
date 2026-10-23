using System.ComponentModel;

namespace Triarch;

public sealed class BackgroundApp : INotifyPropertyChanged
{
	private bool _selected;

	public string ProcessName { get; init; } = "";

	public string Title { get; init; } = "";

	public string Detail { get; init; } = "";

	public long MemoryBytes { get; init; }

	public int Count { get; init; }

	public bool HasWindow { get; init; }

	public bool Suggested { get; init; }

	public string MemoryText => $"{(double)MemoryBytes / 1048576.0:N0} MB";

	public bool Selected
	{
		get
		{
			return _selected;
		}
		set
		{
			_selected = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Selected"));
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;
}
