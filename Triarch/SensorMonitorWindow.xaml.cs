using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using LibreHardwareMonitor.Hardware;

namespace Triarch;

public partial class SensorMonitorWindow : Window, INotifyPropertyChanged, IComponentConnector
{
	public sealed class HardwareSensorGroup
	{
		public string Hardware { get; init; } = "";

		public string Prefix { get; init; } = "";

		public List<SensorRow> Rows { get; init; } = new List<SensorRow>();
	}

	private readonly SensorMonitor _monitor;

	private readonly AppSettings _settings;

	private readonly HashSet<string> _hiddenHw;

	private readonly Action _onFilterChanged;

	private string? _category;

	private string _search = "";

	private int _columnsCount = 2;

	private bool _suppressEvents;

	public int ColumnsCount
	{
		get
		{
			return _columnsCount;
		}
		set
		{
			if (_columnsCount != value)
			{
				_columnsCount = value;
				this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("ColumnsCount"));
			}
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public SensorMonitorWindow(SensorMonitor monitor, AppSettings settings, HashSet<string> hiddenHw, Action onFilterChanged)
	{
		_monitor = monitor;
		_settings = settings;
		_hiddenHw = hiddenHw;
		_onFilterChanged = onFilterChanged;
		_suppressEvents = true;
		InitializeComponent();
		Topmost = _settings.SensorWindowTopmost;
		ChkTopmost.IsChecked = Topmost;
		int cols = Math.Clamp(_settings.SensorWindowColumns, 1, 3);
		ColumnsCount = cols;
		Col1.IsChecked = cols == 1;
		Col2.IsChecked = cols == 2;
		Col3.IsChecked = cols == 3;
		_suppressEvents = false;
		StateChanged += delegate
		{
			bool max = WindowState == WindowState.Maximized;
			Root.Margin = (max ? new Thickness(7.0) : new Thickness(0.0));
			BtnMax.Content = (max ? "\ue923" : "\ue922");
		};
		BuildFilterChips();
		RebuildGroups();
	}

	public void BuildFilterChips()
	{
		_suppressEvents = true;
		HardwareFilterPanel.Children.Clear();
		List<(string Hardware, HardwareType Type)> hws = (from r in _monitor.Rows
			group r by r.Hardware into g
			select (Hardware: g.Key, Type: g.First().HardwareType)).ToList();
		foreach (var item in hws)
		{
			string hw = item.Hardware;
			string prefix = SensorRow.HardwareGroupPrefix(item.Type);
			CheckBox cb = new CheckBox
			{
				Content = $"{prefix}: {hw}",
				IsChecked = !_hiddenHw.Contains(hw),
				Margin = new Thickness(0.0, 0.0, 14.0, 6.0),
				VerticalAlignment = VerticalAlignment.Center
			};
			cb.Click += delegate
			{
				if (cb.IsChecked == true)
				{
					_hiddenHw.Remove(hw);
				}
				else
				{
					_hiddenHw.Add(hw);
				}
				SaveAndSync();
			};
			HardwareFilterPanel.Children.Add(cb);
		}
		CategoryFilterPanel.Children.Clear();
		AddCategoryChip("Todos", null, _category == null);
		foreach (string cat in from c in _monitor.Rows.Select((SensorRow r) => r.Category).Distinct()
			orderby c
			select c)
		{
			AddCategoryChip(cat, cat, _category == cat);
		}
		_suppressEvents = false;
	}

	private void AddCategoryChip(string label, string? category, bool isChecked)
	{
		RadioButton rb = new RadioButton
		{
			Content = label,
			GroupName = "SecMonCat",
			Style = (Style)FindResource("Chip"),
			IsChecked = isChecked
		};
		rb.Checked += delegate
		{
			if (!_suppressEvents)
			{
				_category = category;
				RebuildGroups();
			}
		};
		CategoryFilterPanel.Children.Add(rb);
	}

	public void RebuildGroups()
	{
		IEnumerable<SensorRow> query = _monitor.Rows.Where((SensorRow r) => !_hiddenHw.Contains(r.Hardware));
		if (_category != null)
		{
			query = query.Where((SensorRow r) => r.Category == _category);
		}
		if (_search.Length > 0)
		{
			query = query.Where((SensorRow r) => r.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) || r.Hardware.Contains(_search, StringComparison.OrdinalIgnoreCase) || r.Category.Contains(_search, StringComparison.OrdinalIgnoreCase));
		}
		List<HardwareSensorGroup> groups = (from r in query
			group r by r.Hardware into g
			select new HardwareSensorGroup
			{
				Hardware = g.Key,
				Prefix = SensorRow.HardwareGroupPrefix(g.First().HardwareType).ToUpperInvariant(),
				Rows = g.OrderBy((SensorRow x) => x.Order).ThenBy((SensorRow x) => x.Name).ToList()
			}).ToList();
		GroupsList.ItemsSource = groups;
	}

	public void UpdateLiveTelemetry(double? fps, float? cpuTemp, float? cpuLoad, float? gpuTemp, float? gpuLoad)
	{
		TopFps.Text = ((fps > 0.0) ? $"{fps:0}" : "—");
		TopCpuTemp.Text = (cpuTemp.HasValue ? $"{cpuTemp:0.#} °C" : "—");
		TopCpuLoad.Text = (cpuLoad.HasValue ? $"({cpuLoad:0}%)" : "");
		TopGpuTemp.Text = (gpuTemp.HasValue ? $"{gpuTemp:0.#} °C" : "—");
		TopGpuLoad.Text = (gpuLoad.HasValue ? $"({gpuLoad:0}%)" : "");
	}

	private void SaveAndSync()
	{
		_settings.HiddenSensorHardware = _hiddenHw.ToList();
		try
		{
			_settings.Save();
		}
		catch
		{
		}
		BuildFilterChips();
		RebuildGroups();
		_onFilterChanged();
	}

	private void HideHardware_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { Tag: string hw } && !string.IsNullOrEmpty(hw))
		{
			_hiddenHw.Add(hw);
			SaveAndSync();
		}
	}

	private void PresetCpuGpu_Click(object sender, RoutedEventArgs e)
	{
		_hiddenHw.Clear();
		foreach (SensorRow r in _monitor.Rows)
		{
			bool isCpuOrGpu = r.HardwareType == HardwareType.Cpu || r.HardwareType == HardwareType.GpuNvidia || r.HardwareType == HardwareType.GpuAmd || r.HardwareType == HardwareType.GpuIntel;
			if (!isCpuOrGpu)
			{
				_hiddenHw.Add(r.Hardware);
			}
		}
		SaveAndSync();
	}

	private void PresetAll_Click(object sender, RoutedEventArgs e)
	{
		_hiddenHw.Clear();
		SaveAndSync();
	}

	private void ResetMinMax_Click(object sender, RoutedEventArgs e)
	{
		_monitor.ResetMinMax();
	}

	private void ChkTopmost_Click(object sender, RoutedEventArgs e)
	{
		Topmost = ChkTopmost.IsChecked == true;
		_settings.SensorWindowTopmost = Topmost;
		try
		{
			_settings.Save();
		}
		catch
		{
		}
	}

	private void Col_Checked(object sender, RoutedEventArgs e)
	{
		if (!_suppressEvents && sender is FrameworkElement { Tag: string s } && int.TryParse(s, out var c))
		{
			ColumnsCount = Math.Clamp(c, 1, 3);
			_settings.SensorWindowColumns = ColumnsCount;
			try
			{
				_settings.Save();
			}
			catch
			{
			}
		}
	}

	private void SearchBox_Changed(object sender, TextChangedEventArgs e)
	{
		_search = SearchBox.Text.Trim();
		RebuildGroups();
	}

	private void Min_Click(object sender, RoutedEventArgs e)
	{
		WindowState = WindowState.Minimized;
	}

	private void Max_Click(object sender, RoutedEventArgs e)
	{
		WindowState = ((WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized);
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}
}
