using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using System.Windows.Shapes;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace Triarch;

public partial class MainWindow : Window, IComponentConnector, IStyleConnector
{
	public sealed record DashComponent(string Icon, string Name, string Model, string Temp, string Load, string Clock, string Power, string Target);

	public sealed record DashEvent(string Icon, string Title, string Detail, string Value, DateTime At)
	{
		public string When
		{
			get
			{
				if (At.Date == DateTime.Today)
				{
					return $"hoje {At:HH:mm}";
				}
				return $"{At:dd/MM HH:mm}";
			}
		}
	}

	public sealed record DashRingItem(System.Windows.Media.Brush Swatch, string Label, string Value);

	public sealed record DashCheck(string Title, string Detail, bool Ok);

	private readonly record struct DashSample(float? CpuT, float? GpuT, float? CpuL, float? GpuL, float? CpuW, float? GpuW);

	public sealed record GuideStep(string Number, string Title, string Where, string Text);

	public sealed record MeterItem(string Label, string Value, double Fraction);

	public sealed record MemRow(string Label, string Value, string Hint);

	private List<InstallableApp>? _installApps;

	private bool _appsBusy;

	private bool _appsStop;

	private const int DashMax = 900;

	private readonly List<DashSample> _dash = new List<DashSample>();

	private readonly List<DashEvent> _events = new List<DashEvent>();

	private List<DashCheck> _health = new List<DashCheck>();

	private DateTime _healthAt = DateTime.MinValue;

	private bool _diskHealthBusy;

	private bool _drvInit;

	private bool _drvBusy;

	private bool _dduBusy;

	private List<GpuDriverStatus> _drvGpus = new List<GpuDriverStatus>();

	private BoardInfo _drvBoardInfo = new BoardInfo();

	private List<WuDriver> _drvWu = new List<WuDriver>();

	private CancellationTokenSource? _drvCts;

	private DduGpuDetection? _dduGpu;

	private SensorMonitorWindow? _sensorWindow;

	private HashSet<string>? _hiddenSensorHw;

	private HashSet<string> HiddenSensorHw => _hiddenSensorHw ?? (_hiddenSensorHw = ((_settings.HiddenSensorHardware != null) ? new HashSet<string>(_settings.HiddenSensorHardware, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase)));


	private List<LibGame> _libGames = new List<LibGame>();

	private LibGame? _lg;

	private GameProfile? _lp;

	private bool _lLoading;

	private bool _launcherInited;

	private CancellationTokenSource? _launchCts;

	private readonly Dictionary<string, string?> _exeGuess = new Dictionary<string, string>();

	private static readonly (string Label, string Token, string? Hint)[] ExtraPresets = new (string, string, string)[6]
	{
		("Pular intro (-novid)", "-novid", null),
		("Prioridade alta (-high)", "-high", null),
		("Sem limite de FPS (+fps_max 0)", "+fps_max 0", null),
		("Sem joystick (-nojoy)", "-nojoy", null),
		("Abrir console (-console)", "-console", null),
		("Programas de terceiros (-allow_third_party_software)", "-allow_third_party_software", "CS2: libera programas que o modo confiável bloqueia, como a captura de jogo do OBS, ReShade e alguns overlays.\nAtenção: a Valve diz que usar esta opção pode reduzir o seu Fator de Confiança (Trust Factor) no matchmaking.")
	};

	private bool _energyBusy;

	private CancellationTokenSource? _netCts;

	private bool _netRunning;

	private List<DiskHealthCard>? _lastNet;

	private uint? _ocClockCap;

	private uint? _ocPendingCap;

	private CancellationTokenSource? _ocCts;

	private bool _ocRunning;

	private HashSet<string>? _ovShow;

	private GpuRtBench? _rtBench;

	private NotifyIcon? _trayIcon;

	private bool _isRealExit;

	private readonly SensorMonitor _monitor;

	private readonly DispatcherTimer _timer;

	private readonly ICollectionView _view;

	private string? _category;

	private string _search = "";

	private List<GpuInfo> _gpus = new List<GpuInfo>();

	private GpuInfo? _gpu;

	private readonly StressTester _stress = new StressTester();

	private readonly List<double> _tempHistory = new List<double>();

	private const int HistoryLength = 300;

	private DateTime _stressStart;

	private TimeSpan _stressDuration;

	private double _tempLimit = 90.0;

	private float _stressMaxTemp;

	private readonly AppSettings _settings = AppSettings.Load();

	private readonly HowManyFpsClient _fpsApi = new HowManyFpsClient();

	private List<GameRow> _gameRows = new List<GameRow>();

	private ListCollectionView? _gameView;

	private CancellationTokenSource? _gameFetch;

	private string _cpuName = "";

	private string _gpuName = "";

	private double _cpuIdx = 80.0;

	private double _gpuIdx = 50.0;

	private readonly FrameMonitor _frames = new FrameMonitor();

	private BottleneckSession? _bnSession;

	private double _balanceRatio = 1.0;

	private OverlayWindow? _overlay;

	private DispatcherTimer? _overlayTimer;

	private bool _overlayReady;

	private const int HotkeyId = 21061;

	private readonly BenchRenderer _bench = new BenchRenderer();

	private WriteableBitmap? _benchBitmap;

	private CancellationTokenSource? _benchCts;

	private DispatcherTimer? _benchTimer;

	private DateTime _benchStart;

	private string _benchPhase = "";

	private DateTime _benchPhaseStart;

	private List<BackgroundApp> _apps = new List<BackgroundApp>();

	private readonly List<Tweak> _tweaks = WindowsTweaks.All();

	private bool _optInit;

	private readonly GpuBurn _burn = new GpuBurn();

	private bool _gpuBurnOn;

	private double _gpuTempLimit = 85.0;

	private float _gpuMaxTemp;

	private readonly List<double> _gpuTempHistory = new List<double>();

	private bool _sidebarCollapsed;

	private List<DriveCard> _drives = new List<DriveCard>();

	private ToastWindow? _toast;

	private bool _gameBenchRunning;

	private GpuGameBench? _gpuBench;

	private bool _gpuBenchActive;

	private CancellationTokenSource? _gameBenchCts;

	private WriteableBitmap? _simBitmap;

	private int[] _simPixels = Array.Empty<int>();

	private const int SimView = 440;

	private nint OcGpu
	{
		get
		{
			if (_gpus.Count <= 0)
			{
				return IntPtr.Zero;
			}
			return _gpus[0].NvmlHandle;
		}
	}

	private HashSet<string> OvShow
	{
		get
		{
			HashSet<string>? hashSet = _ovShow;
			if (hashSet == null)
			{
				List<string> l = _settings.OverlayItems;
				hashSet = (_ovShow = ((l != null) ? l.ToHashSet() : OverlayMetrics.Defaults()));
			}
			return hashSet;
		}
	}

	private System.Windows.Media.Brush Fg => (System.Windows.Media.Brush)FindResource("Fg");

	private System.Windows.Media.Brush Ink => (System.Windows.Media.Brush)FindResource("Ink");

	private System.Windows.Media.Brush Muted => (System.Windows.Media.Brush)FindResource("Muted");

	private System.Windows.Media.Brush Card => (System.Windows.Media.Brush)FindResource("Card");

	private System.Windows.Media.Brush Accent => (System.Windows.Media.Brush)FindResource("Accent");

	private System.Windows.Media.Brush AccentSoft => (System.Windows.Media.Brush)FindResource("AccentSoft");

	private System.Windows.Media.Brush Success => (System.Windows.Media.Brush)FindResource("Success");

	private System.Windows.Media.Brush Warning => (System.Windows.Media.Brush)FindResource("Warning");

	private System.Windows.Media.Brush Danger => (System.Windows.Media.Brush)FindResource("Danger");

	private (ToggleButton Group, StackPanel Items, Border Sep)[] SidebarGroups => new (ToggleButton, StackPanel, Border)[4]
	{
		(GrpSystem, GrpSystemItems, SepSystem),
		(GrpGames, GrpGamesItems, SepGames),
		(GrpPerf, GrpPerfItems, SepPerf),
		(GrpTools, GrpToolsItems, SepTools)
	};

	private bool BenchRunning => _benchCts != null;

	private bool AnyBenchRunning
	{
		get
		{
			if (!BenchRunning)
			{
				return _gameBenchRunning;
			}
			return true;
		}
	}

	private bool TestRunning
	{
		get
		{
			if (!_stress.Running)
			{
				return _gpuBurnOn;
			}
			return true;
		}
	}

	private void InitApps()
	{
		if (_appsBusy)
		{
			return;
		}
		if (_installApps == null)
		{
			_installApps = AppInstaller.Catalog();
			AppsList.ItemsSource = _installApps;
			AppInstaller.RefreshInstalled(_installApps);
			foreach (InstallableApp a in _installApps)
			{
				a.Selected = a.PreSelect && !a.Installed;
			}
		}
		else
		{
			AppInstaller.RefreshInstalled(_installApps);
		}
		bool winget = AppInstaller.WingetAvailable;
		BtnAppsStore.Visibility = (winget ? Visibility.Collapsed : Visibility.Visible);
		BtnAppsInstall.IsEnabled = winget;
		int installed = _installApps.Count((InstallableApp installableApp) => installableApp.Installed);
		AppsStatus.Text = (winget ? $"{installed} de {_installApps.Count} já instalados. Marque o que quer e clique em INSTALAR SELECIONADOS." : "O winget não foi encontrado. Em PC recém-formatado ele aparece depois que a Microsoft Store atualiza o \"Instalador de Aplicativo\": use o botão abaixo, espere atualizar e abra esta página de novo.");
	}

	private void BtnAppsAll_Click(object sender, RoutedEventArgs e)
	{
		if (_installApps == null || _appsBusy)
		{
			return;
		}
		foreach (InstallableApp installApp in _installApps)
		{
			installApp.Selected = !installApp.Installed;
		}
	}

	private void BtnAppsNone_Click(object sender, RoutedEventArgs e)
	{
		if (_installApps == null || _appsBusy)
		{
			return;
		}
		foreach (InstallableApp installApp in _installApps)
		{
			installApp.Selected = false;
		}
	}

	private void BtnAppsStore_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?productid=9NBLGGH4NNS1")
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			AppsStatus.Text = "Não foi possível abrir a Microsoft Store: " + ex.Message;
		}
	}

	private async void BtnAppsInstall_Click(object sender, RoutedEventArgs e)
	{
		if (_installApps == null)
		{
			return;
		}
		if (_appsBusy)
		{
			_appsStop = true;
			BtnAppsInstall.IsEnabled = false;
			AppsStatus.Text = "Parando depois do app atual…";
			return;
		}
		List<InstallableApp> sel = _installApps.Where((InstallableApp a) => a.Selected && !a.Installed).ToList();
		if (sel.Count == 0)
		{
			AppsStatus.Text = "Nenhum app selecionado.";
		}
		else
		{
			if (sel.Any((InstallableApp a) => a.WingetId == "RARLab.WinRAR") && sel.Any((InstallableApp a) => a.WingetId == "7zip.7zip") && System.Windows.MessageBox.Show("WinRAR e 7-Zip fazem a mesma coisa. Instalar os dois mesmo assim?", "Instalar apps", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
			{
				return;
			}
			_appsBusy = true;
			_appsStop = false;
			BtnAppsInstall.Content = "PARAR";
			foreach (InstallableApp item in sel)
			{
				item.Status = "Na fila.";
			}
			int ok = 0;
			int done = 0;
			List<string> failed = new List<string>();
			AppsProgress.ScaleX = 0.0;
			foreach (InstallableApp app in sel)
			{
				if (_appsStop)
				{
					app.Status = "Não instalado (parado).";
					continue;
				}
				AppsStatus.Text = $"Instalando {app.Name} ({done + 1} de {sel.Count})… baixando do site oficial, pode levar alguns minutos.";
				app.Status = "Instalando…";
				try
				{
					(bool, string) tuple = await AppInstaller.InstallAsync(app, CancellationToken.None);
					bool success = tuple.Item1;
					string msg = tuple.Item2;
					app.Status = msg;
					if (success)
					{
						ok++;
						app.Selected = false;
					}
					else
					{
						failed.Add(app.Name);
					}
				}
				catch (Exception ex)
				{
					app.Status = "Falhou: " + ex.Message;
					failed.Add(app.Name);
				}
				done++;
				AppsProgress.ScaleX = (double)done / (double)sel.Count;
			}
			AppInstaller.RefreshInstalled(_installApps);
			_appsBusy = false;
			BtnAppsInstall.Content = "INSTALAR SELECIONADOS";
			BtnAppsInstall.IsEnabled = true;
			AppsStatus.Text = $"{ok} de {sel.Count} instalados." + ((failed.Count > 0) ? (" Falharam: " + string.Join(", ", failed) + " (veja o motivo na lista).") : "") + (_appsStop ? " Instalação interrompida." : "");
		}
	}

	private float? MainGpuLoad()
	{
		if (_gpus.Count != 0)
		{
			return _monitor.Rows.FirstOrDefault((SensorRow r) => r.Hardware == _gpus[0].Name && r.Type == SensorType.Load && r.Name == "GPU Core")?.RawValue;
		}
		return null;
	}

	private float? CpuClockAvg()
	{
		List<float> c = (from r in _monitor.Rows
			where r.HardwareType == HardwareType.Cpu && r.Type == SensorType.Clock && r.Name.StartsWith("Core #") && r.RawValue > 0f
			select r.RawValue.Value).ToList();
		if (c.Count <= 0)
		{
			return null;
		}
		return c.Average();
	}

	private void CollectDashSample()
	{
		_dash.Add(new DashSample(_monitor.CpuTemperature, MainGpuTemp(), _monitor.CpuLoad, MainGpuLoad(), _monitor.CpuPower, MainGpuPower()));
		if (_dash.Count > 900)
		{
			_dash.RemoveAt(0);
		}
		if (PageDash.IsVisible)
		{
			UpdateDashboard(full: false);
		}
	}

	private void AddEvent(string icon, string title, string detail, string value)
	{
		_events.Insert(0, new DashEvent(icon, title, detail, value, DateTime.Now));
		if (_events.Count > 30)
		{
			_events.RemoveAt(_events.Count - 1);
		}
		if (PageDash.IsVisible)
		{
			ShowEvents();
		}
	}

	private IEnumerable<DashEvent> SavedEvents()
	{
		foreach (BenchRun r in _settings.BenchRuns)
		{
			double? multi = r.Multi;
			if (multi.HasValue)
			{
				double m = multi.GetValueOrDefault();
				yield return new DashEvent("\ue8b9", "TRIARCH BENCH", "Render · multi-core", $"{m:N0} pts", r.Date);
				continue;
			}
			multi = r.Single;
			if (multi.HasValue)
			{
				double s = multi.GetValueOrDefault();
				yield return new DashEvent("\ue8b9", "TRIARCH BENCH", "Render · single-core", $"{s:N0} pts", r.Date);
			}
		}
		foreach (BenchRecord r2 in _settings.BenchRecords)
		{
			(string, string) tuple = r2.Test switch
			{
				"gpu" => ("\ue7f4", "Cena de jogo (GPU)"), 
				"sim" => ("\ue7fc", "Simulação de jogo"), 
				"mem" => ("\ueda2", "Memória e carregamento"), 
				"oc" => ("\uec4a", "Verificação de estabilidade"), 
				"disk" => ("\ueda2", "Benchmark de disco"), 
				_ => ("\ue8b9", r2.Test), 
			};
			string icon = tuple.Item1;
			string title = tuple.Item2;
			string first = r2.Summary.Split('·')[0].Trim();
			string rest = (r2.Summary.Contains('·') ? r2.Summary.Substring(r2.Summary.IndexOf('·') + 1).Trim() : "");
			yield return new DashEvent(icon, title, rest, first, r2.Date);
		}
	}

	private void ShowEvents()
	{
		List<DashEvent> list = (from e in _events.Concat(SavedEvents())
			orderby e.At descending
			select e).Take(4).ToList();
		DashEvents.ItemsSource = list;
		DashEventsEmpty.Visibility = ((list.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void DashOption_Checked(object sender, RoutedEventArgs e)
	{
		if (DashChart != null)
		{
			DrawDashChart();
		}
	}

	private void DashChart_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		DrawDashChart();
	}

	private void DashBench_Click(object sender, RoutedEventArgs e)
	{
		NavBench.IsChecked = true;
	}

	private void DashOptimize_Click(object sender, RoutedEventArgs e)
	{
		NavOptimize.IsChecked = true;
	}

	private void DashGo_Click(object sender, RoutedEventArgs e)
	{
		if (!(sender is FrameworkElement { Tag: var tag }))
		{
			return;
		}
		string page = tag as string;
		if (page != null)
		{
			System.Windows.Controls.RadioButton nav = new System.Windows.Controls.RadioButton[4] { NavSensors, NavGpu, NavSystem, NavDisks }.FirstOrDefault((System.Windows.Controls.RadioButton n) => (string)n.Tag == page);
			if (nav != null)
			{
				nav.IsChecked = true;
			}
		}
	}

	private void UpdateDashboard(bool full = true)
	{
		DrawDashChart();
		UpdateDashComponents();
		UpdateDashRings();
		if (full)
		{
			UpdateDashScores();
			ShowEvents();
		}
		if (full || (DateTime.Now - _healthAt).TotalSeconds > 60.0)
		{
			UpdateDashHealth();
		}
	}

	private static string F(float? v, string fmt, string unit)
	{
		if (!v.HasValue)
		{
			return "—";
		}
		return v.GetValueOrDefault().ToString(fmt) + unit;
	}

	private void UpdateDashComponents()
	{
		List<DashComponent> rows = new List<DashComponent>
		{
			new DashComponent("\ue950", "Processador", GameCatalog.ShortCpuName(_cpuName), F(_monitor.CpuTemperature, "0", " °C"), F(_monitor.CpuLoad, "0", " %"), F(CpuClockAvg(), "0", " MHz"), F(_monitor.CpuPower, "0", " W"), "PageSensors")
		};
		if (_gpus.Count > 0)
		{
			uint? clk = ((_gpus[0].NvmlHandle != IntPtr.Zero) ? Nvml.Clock(_gpus[0].NvmlHandle, 0) : ((uint?)null));
			string model = GameCatalog.ShortGpuName(_gpus[0].Name);
			string temp = F(MainGpuTemp(), "0", " °C");
			string load = F(MainGpuLoad(), "0", " %");
			string clock;
			if (clk.HasValue)
			{
				uint c = clk.GetValueOrDefault();
				clock = $"{c} MHz";
			}
			else
			{
				clock = "—";
			}
			rows.Add(new DashComponent("\ue7f4", "Placa de vídeo", model, temp, load, clock, F(MainGpuPower(), "0", " W"), "PageGpu"));
		}
		var (total, avail) = Optimizer.MemoryStatus();
		if (total > 0)
		{
			double used = total - avail;
			rows.Add(new DashComponent("\ue7f8", "Memória RAM", $"{(double)total / 1073741824.0:0} GB instalados", "—", $"{used / (double)total:P0}", "—", $"{used / 1073741824.0:0.0} GB", "PageSystem"));
		}
		DriveCard c2 = _drives.FirstOrDefault((DriveCard d) => d.Letter.StartsWith("C", StringComparison.OrdinalIgnoreCase));
		if (c2 != null)
		{
			rows.Add(new DashComponent("\ueda2", "Disco " + c2.Letter, (c2.Disk.Length > 0) ? c2.Disk : c2.Kind, "—", c2.UsedPercent, "—", DriveCard.Fmt(c2.Free) + " livres", "PageDisks"));
		}
		DashComponents.ItemsSource = rows;
	}

	private void UpdateDashScores()
	{
		List<BenchRun> runs = _settings.BenchRuns.Where((BenchRun r) => r.Multi.HasValue).ToList();
		if (runs.Count > 0)
		{
			DashScore.Text = $"{runs[0].Multi:N0}";
			DashScoreSub.Text = $"pts · render multi-core · {runs[0].Date:dd/MM HH:mm}";
			if (runs.Count > 1)
			{
				double d = runs[0].Multi.Value - runs[1].Multi.Value;
				DashScoreDelta.Text = ((d >= 0.0) ? $"▲ {d:N0}" : $"▼ {0.0 - d:N0}");
			}
			else
			{
				DashScoreDelta.Text = "";
			}
		}
		else
		{
			DashScore.Text = "—";
			DashScoreDelta.Text = "";
			DashScoreSub.Text = "rode o TRIARCH BENCH para ter uma nota";
		}
		double? single = _settings.BenchRuns.FirstOrDefault((BenchRun r) => r.Single.HasValue)?.Single;
		ItemsControl dashScores = DashScores;
		KeyValuePair<string, string>[] array = new KeyValuePair<string, string>[3];
		string value;
		if (single.HasValue)
		{
			double s = single.GetValueOrDefault();
			value = $"{s:N0} pts";
		}
		else
		{
			value = "—";
		}
		array[0] = new KeyValuePair<string, string>("Render single-core", value);
		array[1] = new KeyValuePair<string, string>("Cena de jogo (GPU)", Last("gpu"));
		array[2] = new KeyValuePair<string, string>("Simulação de jogo", Last("sim"));
		dashScores.ItemsSource = array;
		DashCpuName.Text = GameCatalog.ShortCpuName(_cpuName);
		DashGpuName.Text = ((_gpus.Count > 0) ? GameCatalog.ShortGpuName(_gpus[0].Name) : "—");
		string Last(string test)
		{
			BenchRecord? benchRecord = _settings.BenchRecords.FirstOrDefault((BenchRecord r) => r.Test == test);
			return (((object)benchRecord != null) ? benchRecord.Summary.Split('·')[0].Trim() : null) ?? "—";
		}
	}

	private void DrawDashChart()
	{
		DashChart.Children.Clear();
		double w = DashChart.ActualWidth;
		double h = DashChart.ActualHeight;
		if (w < 80.0 || h < 60.0)
		{
			return;
		}
		int range = int.Parse(CheckedTag(DashRangePanel) ?? "300");
		string metric = CheckedTag(DashMetricPanel) ?? "temp";
		Func<DashSample, float?> func;
		if (metric == "load")
		{
			func = (DashSample s) => s.CpuL;
		}
		else
		{
			func = ((!(metric == "power")) ? ((Func<DashSample, float?>)((DashSample s) => s.CpuT)) : ((Func<DashSample, float?>)((DashSample s) => s.CpuW)));
		}
		Func<DashSample, float?> cpu = func;
		if (metric == "load")
		{
			func = (DashSample s) => s.GpuL;
		}
		else
		{
			func = ((!(metric == "power")) ? ((Func<DashSample, float?>)((DashSample s) => s.GpuT)) : ((Func<DashSample, float?>)((DashSample s) => s.GpuW)));
		}
		Func<DashSample, float?> gpu = func;
		string text;
		if (metric == "load")
		{
			text = " %";
		}
		else
		{
			text = ((!(metric == "power")) ? " °C" : " W");
		}
		string unit = text;
		if (metric == "load")
		{
			text = "CARGA";
		}
		else
		{
			text = ((!(metric == "power")) ? "TEMPERATURA" : "CONSUMO");
		}
		string name = text;
		List<DashSample> data = _dash.Skip(Math.Max(0, _dash.Count - range)).ToList();
		float? num;
		if (data.Count <= 0)
		{
			num = null;
		}
		else
		{
			Func<DashSample, float?> func2 = cpu;
			List<DashSample> list = data;
			num = func2(list[list.Count - 1]);
		}
		float? cNow = num;
		float? num2;
		if (data.Count <= 0)
		{
			num2 = null;
		}
		else
		{
			Func<DashSample, float?> func3 = gpu;
			List<DashSample> list2 = data;
			num2 = func3(list2[list2.Count - 1]);
		}
		float? gNow = num2;
		DashChartLabel.Text = "CPU · " + name;
		TextBlock dashChartValue = DashChartValue;
		string text2;
		if (cNow.HasValue)
		{
			float cv = cNow.GetValueOrDefault();
			text2 = $"{cv:0.#}{unit}";
		}
		else
		{
			text2 = "—";
		}
		dashChartValue.Text = text2;
		TextBlock dashChartValue2 = DashChartValue2;
		string text3;
		if (gNow.HasValue)
		{
			float gv = gNow.GetValueOrDefault();
			text3 = $"{gv:0.#}{unit}";
		}
		else
		{
			text3 = "—";
		}
		dashChartValue2.Text = text3;
		float? firstC = data.Select(cpu).FirstOrDefault((float? num5) => num5.HasValue);
		TextBlock dashChartDelta = DashChartDelta;
		object text4;
		if (cNow.HasValue)
		{
			float a = cNow.GetValueOrDefault();
			if (firstC.HasValue)
			{
				float b = firstC.GetValueOrDefault();
				text4 = $"{((a - b >= 0f) ? "+" : "")}{a - b:0.#}{unit} em {((range >= 60) ? $"{range / 60} min" : $"{range} s")}";
				goto IL_04a0;
			}
		}
		text4 = ((!cNow.HasValue && metric != "load") ? "sem leitura da CPU (veja o aviso no menu)" : "coletando…");
		goto IL_04a0;
		IL_04a0:
		dashChartDelta.Text = (string)text4;
		List<double> all = (from num5 in data.SelectMany((DashSample s) => new float?[2]
			{
				cpu(s),
				gpu(s)
			})
			where num5.HasValue
			select num5).Select((Func<float?, double>)((float? num5) => num5.Value)).ToList();
		double yMin = ((metric == "temp") ? 20 : 0);
		double num3;
		if (metric == "load")
		{
			num3 = 100.0;
		}
		else
		{
			num3 = ((!(metric == "power")) ? Math.Max(90.0, (all.Count > 0) ? (all.Max() + 5.0) : 90.0) : Math.Max(50.0, (all.Count > 0) ? (all.Max() * 1.2) : 50.0));
		}
		double yMax = num3;
		double plotW = w - 48.0;
		double plotH = h - 8.0 - 22.0;
		SolidColorBrush fg = (SolidColorBrush)FindResource("Fg");
		SolidColorBrush accent = (SolidColorBrush)FindResource("Accent");
		System.Windows.Media.Brush faint = (System.Windows.Media.Brush)FindResource("Faint");
		System.Windows.Media.FontFamily mono = (System.Windows.Media.FontFamily)FindResource("Mono");
		for (int k = 0; k <= 2; k++)
		{
			double v = yMin + (yMax - yMin) * (double)k / 2.0;
			double y = Y(v);
			DashChart.Children.Add(new Line
			{
				X1 = 48.0,
				X2 = w,
				Y1 = y,
				Y2 = y,
				Stroke = (System.Windows.Media.Brush)FindResource("Line"),
				StrokeDashArray = new DoubleCollection { 5.0, 5.0 }
			});
			TextBlock lbl = new TextBlock
			{
				Text = $"{v:0}{unit.Trim()}",
				FontSize = 11.0,
				Foreground = faint,
				FontFamily = mono
			};
			Canvas.SetLeft(lbl, 0.0);
			Canvas.SetTop(lbl, y - 8.0);
			DashChart.Children.Add(lbl);
		}
		string[] xs = new string[3]
		{
			(range >= 60) ? $"-{range / 60} min" : $"-{range} s",
			(range >= 120) ? $"-{range / 120} min" : $"-{range / 2} s",
			"agora"
		};
		for (int k2 = 0; k2 < 3; k2++)
		{
			TextBlock t = new TextBlock
			{
				Text = xs[k2],
				FontSize = 11.0,
				Foreground = faint
			};
			t.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
			Canvas.SetLeft(t, Math.Clamp(48.0 + plotW * (double)k2 / 2.0 - t.DesiredSize.Width / 2.0, 48.0, w - t.DesiredSize.Width));
			Canvas.SetTop(t, h - 16.0);
			DashChart.Children.Add(t);
		}
		if (data.Count < 2)
		{
			return;
		}
		Polyline gLine = new Polyline
		{
			Stroke = (System.Windows.Media.Brush)FindResource("Muted"),
			StrokeThickness = 1.5,
			StrokeDashArray = new DoubleCollection { 3.0, 2.0 },
			StrokeLineJoin = PenLineJoin.Round
		};
		for (int i = 0; i < data.Count; i++)
		{
			float? num4 = gpu(data[i]);
			if (num4.HasValue)
			{
				float g = num4.GetValueOrDefault();
				gLine.Points.Add(new System.Windows.Point(X(i), Y(g)));
			}
		}
		DashChart.Children.Add(gLine);
		List<System.Windows.Point> pts = new List<System.Windows.Point>();
		for (int i2 = 0; i2 < data.Count; i2++)
		{
			float? num4 = cpu(data[i2]);
			if (num4.HasValue)
			{
				float c = num4.GetValueOrDefault();
				pts.Add(new System.Windows.Point(X(i2), Y(c)));
			}
		}
		if (pts.Count < 2)
		{
			return;
		}
		System.Windows.Media.Color col = accent.Color;
		Polygon area = new Polygon
		{
			Fill = new LinearGradientBrush(System.Windows.Media.Color.FromArgb(102, col.R, col.G, col.B), System.Windows.Media.Color.FromArgb(0, col.R, col.G, col.B), 90.0)
		};
		foreach (System.Windows.Point p in pts)
		{
			area.Points.Add(p);
		}
		area.Points.Add(new System.Windows.Point(pts[pts.Count - 1].X, 8.0 + plotH));
		area.Points.Add(new System.Windows.Point(pts[0].X, 8.0 + plotH));
		DashChart.Children.Add(area);
		Polyline line = new Polyline
		{
			Stroke = accent,
			StrokeThickness = 2.2,
			StrokeLineJoin = PenLineJoin.Round
		};
		foreach (System.Windows.Point p2 in pts)
		{
			line.Points.Add(p2);
		}
		DashChart.Children.Add(line);
		System.Windows.Point last = pts[pts.Count - 1];
		DashChart.Children.Add(new Line
		{
			X1 = last.X,
			X2 = last.X,
			Y1 = 8.0,
			Y2 = 8.0 + plotH,
			Stroke = (System.Windows.Media.Brush)FindResource("AccentBorder"),
			StrokeDashArray = new DoubleCollection { 3.0, 3.0 }
		});
		Ellipse dot = new Ellipse
		{
			Width = 11.0,
			Height = 11.0,
			Stroke = accent,
			StrokeThickness = 2.2,
			Fill = (System.Windows.Media.Brush)FindResource("Card")
		};
		Canvas.SetLeft(dot, last.X - 5.5);
		Canvas.SetTop(dot, last.Y - 5.5);
		DashChart.Children.Add(dot);
		Border bubble = new Border
		{
			Background = (System.Windows.Media.Brush)FindResource("CardElevated"),
			BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBorder"),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(10.0, 5.0, 10.0, 5.0),
			Child = new TextBlock
			{
				Text = DashChartValue.Text,
				FontFamily = mono,
				FontWeight = FontWeights.SemiBold,
				Foreground = fg
			}
		};
		bubble.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
		Canvas.SetLeft(bubble, Math.Max(48.0, last.X - bubble.DesiredSize.Width - 10.0));
		Canvas.SetTop(bubble, Math.Clamp(last.Y - bubble.DesiredSize.Height - 10.0, 0.0, h - bubble.DesiredSize.Height));
		DashChart.Children.Add(bubble);
		double X(int num5)
		{
			return 48.0 + plotW * (double)num5 / (double)Math.Max(1, range - 1) + plotW * (double)(range - data.Count) / (double)Math.Max(1, range - 1);
		}
		double Y(double value)
		{
			return 8.0 + plotH * (1.0 - (Math.Clamp(value, yMin, yMax) - yMin) / (yMax - yMin));
		}
	}

	private void UpdateDashRings()
	{
		(long Total, long Available) tuple = Optimizer.MemoryStatus();
		long total = tuple.Total;
		long avail = tuple.Available;
		double ram = ((total > 0) ? ((double)(total - avail) / (double)total) : 0.0);
		double? vram = null;
		if (_gpus.Count > 0 && _gpus[0].NvmlHandle != IntPtr.Zero)
		{
			Nvml.Memory? memory = Nvml.MemoryInfo(_gpus[0].NvmlHandle);
			if (memory.HasValue)
			{
				Nvml.Memory m = memory.GetValueOrDefault();
				if (m.Total != 0)
				{
					vram = (double)m.Used / (double)m.Total;
				}
			}
		}
		DriveCard c0 = _drives.FirstOrDefault((DriveCard d) => d.Letter.StartsWith("C", StringComparison.OrdinalIgnoreCase));
		DashMemHero.Text = ((total > 0) ? $"{(double)(total - avail) / 1073741824.0:0.0} GB" : "—");
		DashMemSub.Text = ((total > 0) ? $"de RAM em uso · {(double)total / 1073741824.0:0} GB instalados" : "");
		System.Windows.Media.Brush accent = (System.Windows.Media.Brush)FindResource("Accent");
		System.Windows.Media.Brush fg = (System.Windows.Media.Brush)FindResource("Fg");
		System.Windows.Media.Brush muted = (System.Windows.Media.Brush)FindResource("Muted");
		System.Windows.Media.Brush faint = (System.Windows.Media.Brush)FindResource("Faint");
		List<(double, System.Windows.Media.Brush, string)> rings = new List<(double, System.Windows.Media.Brush, string)> { (ram, accent, "RAM") };
		if (vram.HasValue)
		{
			double v = vram.GetValueOrDefault();
			rings.Add((v, muted, "VRAM"));
		}
		if (c0 != null)
		{
			rings.Add((c0.UsedFraction, faint, "Disco " + c0.Letter));
		}
		DashRings.Children.Clear();
		double cx = DashRings.Width / 2.0;
		double cy = DashRings.Height / 2.0;
		for (int i = 0; i < rings.Count; i++)
		{
			double r = 86 - i * 22;
			DashRings.Children.Add(new Ellipse
			{
				Width = r * 2.0,
				Height = r * 2.0,
				Stroke = (System.Windows.Media.Brush)FindResource("Line"),
				StrokeThickness = 12.0,
				Margin = new Thickness(cx - r, cy - r, 0.0, 0.0)
			});
			System.Windows.Shapes.Path arc = new System.Windows.Shapes.Path
			{
				Data = RingArc(rings[i].Item1, cx, cy, r),
				Stroke = rings[i].Item2,
				StrokeThickness = 12.0,
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round
			};
			DashRings.Children.Add(arc);
		}
		TextBlock center = new TextBlock
		{
			Text = $"{ram:P0}",
			FontFamily = (System.Windows.Media.FontFamily)FindResource("Mono"),
			FontSize = 16.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = fg
		};
		center.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
		Canvas.SetLeft(center, cx - center.DesiredSize.Width / 2.0);
		Canvas.SetTop(center, cy - center.DesiredSize.Height / 2.0);
		DashRings.Children.Add(center);
		DashRingLegend.ItemsSource = rings.Select(((double Frac, System.Windows.Media.Brush Brush, string Label) tuple2) => new DashRingItem(tuple2.Brush, tuple2.Label, $"{tuple2.Frac:P0}")).ToList();
	}

	private static Geometry RingArc(double frac, double cx, double cy, double r)
	{
		frac = Math.Clamp(frac, 0.0, 0.9999);
		if (frac <= 0.001)
		{
			return Geometry.Empty;
		}
		double a = frac * 2.0 * Math.PI;
		PathFigure fig = new PathFigure
		{
			StartPoint = new System.Windows.Point(cx, cy - r)
		};
		fig.Segments.Add(new ArcSegment(new System.Windows.Point(cx + r * Math.Sin(a), cy - r * Math.Cos(a)), new System.Windows.Size(r, r), 0.0, frac > 0.5, SweepDirection.Clockwise, isStroked: true));
		PathGeometry pathGeometry = new PathGeometry(new PathFigure[1] { fig });
		pathGeometry.Freeze();
		return pathGeometry;
	}

	private void UpdateDashHealth()
	{
		_healthAt = DateTime.Now;
		List<DashCheck> list = new List<DashCheck>();
		if (_monitor.IsOpen)
		{
			SensorMonitor.CpuSensorProblem sensors = _monitor.DiagnoseCpuSensors();
			list.Add(Item(sensors == SensorMonitor.CpuSensorProblem.None, "Sensores da CPU", "Temperatura e consumo disponíveis.", SensorMonitor.Explain(sensors).Split('.')[0] + "."));
		}
		HardwareErrors errs = OcCheck.ErrorsSince(DateTime.Now.AddDays(-7.0));
		list.Add(Item(errs.Error == null && errs.Total == 0, "Sem erros de hardware", "Nenhum WHEA ou reinício do driver de vídeo em 7 dias.", (errs.Error != null) ? "Não foi possível ler o registro do Windows." : $"{errs.Whea} WHEA · {errs.GpuResets} reinícios do driver em 7 dias."));
		Tweak gm = _tweaks.FirstOrDefault((Tweak t) => t.Id == "gamemode");
		Tweak dvr = _tweaks.FirstOrDefault((Tweak t) => t.Id == "gamedvr");
		try
		{
			gm?.Refresh();
			dvr?.Refresh();
		}
		catch
		{
		}
		if (gm != null)
		{
			list.Add(Item(gm.IsOn, "Modo de Jogo ligado", "O Windows prioriza o jogo.", "Desligado: ligue em Otimização."));
		}
		if (dvr != null)
		{
			list.Add(Item(dvr.IsOn, "Gravação em segundo plano desligada", "O Game Bar não grava o tempo todo.", "O Game DVR está gravando: custa FPS."));
		}
		_health = list;
		DashHealth.ItemsSource = _health;
		static DashCheck Item(bool ok, string title, string okText, string badText)
		{
			return new DashCheck(title, ok ? okText : badText, ok);
		}
	}

	private async void RefreshDiskHealth()
	{
		if (_diskHealthBusy)
		{
			return;
		}
		_diskHealthBusy = true;
		BtnDiskRefresh.IsEnabled = false;
		DiskHealthStatus.Text = "Lendo os discos…";
		try
		{
			List<SensorRow> sensors = _monitor.Rows.ToList();
			List<DiskHealthCard> cards = await Task.Run(() => DiskHealth.Read(sensors));
			DiskCards.ItemsSource = cards;
			int bad = cards.Count((DiskHealthCard c) =>
			{
				DiskVerdict verdict = c.Verdict;
				return (uint)(verdict - 1) <= 1u;
			});
			TextBlock diskHealthStatus = DiskHealthStatus;
			string text;
			if (cards.Count != 0)
			{
				text = ((bad == 0) ? $"{cards.Count} disco(s) · nenhum problema encontrado · lido às {DateTime.Now:HH:mm:ss}" : $"{cards.Count} disco(s) · {bad} precisa(m) de atenção · lido às {DateTime.Now:HH:mm:ss}");
			}
			else
			{
				text = "Nenhum disco encontrado.";
			}
			diskHealthStatus.Text = text;
		}
		catch (Exception ex)
		{
			DiskHealthStatus.Text = "Falhou: " + ex.Message;
		}
		finally
		{
			_diskHealthBusy = false;
			BtnDiskRefresh.IsEnabled = true;
		}
	}

	private void BtnDiskRefresh_Click(object sender, RoutedEventArgs e)
	{
		RefreshDiskHealth();
	}

	private void InitDiskBench()
	{
		if (_gameBenchRunning)
		{
			return;
		}
		string keep = CheckedTag(DiskDrivePanel);
		DiskDrivePanel.Children.Clear();
		List<(string, string)> drives = DiskBench.Drives();
		if (drives.Count == 0)
		{
			DiskStatus.Text = "Nenhuma unidade com espaço livre suficiente (1,5 GB).";
			BtnDiskBench.IsEnabled = false;
			return;
		}
		BtnDiskBench.IsEnabled = true;
		string pick = ((keep != null && drives.Any(((string Root, string Text) d) => d.Root == keep)) ? keep : (drives.FirstOrDefault(((string Root, string Text) d) => d.Root.StartsWith("C", StringComparison.OrdinalIgnoreCase)).Item1 ?? drives[0].Item1));
		foreach (var (root, text) in drives)
		{
			DiskDrivePanel.Children.Add(new System.Windows.Controls.RadioButton
			{
				Content = text,
				Tag = root,
				GroupName = "DiskDrive",
				Style = (Style)FindResource("Chip"),
				IsChecked = (root == pick)
			});
		}
	}

	private async void BtnDiskBench_Click(object sender, RoutedEventArgs e)
	{
		if (_gameBenchRunning)
		{
			_gameBenchCts?.Cancel();
			return;
		}
		if (AnyBenchRunning || TestRunning)
		{
			DiskStatus.Text = "Termine o outro teste antes.";
			return;
		}
		string root = CheckedTag(DiskDrivePanel) ?? "";
		if (root.Length == 0)
		{
			DiskStatus.Text = "Escolha uma unidade.";
			return;
		}
		long fileMb = long.Parse(CheckedTag(DiskSizePanel) ?? "1024");
		_gameBenchCts = new CancellationTokenSource();
		CancellationToken ct = _gameBenchCts.Token;
		SetGameBenchRunning(BtnDiskBench, running: true);
		WrapPanel diskDrivePanel = DiskDrivePanel;
		bool isEnabled = (DiskSizePanel.IsEnabled = false);
		diskDrivePanel.IsEnabled = isEnabled;
		DiskResult.ItemsSource = null;
		(string Phase, double Frac) progress = (Phase: "Preparando…", Frac: 0.0);
		DispatcherTimer timer = new DispatcherTimer(TimeSpan.FromMilliseconds(150.0), DispatcherPriority.Normal, (object? _, EventArgs _) =>
		{
			DiskStatus.Text = progress.Phase;
			DiskProgress.ScaleX = progress.Frac;
		}, Dispatcher);
		timer.Start();
		try
		{
			_gpuBenchActive = true;
			DiskBenchResult r = await Task.Run(() => DiskBench.Run(root, fileMb, (string phase, double f) =>
			{
				progress = (Phase: phase, Frac: f);
			}, ct), ct);
			ct.ThrowIfCancellationRequested();
			List<MemRow> rows = new List<MemRow>
			{
				new MemRow("Leitura sequencial", $"{r.SeqReadMBs:N0} MB/s", "Blocos de 1 MB. Mede copiar arquivos grandes e carregar jogos."),
				new MemRow("Escrita sequencial", $"{r.SeqWriteMBs:N0} MB/s", "Blocos de 1 MB, direto no disco. SSDs com cache SLC caem depois de gravar dezenas de GB."),
				new MemRow("Leitura 4 KB aleatória (1 por vez)", $"{r.ReadIopsQ1:N0} IOPS", $"Latência média de {r.ReadLatencyUs:0.0} µs. É o que define a sensação de rapidez do sistema e dos programas."),
				new MemRow("Escrita 4 KB aleatória (1 por vez)", $"{r.WriteIopsQ1:N0} IOPS", $"Latência média de {r.WriteLatencyUs:0.0} µs."),
				new MemRow("Leitura 4 KB aleatória (8 em paralelo)", $"{r.ReadIopsQ8:N0} IOPS", "Várias leituras ao mesmo tempo, como ao abrir muitos programas.")
			};
			DiskResult.ItemsSource = rows;
			DiskStatus.Text = $"Concluído em {r.Drive} · arquivo de teste ({r.FileMb} MB) apagado";
			SaveBenchRecord("disk", r.Score, $"{r.Drive} · {r.SeqReadMBs:N0}/{r.SeqWriteMBs:N0} MB/s · 4K {r.ReadIopsQ1 / 1000.0:0.#}k IOPS");
		}
		catch (OperationCanceledException)
		{
			DiskStatus.Text = "Cancelado (arquivo de teste apagado)";
		}
		catch (Exception ex2)
		{
			Exception inner = (ex2 as AggregateException)?.InnerException ?? ex2;
			DiskStatus.Text = "Falhou: " + inner.Message;
		}
		finally
		{
			_gpuBenchActive = false;
			timer.Stop();
			DiskProgress.ScaleX = 0.0;
			WrapPanel diskDrivePanel2 = DiskDrivePanel;
			isEnabled = (DiskSizePanel.IsEnabled = true);
			diskDrivePanel2.IsEnabled = isEnabled;
			SetGameBenchRunning(BtnDiskBench, running: false);
		}
	}

	private async void BtnReport_Click(object sender, RoutedEventArgs e)
	{
		Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog
		{
			Title = "Exportar relatório",
			Filter = "Relatório HTML (*.html)|*.html|Dados JSON (*.json)|*.json",
			DefaultExt = ".html",
			FileName = $"TRIARCH-relatorio-{DateTime.Now:yyyyMMdd-HHmm}"
		};
		if (dlg.ShowDialog(this) != true)
		{
			return;
		}
		string path = dlg.FileName;
		bool json = string.Equals(System.IO.Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);
		BtnReport.IsEnabled = false;
		BtnReport.Content = "GERANDO…";
		try
		{
			List<SensorRow> sensorsSnapshot = _monitor.Rows.ToList();
			ReportData data = new ReportData
			{
				Date = DateTime.Now,
				Machine = Environment.MachineName,
				AppVersion = (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? ""),
				LogoBase64 = LoadLogoBase64(),
				System = (from n in ((InfoCategories.ItemsSource as IEnumerable<InfoNode>) ?? Enumerable.Empty<InfoNode>()).Select(ReportBuilder.ToNode)
					where n != null
					select n).Cast<ReportNode>().ToList(),
				Gpus = _gpus.Select((GpuInfo g) => new ReportGpu(g.Name, g.Subtitle, (from n in g.Sections.Select(ReportBuilder.ToNode)
					where n != null
					select n).Cast<ReportNode>().ToList())).ToList(),
				Sensors = sensorsSnapshot.Select((SensorRow s) => new ReportSensor(s.Hardware, s.Category, s.Name, s.Value, s.Min, s.Max)).ToList(),
				Benchmarks = BuildBenchList()
			};
			List<DiskHealthCard> disks = await Task.Run(() => DiskHealth.Read(sensorsSnapshot));
			data.Disks.AddRange(disks.Select((DiskHealthCard d) => new ReportDisk(d.Title, d.Meta, d.VerdictText, d.Reasons, d.Metrics)));
			List<DiskHealthCard> power = await Task.Run(() => BatteryInfo.Read(sensorsSnapshot));
			data.Power.AddRange(power.Select((DiskHealthCard d) => new ReportDisk(d.Title, d.Meta, d.VerdictText, d.Reasons, d.Metrics)));
			if (_lastNet != null)
			{
				data.Network.AddRange(_lastNet.Select((DiskHealthCard d) => new ReportDisk(d.Title, d.Meta, d.VerdictText, d.Reasons, d.Metrics)));
			}
			string text = (json ? ReportBuilder.Json(data) : ReportBuilder.Html(data));
			await File.WriteAllTextAsync(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			SystemSub.Text = "Relatório salvo em " + path;
			try
			{
				Process.Start(new ProcessStartInfo(path)
				{
					UseShellExecute = true
				});
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			System.Windows.MessageBox.Show(this, "Não foi possível gerar o relatório:\n" + ex.Message, "Triarch", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		finally
		{
			BtnReport.Content = "EXPORTAR RELATÓRIO";
			BtnReport.IsEnabled = true;
		}
	}

	private List<ReportBench> BuildBenchList()
	{
		List<ReportBench> list = _settings.BenchRecords.Select((BenchRecord benchRecord) => new ReportBench(benchRecord.Date, TestName(benchRecord.Test), benchRecord.Summary)).ToList();
		foreach (BenchRun r in _settings.BenchRuns)
		{
			List<string> parts = new List<string>();
			double? multi = r.Multi;
			if (multi.HasValue)
			{
				double mt = multi.GetValueOrDefault();
				parts.Add($"multi-core {mt:N0} pts");
			}
			multi = r.Single;
			if (multi.HasValue)
			{
				double st = multi.GetValueOrDefault();
				parts.Add($"single-core {st:N0} pts");
			}
			if (r.Sustained)
			{
				parts.Add("sustentado");
			}
			list.Add(new ReportBench(r.Date, "Render (CPU)", string.Join(" · ", parts) + (string.IsNullOrEmpty(r.Cpu) ? "" : (" · " + r.Cpu))));
		}
		return list.OrderByDescending((ReportBench b) => b.Date).ToList();
		static string TestName(string t)
		{
			return t switch
			{
				"gpu" => "Cena de jogo (GPU)", 
				"sim" => "Simulação de jogo (CPU)", 
				"mem" => "Memória e carregamento", 
				"disk" => "Disco", 
				"oc" => "TRIARCH OC (estabilidade)", 
				_ => t, 
			};
		}
	}

	private static string? LoadLogoBase64()
	{
		try
		{
			StreamResourceInfo info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/logo-badge.png"));
			using MemoryStream ms = new MemoryStream();
			info.Stream.CopyTo(ms);
			return Convert.ToBase64String(ms.ToArray());
		}
		catch
		{
			return null;
		}
	}

	private void InitDriver()
	{
		RefreshDduStateAsync();
		if (!_drvInit)
		{
			_drvInit = true;
			BtnDrvScan_Click(this, new RoutedEventArgs());
		}
	}

	private async Task RefreshDduStateAsync()
	{
		try
		{
			_dduGpu = await DduService.DetectPrimaryGpuAsync();
		}
		catch
		{
			_dduGpu = new DduGpuDetection(_gpus.FirstOrDefault()?.Name ?? "GPU não identificada", DduGpuVendor.Unknown);
		}
		bool flag = DduService.IsInstalled();
		string gpuLabel = ((_dduGpu.Vendor != DduGpuVendor.Unknown) ? (_dduGpu.Name + " (" + _dduGpu.VendorLabel + ")") : _dduGpu.Name);
		string statusBadge = (flag ? "Pronto" : "Necessário Baixar");
		string exeDetail = (flag ? "Pronto · resources/tools/ddu/Display Driver Uninstaller.exe" : "Necessário Baixar · download automático na execução");
		System.Windows.Media.Brush badgeBg = (flag ? ((System.Windows.Media.Brush)FindResource("SuccessSoft")) : ((System.Windows.Media.Brush)FindResource("AccentSoft")));
		System.Windows.Media.Brush badgeBorder = (flag ? ((System.Windows.Media.Brush)FindResource("Success")) : ((System.Windows.Media.Brush)FindResource("AccentBorder")));
		System.Windows.Media.Brush badgeFg = (flag ? ((System.Windows.Media.Brush)FindResource("Success")) : ((System.Windows.Media.Brush)FindResource("Accent")));
		TextBlock drvDduGpuName = DrvDduGpuName;
		string text = (OptDduGpuName.Text = gpuLabel);
		drvDduGpuName.Text = text;
		TextBlock drvDduExeStatus = DrvDduExeStatus;
		text = (OptDduExeStatus.Text = exeDetail);
		drvDduExeStatus.Text = text;
		TextBlock drvDduStatusBadge = DrvDduStatusBadge;
		text = (OptDduStatusBadge.Text = statusBadge);
		drvDduStatusBadge.Text = text;
		Border drvDduBadge = DrvDduBadge;
		System.Windows.Media.Brush background = (OptDduBadge.Background = badgeBg);
		drvDduBadge.Background = background;
		Border drvDduBadge2 = DrvDduBadge;
		background = (OptDduBadge.BorderBrush = badgeBorder);
		drvDduBadge2.BorderBrush = background;
		TextBlock drvDduStatusBadge2 = DrvDduStatusBadge;
		background = (OptDduStatusBadge.Foreground = badgeFg);
		drvDduStatusBadge2.Foreground = background;
	}

	private void SetDduBusy(bool busy, string? message = null, int? progressPct = null)
	{
		_dduBusy = busy;
		System.Windows.Controls.Button btnDrvDduOpen = BtnDrvDduOpen;
		bool isEnabled = (BtnOptDduOpen.IsEnabled = !busy);
		btnDrvDduOpen.IsEnabled = isEnabled;
		System.Windows.Controls.Button btnDrvDduSilent = BtnDrvDduSilent;
		isEnabled = (BtnOptDduSilent.IsEnabled = !busy);
		btnDrvDduSilent.IsEnabled = isEnabled;
		Visibility progVis = ((!busy || !progressPct.HasValue) ? Visibility.Collapsed : Visibility.Visible);
		Grid drvDduProgressBox = DrvDduProgressBox;
		Visibility visibility = (OptDduProgressBox.Visibility = progVis);
		drvDduProgressBox.Visibility = visibility;
		if (progressPct.HasValue)
		{
			double scale = Math.Clamp((double)progressPct.Value / 100.0, 0.0, 1.0);
			ScaleTransform drvDduProgress = DrvDduProgress;
			double scaleX = (OptDduProgress.ScaleX = scale);
			drvDduProgress.ScaleX = scaleX;
		}
		if (message != null)
		{
			TextBlock drvDduMessage = DrvDduMessage;
			string text = (OptDduMessage.Text = message);
			drvDduMessage.Text = text;
		}
	}

	private async void BtnDduOpen_Click(object sender, RoutedEventArgs e)
	{
		if (!_dduBusy)
		{
			await RunDduWorkflowAsync(silentMode: false);
		}
	}

	private async void BtnDduSilent_Click(object sender, RoutedEventArgs e)
	{
		if (_dduBusy)
		{
			return;
		}
		if ((object)_dduGpu == null)
		{
			_dduGpu = await DduService.DetectPrimaryGpuAsync();
		}
		if (_dduGpu.Vendor == DduGpuVendor.Unknown)
		{
			System.Windows.MessageBox.Show(this, "Não foi possível identificar automaticamente a fabricante da sua GPU primária (AMD, NVIDIA ou Intel).\n\nUtilize o botão \"Abrir DDU (Painel Completo)\" para selecionar manualmente o dispositivo.", "Desinstalador de Drivers de Vídeo (DDU)", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		string args = DduService.GetSilentArguments(_dduGpu.Vendor);
		if (System.Windows.MessageBox.Show(this, $"ATENÇÃO: Limpeza Rápida e Reinicialização (DDU)\n\n• GPU detectada: {_dduGpu.Name} ({_dduGpu.VendorLabel})\n• Comando DDU: {args}\n\n" + "O Display Driver Uninstaller removerá completamente os drivers de vídeo atuais e REINICIARÁ O COMPUTADOR AUTOMATICAMENTE logo após o expurgo dos drivers.\n\nSalve todos os seus arquivos abertos antes de prosseguir. Deseja iniciar a limpeza profunda agora?", "Confirmar Limpeza Rápida e Reinicialização", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes)
		{
			await RunDduWorkflowAsync(silentMode: true);
		}
	}

	private async Task RunDduWorkflowAsync(bool silentMode)
	{
		if ((object)_dduGpu == null)
		{
			_dduGpu = await DduService.DetectPrimaryGpuAsync();
		}
		int currentPct = (DduService.IsInstalled() ? 100 : 0);
		SetDduBusy(busy: true, DduService.IsInstalled() ? "Solicitando elevação de Administrador (UAC)…" : "Preparando download do DDU portátil…", currentPct);
		Progress<int> intProgress = new Progress<int>((int pct) =>
		{
			currentPct = pct;
			SetDduBusy(busy: true, null, pct);
		});
		Progress<string> textProgress = new Progress<string>((string status) =>
		{
			SetDduBusy(busy: true, status, currentPct);
		});
		try
		{
			DduExecutionResult result = await DduService.LaunchAsync(silentMode, _dduGpu.Vendor, intProgress, textProgress);
			await RefreshDduStateAsync();
			SetDduBusy(busy: false, result.Message);
			if (result.Started)
			{
				AddEvent("\ue896", "DDU", silentMode ? ("Limpeza rápida (" + _dduGpu.VendorLabel + ")") : "Painel completo aberto", "");
			}
		}
		catch (Exception ex)
		{
			await RefreshDduStateAsync();
			SetDduBusy(busy: false, "Falha ao executar o DDU: " + ex.Message);
		}
	}

	private void DrvSet(string text, double? frac = null)
	{
		DrvStatus.Text = text;
		if (frac.HasValue)
		{
			DrvProgress.ScaleX = Math.Clamp(frac.Value, 0.0, 1.0);
		}
	}

	private void DrvBusy(bool busy)
	{
		_drvBusy = busy;
		BtnDrvScan.IsEnabled = !busy;
		BtnDrvBackup.IsEnabled = !busy;
		BtnDrvWuSearch.IsEnabled = !busy;
		BtnDrvInstall.IsEnabled = !busy && _drvGpus.Any((GpuDriverStatus g) => g.CanAutoInstall);
		BtnDrvWuInstall.IsEnabled = !busy && _drvWu.Count > 0;
	}

	private async void BtnDrvScan_Click(object sender, RoutedEventArgs e)
	{
		if (_drvBusy)
		{
			return;
		}
		DrvBusy(busy: true);
		_drvCts = new CancellationTokenSource();
		try
		{
			DrvSet("Lendo drivers instalados…", 0.1);
			List<DriverRow> inv = await Task.Run((Func<List<DriverRow>>)DriverManager.Inventory);
			DrvList.ItemsSource = inv;
			DrvSet("Lendo placa de vídeo e placa-mãe…", 0.3);
			_drvGpus = await Task.Run((Func<List<GpuDriverStatus>>)DriverManager.ReadGpus);
			_drvBoardInfo = await Task.Run((Func<BoardInfo>)DriverManager.ReadBoard);
			ShowBoard();
			bool studio = DrvStudio.IsChecked == true;
			int i = 0;
			foreach (GpuDriverStatus g in _drvGpus)
			{
				DrvSet($"Consultando {g.Vendor.ToUpperInvariant()} para {g.GpuName}…", 0.4 + 0.5 * (double)i / (double)Math.Max(1, _drvGpus.Count));
				if (g.Vendor == "NVIDIA")
				{
					await DriverManager.CheckNvidiaAsync(g, studio, _drvCts.Token);
				}
				else if (g.Vendor == "AMD")
				{
					await DriverManager.CheckAmdAsync(g, _drvCts.Token);
				}
				else if (g.Vendor == "Intel")
				{
					g.Message = g.Vendor + ": use a página oficial para ver a versão mais recente (instalado: " + g.Installed + ").";
				}
				else
				{
					g.Message = "Fabricante não reconhecido.";
				}
				i++;
			}
			DrvGpus.ItemsSource = null;
			DrvGpus.ItemsSource = _drvGpus;
			DrvSet($"Pronto às {DateTime.Now:HH:mm:ss}.", 1.0);
		}
		catch (Exception ex)
		{
			DrvSet("Falhou: " + ex.Message, 0.0);
		}
		finally
		{
			DrvBusy(busy: false);
		}
	}

	private void ShowBoard()
	{
		BoardInfo b = _drvBoardInfo;
		DrvBoard.Text = $"{b.Maker} {b.Model}\nBIOS: {((b.Bios.Length > 0) ? b.Bios : "—")}" + ((b.Brand.Length > 0) ? ("\nFabricante reconhecido: " + b.Brand) : "\nFabricante não reconhecido");
		BtnDrvBoardPage.IsEnabled = b.SupportUrl.Length > 0;
	}

	private static void OpenUrl(string url)
	{
		try
		{
			Process.Start(new ProcessStartInfo(url)
			{
				UseShellExecute = true
			});
		}
		catch
		{
		}
	}

	private void BtnDrvGpuPage_Click(object sender, RoutedEventArgs e)
	{
		OpenUrl((_drvGpus.FirstOrDefault((GpuDriverStatus x) => x.OfficialPage.Length > 0 && x.Vendor != "Intel") ?? _drvGpus.FirstOrDefault((GpuDriverStatus x) => x.OfficialPage.Length > 0))?.OfficialPage ?? "https://www.nvidia.com/Download/index.aspx");
	}

	private void BtnDrvBoardPage_Click(object sender, RoutedEventArgs e)
	{
		OpenUrl(_drvBoardInfo.SupportUrl);
	}

	private async void BtnDrvInstall_Click(object sender, RoutedEventArgs e)
	{
		GpuDriverStatus g = _drvGpus.FirstOrDefault((GpuDriverStatus x) => x.CanAutoInstall);
		if (g == null || _drvBusy)
		{
			return;
		}
		string msg;
		if (g.Vendor == "AMD")
		{
			msg = "Baixar e abrir o instalador AMD Software " + g.Latest + "?\nO arquivo vem do servidor oficial da AMD.";
		}
		else
		{
			msg = (g.UpdateAvailable ? $"Instalar o driver NVIDIA {g.Latest} ({g.SizeText})?\nO instalador oficial será baixado e aberto." : $"Você já está com a versão {g.Installed}. Baixar e abrir o instalador {g.Latest} mesmo assim (reinstalação)?");
		}
		if (System.Windows.MessageBox.Show(this, msg, "DRIVERS", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
		{
			return;
		}
		DrvBusy(busy: true);
		_drvCts = new CancellationTokenSource();
		try
		{
			DrvSet(await DriverManager.DownloadAndLaunchAsync(g, (string t, double f) =>
			{
				Dispatcher.BeginInvoke((Action)(() =>
				{
					DrvSet(t, f);
				}));
			}, _drvCts.Token), 1.0);
		}
		catch (Exception ex)
		{
			DrvSet("Falhou: " + ex.Message, 0.0);
		}
		finally
		{
			DrvBusy(busy: false);
		}
	}

	private async void BtnDrvWuSearch_Click(object sender, RoutedEventArgs e)
	{
		if (_drvBusy)
		{
			return;
		}
		DrvBusy(busy: true);
		DrvSet("Procurando drivers no Windows Update (pode levar 1 minuto)…", 0.3);
		try
		{
			_drvWu = await DriverManager.SearchWindowsUpdateAsync();
			DrvWuList.Text = ((_drvWu.Count == 0) ? "Nenhum driver novo no Windows Update." : string.Join("\n", _drvWu.Select((WuDriver w) => "• " + w.Title + ((w.SizeBytes > 0) ? $" ({(double)w.SizeBytes / 1048576.0:F0} MB)" : ""))));
			DrvSet($"{_drvWu.Count} driver(s) encontrados.", 1.0);
		}
		catch (Exception ex)
		{
			DrvWuList.Text = "Falhou: " + ex.Message;
			DrvSet("Falhou: " + ex.Message, 0.0);
		}
		finally
		{
			DrvBusy(busy: false);
		}
	}

	private async void BtnDrvWuInstall_Click(object sender, RoutedEventArgs e)
	{
		if (_drvBusy || _drvWu.Count == 0 || System.Windows.MessageBox.Show(this, $"Instalar {_drvWu.Count} driver(s) do Windows Update? Pode ser preciso reiniciar.", "DRIVERS", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
		{
			return;
		}
		DrvBusy(busy: true);
		try
		{
			string r = await DriverManager.InstallWindowsUpdateAsync(_drvWu, (string t) =>
			{
				Dispatcher.BeginInvoke((Action)(() =>
				{
					DrvSet(t, 0.5);
				}));
			});
			DrvSet(r, 1.0);
			_drvWu = new List<WuDriver>();
			DrvWuList.Text = r;
		}
		catch (Exception ex)
		{
			DrvSet("Falhou: " + ex.Message, 0.0);
		}
		finally
		{
			DrvBusy(busy: false);
		}
	}

	private async void BtnDrvBackup_Click(object sender, RoutedEventArgs e)
	{
		if (_drvBusy)
		{
			return;
		}
		DrvBusy(busy: true);
		_drvCts = new CancellationTokenSource();
		try
		{
			DrvSet(await DriverManager.BackupAsync((string t) =>
			{
				Dispatcher.BeginInvoke((Action)(() =>
				{
					DrvSet(t, 0.5);
				}));
			}, _drvCts.Token), 1.0);
		}
		catch (Exception ex)
		{
			DrvSet("Falhou: " + ex.Message, 0.0);
		}
		finally
		{
			DrvBusy(busy: false);
		}
	}


	private void InitLauncher()
	{
		if (_launcherInited)
		{
			return;
		}
		_launcherInited = true;
		(string, string, string)[] extraPresets = ExtraPresets;
		for (int i = 0; i < extraPresets.Length; i++)
		{
			(string, string, string) tuple = extraPresets[i];
			string label = tuple.Item1;
			string token = tuple.Item2;
			string hint = tuple.Item3;
			System.Windows.Controls.Button b = new System.Windows.Controls.Button
			{
				Content = label,
				Tag = token,
				Style = (Style)FindResource("OutlineButton"),
				Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
				FontSize = 11.0,
				Margin = new Thickness(0.0, 0.0, 6.0, 6.0),
				ToolTip = hint
			};
			b.Click += (object _, RoutedEventArgs _) =>
			{
				string text = (string)b.Tag;
				if (!LExtra.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
				{
					LExtra.Text = (LExtra.Text.Trim() + " " + text).Trim();
				}
			};
			LExtraPresets.Children.Add(b);
		}
		ScanGamesAsync();
	}

	private async Task ScanGamesAsync()
	{
		BtnScanGames.IsEnabled = false;
		LGameCount.Text = "Procurando seus jogos…";
		try
		{
			List<CustomGame> custom = _settings.CustomGames.ToList();
			_libGames = await Task.Run(() => GameLibrary.Scan(custom));
			ApplyGameFilter();
			IEnumerable<string> byStore = from g in _libGames
				group g by g.Store into x
				select $"{x.Key} {x.Count()}";
			LGameCount.Text = ((_libGames.Count == 0) ? "Nenhum jogo encontrado. Use + ADICIONAR .EXE para incluir um à mão." : $"{_libGames.Count} jogos · {string.Join(" · ", byStore)}");
		}
		catch (Exception ex)
		{
			LGameCount.Text = "Falhou: " + ex.Message;
		}
		finally
		{
			BtnScanGames.IsEnabled = true;
		}
	}

	private void ApplyGameFilter()
	{
		string q = LGameSearch.Text.Trim();
		string keep = _lg?.Key;
		List<LibGame> list = _libGames.Where((LibGame g) => q.Length == 0 || g.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
		LGameList.ItemsSource = list;
		if (keep != null)
		{
			LGameList.SelectedItem = list.FirstOrDefault((LibGame g) => g.Key == keep);
		}
	}

	private void LGameSearch_Changed(object sender, TextChangedEventArgs e)
	{
		if (LGameList != null)
		{
			ApplyGameFilter();
		}
	}

	private async void BtnScanGames_Click(object sender, RoutedEventArgs e)
	{
		await ScanGamesAsync();
	}

	private async void BtnAddExe_Click(object sender, RoutedEventArgs e)
	{
		Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog
		{
			Filter = "Programas (*.exe)|*.exe",
			Title = "Escolha o .exe do jogo"
		};
		if (dlg.ShowDialog(this) == true)
		{
			if (!_settings.CustomGames.Any((CustomGame c) => c.Exe.Equals(dlg.FileName, StringComparison.OrdinalIgnoreCase)))
			{
				_settings.CustomGames.Add(new CustomGame
				{
					Name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName),
					Exe = dlg.FileName
				});
				_settings.Save();
			}
			await ScanGamesAsync();
			LGameList.SelectedItem = (LGameList.ItemsSource as IEnumerable<LibGame>)?.FirstOrDefault((LibGame g) => g.Store == "Manual" && g.Id == dlg.FileName);
		}
	}

	private static void SetChip(System.Windows.Controls.Panel panel, string tag)
	{
		List<System.Windows.Controls.RadioButton> chips = panel.Children.OfType<System.Windows.Controls.RadioButton>().ToList();
		System.Windows.Controls.RadioButton hit = chips.FirstOrDefault((System.Windows.Controls.RadioButton c) => (string)c.Tag == tag) ?? chips.FirstOrDefault();
		if (hit != null)
		{
			hit.IsChecked = true;
		}
	}

	private async void LGameList_Changed(object sender, SelectionChangedEventArgs e)
	{
		_lg = LGameList.SelectedItem as LibGame;
		if (_lg == null)
		{
			LDetail.Visibility = Visibility.Collapsed;
			LEmpty.Visibility = Visibility.Visible;
			_lp = null;
			return;
		}
		LDetail.Visibility = Visibility.Visible;
		LEmpty.Visibility = Visibility.Collapsed;
		LibGame g = _lg;
		if (!_settings.GameProfiles.TryGetValue(g.Key, out GameProfile p))
		{
			p = new GameProfile();
			_settings.GameProfiles[g.Key] = p;
		}
		_lLoading = true;
		_lp = p;
		LTitle.Text = g.Name;
		LSub.Text = g.Store + " · " + g.InstallDir;
		SetChip(LPriorityPanel, p.Priority);
		LChkPlan.IsChecked = p.HighPerfPlan;
		LChkGpu.IsChecked = p.GpuHigh;
		LChkFso.IsChecked = p.DisableFso;
		SetChip(LModePanel, p.WinMode);
		SetChip(LResPanel, p.Res);
		SetChip(LHzPanel, p.Hz.ToString());
		SetChip(LStylePanel, p.Style);
		LExtra.Text = p.Extra;
		LHelpers.ItemsSource = null;
		LHelpers.ItemsSource = p.Helpers;
		LSteamBox.Visibility = ((!g.IsSteam) ? Visibility.Collapsed : Visibility.Visible);
		TextBlock lSteamNow = LSteamNow;
		string text;
		if (!g.IsSteam)
		{
			text = "";
		}
		else
		{
			string cur = SteamInfo.GetLaunchOptions(g.Id);
			text = "Opções atuais na Steam: " + ((cur != null && cur.Length > 0) ? cur : "(nenhuma)");
		}
		lSteamNow.Text = text;
		TextBlock lGfxNote = LGfxNote;
		string text2;
		if (g.IsSteam)
		{
			text2 = "Os argumentos só passam a valer na Steam depois de APLICAR NAS OPÇÕES DA STEAM (a Steam é fechada nessa hora). Depois, o JOGAR abre pela Steam normalmente.";
		}
		else
		{
			text2 = ((g.LaunchUri != null) ? "Esta loja abre o jogo por um atalho que não aceita argumentos: use as opções de inicialização da própria loja. Desempenho e programas auxiliares funcionam normalmente." : "Os argumentos são enviados direto ao .exe quando você clica em JOGAR.");
		}
		lGfxNote.Text = text2;
		LStatus.Text = "";
		_lLoading = false;
		UpdateLauncherPreview();
		await ShowExeAsync(g, p);
	}

	private async Task ShowExeAsync(LibGame g, GameProfile p)
	{
		string exe = p.ExePath ?? g.ExePath;
		if (exe == null)
		{
			LExeText.Text = "Procurando o executável…";
			if (!_exeGuess.TryGetValue(g.Key, out exe))
			{
				string dir = g.InstallDir;
				string name = g.Name;
				exe = await Task.Run(() => GameLauncher.FindMainExe(dir, name));
				_exeGuess[g.Key] = exe;
			}
			if (_lg == g)
			{
				LExeText.Text = ((exe != null) ? ("Executável (detectado, confirme): " + exe) : "Executável não identificado. Ele é descoberto ao jogar, ou escolha manualmente.");
			}
		}
		else
		{
			LExeText.Text = "Executável: " + exe;
		}
	}

	private void SaveProfileFromControls()
	{
		if (!_lLoading && _lp != null && LPreview != null)
		{
			_lp.Priority = CheckedTag(LPriorityPanel) ?? "normal";
			_lp.HighPerfPlan = LChkPlan.IsChecked == true;
			_lp.GpuHigh = LChkGpu.IsChecked == true;
			_lp.DisableFso = LChkFso.IsChecked == true;
			_lp.WinMode = CheckedTag(LModePanel) ?? "default";
			_lp.Res = CheckedTag(LResPanel) ?? "default";
			_lp.Hz = (int.TryParse(CheckedTag(LHzPanel), out var hz) ? hz : 0);
			_lp.Style = CheckedTag(LStylePanel) ?? "source";
			_lp.Extra = LExtra.Text.Trim();
			try
			{
				_settings.Save();
			}
			catch
			{
			}
			UpdateLauncherPreview();
		}
	}

	private void LProfile_Changed(object sender, RoutedEventArgs e)
	{
		SaveProfileFromControls();
	}

	private void LExtra_Changed(object sender, TextChangedEventArgs e)
	{
		SaveProfileFromControls();
	}

	private void UpdateLauncherPreview()
	{
		if (_lp != null && LPreview != null)
		{
			string a = GameLauncher.BuildArgs(_lp);
			LPreview.Text = ((a.Length > 0) ? a : "(nenhum argumento)");
		}
	}

	private void BtnRecommended_Click(object sender, RoutedEventArgs e)
	{
		if (_lp != null)
		{
			_lLoading = true;
			SetChip(LPriorityPanel, "high");
			LChkPlan.IsChecked = true;
			LChkGpu.IsChecked = true;
			LChkFso.IsChecked = true;
			_lLoading = false;
			SaveProfileFromControls();
			LStatus.Text = "Perfil para mais FPS ligado: prioridade alta, Alto desempenho, GPU dedicada e sem otimizações de tela cheia.";
		}
	}

	private async void BtnPickExe_Click(object sender, RoutedEventArgs e)
	{
		if (_lg == null || _lp == null)
		{
			return;
		}
		Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog
		{
			Filter = "Programas (*.exe)|*.exe",
			Title = "Escolha o .exe do jogo",
			InitialDirectory = (Directory.Exists(_lg.InstallDir) ? _lg.InstallDir : "")
		};
		if (dlg.ShowDialog(this) == true)
		{
			_lp.ExePath = dlg.FileName;
			try
			{
				_settings.Save();
			}
			catch
			{
			}
			await ShowExeAsync(_lg, _lp);
		}
	}

	private void BtnAddHelper_Click(object sender, RoutedEventArgs e)
	{
		if (_lp == null)
		{
			return;
		}
		Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog
		{
			Filter = "Programas e scripts|*.exe;*.bat;*.cmd;*.lnk",
			Title = "Programa para abrir junto com o jogo"
		};
		if (dlg.ShowDialog(this) == true)
		{
			_lp.Helpers.Add(new HelperProgram
			{
				Path = dlg.FileName,
				CloseWithGame = (LHelperClose.IsChecked == true)
			});
			try
			{
				_settings.Save();
			}
			catch
			{
			}
			LHelpers.ItemsSource = null;
			LHelpers.ItemsSource = _lp.Helpers;
		}
	}

	private void BtnRemoveHelper_Click(object sender, RoutedEventArgs e)
	{
		if (_lp != null && ((FrameworkElement)sender).Tag is HelperProgram h)
		{
			_lp.Helpers.Remove(h);
			try
			{
				_settings.Save();
			}
			catch
			{
			}
			LHelpers.ItemsSource = null;
			LHelpers.ItemsSource = _lp.Helpers;
		}
	}

	private async void BtnSteamApply_Click(object sender, RoutedEventArgs e)
	{
		LibGame g = _lg;
		if (g == null || !g.IsSteam || _lp == null)
		{
			return;
		}
		string opts = GameLauncher.BuildArgs(_lp);
		BtnSteamApply.IsEnabled = false;
		try
		{
			if (SteamInfo.IsRunning)
			{
				if (System.Windows.MessageBox.Show(this, "A Steam precisa ser fechada para gravar as opções de inicialização (senão ela as sobrescreve ao sair).\n\nFechar a Steam agora? Jogos abertos serão encerrados. Feito um backup do arquivo antes de alterar.", "JOGOS · Steam", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
				{
					return;
				}
				LStatus.Text = "Fechando a Steam…";
				if (!(await SteamInfo.ShutdownAsync()))
				{
					LStatus.Text = "A Steam não fechou. Feche-a manualmente e tente de novo.";
					return;
				}
			}
			string err = await Task.Run(() => SteamInfo.SetLaunchOptions(g.Id, opts));
			LStatus.Text = err ?? ((opts.Length > 0) ? ("Opções gravadas na Steam: " + opts + ". Clique em JOGAR (a Steam abre sozinha).") : "Opções da Steam limpas para este jogo.");
			TextBlock lSteamNow = LSteamNow;
			string cur = SteamInfo.GetLaunchOptions(g.Id);
			lSteamNow.Text = "Opções atuais na Steam: " + ((cur != null && cur.Length > 0) ? cur : "(nenhuma)");
		}
		finally
		{
			BtnSteamApply.IsEnabled = true;
		}
	}

	private async void BtnPlay_Click(object sender, RoutedEventArgs e)
	{
		if (_launchCts != null)
		{
			_launchCts.Cancel();
			return;
		}
		LibGame g = _lg;
		GameProfile p = _lp;
		if (g == null || p == null)
		{
			return;
		}
		_launchCts = new CancellationTokenSource();
		BtnPlay.Content = "PARAR ACOMPANHAMENTO";
		try
		{
			await GameLauncher.RunAsync(g, p, (string t) =>
			{
				Dispatcher.BeginInvoke((Action)(() =>
				{
					LStatus.Text = t;
				}));
			}, (string exe) =>
			{
				Dispatcher.BeginInvoke((Action)(() =>
				{
					if (!string.Equals(p.ExePath, exe, StringComparison.OrdinalIgnoreCase))
					{
						p.ExePath = exe;
						try
						{
							_settings.Save();
						}
						catch
						{
						}
						if (_lg == g)
						{
							LExeText.Text = "Executável: " + exe;
						}
					}
				}));
			}, _launchCts.Token);
		}
		finally
		{
			_launchCts?.Dispose();
			_launchCts = null;
			BtnPlay.Content = "JOGAR";
		}
	}

	private async void RefreshEnergy()
	{
		if (_energyBusy)
		{
			return;
		}
		_energyBusy = true;
		BtnEnergyRefresh.IsEnabled = false;
		EnergyStatus.Text = "Lendo bateria e energia…";
		try
		{
			List<SensorRow> sensors = _monitor.Rows.ToList();
			List<DiskHealthCard> cards = await Task.Run(() => BatteryInfo.Read(sensors));
			EnergyCards.ItemsSource = cards;
			bool hasBattery = cards.Count > 1;
			EnergyStatus.Text = (hasBattery ? "Bateria detectada" : "Nenhuma bateria (PC de mesa)") + $" · lido às {DateTime.Now:HH:mm:ss}";
		}
		catch (Exception ex)
		{
			EnergyStatus.Text = "Falhou: " + ex.Message;
		}
		finally
		{
			_energyBusy = false;
			BtnEnergyRefresh.IsEnabled = true;
		}
	}

	private void BtnEnergyRefresh_Click(object sender, RoutedEventArgs e)
	{
		RefreshEnergy();
	}

	private async void BtnNetTest_Click(object sender, RoutedEventArgs e)
	{
		if (_netRunning)
		{
			_netCts?.Cancel();
			return;
		}
		if (AnyBenchRunning || TestRunning)
		{
			NetStatus.Text = "Termine o outro teste antes.";
			return;
		}
		_netRunning = true;
		_netCts = new CancellationTokenSource();
		CancellationToken ct = _netCts.Token;
		BtnNetTest.Content = "CANCELAR";
		NetCards.ItemsSource = null;
		NetProgress.ScaleX = 0.0;
		try
		{
			List<DiskHealthCard> cards = (_lastNet = await NetworkTest.Run((string text, double frac) =>
			{
				Dispatcher.BeginInvoke((Action)(() =>
				{
					NetStatus.Text = text;
					NetProgress.ScaleX = Math.Clamp(frac, 0.0, 1.0);
				}));
			}, ct));
			NetCards.ItemsSource = cards;
			NetStatus.Text = $"Concluído às {DateTime.Now:HH:mm:ss}. O resultado entra no relatório exportado.";
			NetProgress.ScaleX = 1.0;
		}
		catch (OperationCanceledException)
		{
			NetStatus.Text = "Teste cancelado.";
			NetProgress.ScaleX = 0.0;
		}
		catch (Exception ex2)
		{
			NetStatus.Text = "Falhou: " + ex2.Message;
			NetProgress.ScaleX = 0.0;
		}
		finally
		{
			_netRunning = false;
			_netCts?.Dispose();
			_netCts = null;
			BtnNetTest.Content = "TESTAR";
		}
	}

	private async void BtnNetHostTest_Click(object sender, RoutedEventArgs e)
	{
		string target = NetHostInput.Text;
		BtnNetHostTest.IsEnabled = false;
		NetToolResult.Text = "Testando conectividade com " + target + "…";
		try
		{
			NetToolResult.Text = await NetworkTest.TestHostPortAsync(target);
		}
		catch (Exception ex)
		{
			NetToolResult.Text = "Erro: " + ex.Message;
		}
		finally
		{
			BtnNetHostTest.IsEnabled = true;
		}
	}

	private async void BtnNetQuickTool_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { Tag: string mode })
		{
			if (mode == "winsock" && !RequireAdmin())
			{
				return;
			}
			NetToolResult.Text = "Executando comando de rede…";
			NetToolResult.Text = await NetworkTest.RunNetCmdAsync(mode);
		}
	}

	private void BtnNetCopy_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			sb.AppendLine($"=== TRIARCH v1.0 · DIAGNÓSTICO DE REDE ({Environment.MachineName}) ===");
			sb.AppendLine($"Data/Hora: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
			if (_lastNet != null && _lastNet.Count > 0)
			{
				foreach (DiskHealthCard c in _lastNet)
				{
					sb.AppendLine($"Status: {c.VerdictText} ({c.Meta})");
					sb.AppendLine($"Parecer: {c.Reasons}");
					foreach (KeyValuePair<string, string> kv in c.Metrics)
					{
						sb.AppendLine($"- {kv.Key}: {kv.Value}");
					}
				}
			}
			else
			{
				sb.AppendLine("(Teste completo ainda não executado nesta sessão)");
			}
			if (!string.IsNullOrWhiteSpace(NetToolResult.Text))
			{
				sb.AppendLine("--- Teste Rápido / Conectividade ---");
				sb.AppendLine(NetToolResult.Text);
			}
			System.Windows.Clipboard.SetText(sb.ToString().Trim());
			NetStatus.Text = "Diagnóstico de rede copiado para a área de transferência!";
		}
		catch (Exception ex)
		{
			NetStatus.Text = "Não foi possível copiar: " + ex.Message;
		}
	}

	private void BtnOpenAppData_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Directory.CreateDirectory(AppSettings.Folder);
			Process.Start(new ProcessStartInfo("explorer.exe", "\"" + AppSettings.Folder + "\"")
			{
				UseShellExecute = true
			});
		}
		catch
		{
		}
	}

	private void BtnOpenDriversBackup_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Triarch-Drivers");
			Directory.CreateDirectory(dir);
			Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"")
			{
				UseShellExecute = true
			});
		}
		catch
		{
		}
	}

	private void UnusedCopyAnchor()
	{
		if (false)
		{
		}
	}

	private void UnusedNetAnchor()
	{
		if (false)
		{
		}
	}

	private void Theme_Click(object sender, RoutedEventArgs e)
	{
		ThemeManager.Apply(!ThemeManager.IsLight);
		_settings.LightTheme = ThemeManager.IsLight;
		try
		{
			_settings.Save();
		}
		catch
		{
		}
		UpdateThemeButton();
		if (PageDash.IsVisible)
		{
			UpdateDashboard(full: false);
		}
		if (PageStress.IsVisible)
		{
			DrawChart();
		}
	}

	private void UpdateThemeButton()
	{
		bool light = ThemeManager.IsLight;
		BtnTheme.Content = (light ? "\ue708" : "\ue706");
		BtnTheme.ToolTip = (light ? "Modo escuro" : "Modo claro");
		if (ChkSettingsLightTheme != null)
		{
			ChkSettingsLightTheme.IsChecked = light;
		}
		TitleLogo.Source = new BitmapImage(new Uri(light ? "pack://application:,,,/Assets/logo-dark.png" : "pack://application:,,,/Assets/logo.png"));
	}

	private void InitOc()
	{
		nint ocGpu = OcGpu;
		OcGpuTitle.Text = ((_gpus.Count > 0) ? ("GPU · " + GameCatalog.ShortGpuName(_gpus[0].Name).ToUpperInvariant()) : "GPU");
		if (ocGpu == IntPtr.Zero)
		{
			OcGpuState.Text = "Ajustes disponíveis só para placas NVIDIA (via driver oficial).";
			Slider ocPower = OcPower;
			bool flag = (BtnOcApply.IsEnabled = false);
			bool isEnabled = flag;
			ocPower.IsEnabled = isEnabled;
		}
		else
		{
			(uint, uint)? tuple = Nvml.PowerConstraintsMw(ocGpu);
			if (tuple.HasValue)
			{
				(uint, uint) valueOrDefault = tuple.GetValueOrDefault();
				OcPower.Minimum = (double)valueOrDefault.Item1 / 1000.0;
				OcPower.Maximum = (double)valueOrDefault.Item2 / 1000.0;
			}
			uint? num = Nvml.PowerLimitMw(ocGpu);
			if (num.HasValue)
			{
				uint valueOrDefault2 = num.GetValueOrDefault();
				OcPower.Value = (double)valueOrDefault2 / 1000.0;
			}
			uint valueOrDefault3 = Nvml.MaxClock(ocGpu, 0) ?? 3000;
			Chip("Sem teto", null, on: true);
			Chip($"Leve · {valueOrDefault3 - 300} MHz", valueOrDefault3 - 300, on: false);
			Chip($"Equilibrado · {valueOrDefault3 - 500} MHz", valueOrDefault3 - 500, on: false);
			Chip($"Econômico · {valueOrDefault3 - 800} MHz", valueOrDefault3 - 800, on: false);
			UpdateOcGpuState();
		}
		BuildOcGuide();
		UpdateOcHistory();
		UpdateRecentErrors();
		void Chip(string label, uint? cap, bool on)
		{
			System.Windows.Controls.RadioButton radioButton = new System.Windows.Controls.RadioButton
			{
				Content = label,
				GroupName = "OcClock",
				Style = (Style)FindResource("Chip"),
				IsChecked = on
			};
			radioButton.Checked += delegate
			{
				_ocPendingCap = cap;
			};
			OcClockPanel.Children.Add(radioButton);
		}
	}

	private void UpdateOcGpuState()
	{
		nint ocGpu = OcGpu;
		if (ocGpu != IntPtr.Zero)
		{
			uint? num = Nvml.PowerLimitMw(ocGpu);
			object obj;
			if (num.HasValue)
			{
				uint valueOrDefault = num.GetValueOrDefault();
				obj = $"{(double)valueOrDefault / 1000.0:0} W";
			}
			else
			{
				obj = "—";
			}
			string value = (string)obj;
			num = Nvml.PowerDefaultMw(ocGpu);
			object obj2;
			if (num.HasValue)
			{
				uint valueOrDefault2 = num.GetValueOrDefault();
				obj2 = $"{(double)valueOrDefault2 / 1000.0:0} W";
			}
			else
			{
				obj2 = "—";
			}
			string value2 = (string)obj2;
			TextBlock ocGpuState = OcGpuState;
			string obj3 = $"Limite de energia atual {value} (padrão {value2}) · teto de clock: ";
			num = _ocClockCap;
			object obj4;
			if (num.HasValue)
			{
				uint valueOrDefault3 = num.GetValueOrDefault();
				obj4 = $"{valueOrDefault3} MHz";
			}
			else
			{
				obj4 = "nenhum";
			}
			num = Nvml.Clock(ocGpu, 0);
			object obj5;
			if (num.HasValue)
			{
				uint valueOrDefault4 = num.GetValueOrDefault();
				obj5 = $"{valueOrDefault4} MHz";
			}
			else
			{
				obj5 = "—";
			}
			ocGpuState.Text = obj3 + (string)obj4 + " · clock máximo atual: " + (string)obj5;
		}
	}

	private void OcPower_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (OcPowerValue != null)
		{
			OcPowerValue.Text = $"{OcPower.Value:0} W";
		}
	}

	private void BtnOcApply_Click(object sender, RoutedEventArgs e)
	{
		nint ocGpu = OcGpu;
		if (ocGpu == IntPtr.Zero || !RequireAdmin())
		{
			return;
		}
		List<string> list = new List<string>();
		string text = Nvml.Explain(Nvml.SetPowerLimitMw(ocGpu, (uint)(OcPower.Value * 1000.0)));
		list.Add((text != null) ? ("Energia: não aplicado (" + text + ").") : $"Energia: {OcPower.Value:0} W aplicado.");
		uint? ocPendingCap = _ocPendingCap;
		int code2;
		if (ocPendingCap.HasValue)
		{
			uint valueOrDefault = ocPendingCap.GetValueOrDefault();
			code2 = Nvml.LockGpuClocks(ocGpu, 210u, valueOrDefault);
		}
		else
		{
			code2 = Nvml.ResetGpuClocks(ocGpu);
		}
		string text2 = Nvml.Explain(code2);
		if (text2 != null)
		{
			list.Add("Teto de clock: não aplicado (" + text2 + ").");
		}
		else
		{
			_ocClockCap = _ocPendingCap;
			ocPendingCap = _ocClockCap;
			object item;
			if (ocPendingCap.HasValue)
			{
				uint valueOrDefault2 = ocPendingCap.GetValueOrDefault();
				item = $"Teto de clock: {valueOrDefault2} MHz aplicado.";
			}
			else
			{
				item = "Teto de clock: removido.";
			}
			list.Add((string)item);
		}
		OcGpuResult.Text = string.Join(" ", list) + " Rode o verificador para confirmar a estabilidade.";
		UpdateOcGpuState();
	}

	private void BtnOcReset_Click(object sender, RoutedEventArgs e)
	{
		nint ocGpu = OcGpu;
		if (ocGpu == IntPtr.Zero || !RequireAdmin())
		{
			return;
		}
		uint? num = Nvml.PowerDefaultMw(ocGpu);
		int num2;
		if (num.HasValue)
		{
			uint valueOrDefault = num.GetValueOrDefault();
			num2 = Nvml.SetPowerLimitMw(ocGpu, valueOrDefault);
		}
		else
		{
			num2 = 3;
		}
		int code = num2;
		int num3 = Nvml.ResetGpuClocks(ocGpu);
		num = Nvml.PowerDefaultMw(ocGpu);
		if (num.HasValue)
		{
			uint valueOrDefault2 = num.GetValueOrDefault();
			OcPower.Value = (double)valueOrDefault2 / 1000.0;
		}
		foreach (System.Windows.Controls.RadioButton item in OcClockPanel.Children.OfType<System.Windows.Controls.RadioButton>())
		{
			item.IsChecked = item.Content as string == "Sem teto";
		}
		if (num3 == 0)
		{
			_ocClockCap = null;
		}
		TextBlock ocGpuResult = OcGpuResult;
		string text = Nvml.Explain(code) ?? Nvml.Explain(num3);
		ocGpuResult.Text = ((text != null) ? ("Restauração parcial: " + text + ".") : "GPU de volta ao padrão de fábrica.");
		UpdateOcGpuState();
	}

	private void UpdateRecentErrors()
	{
		HardwareErrors hardwareErrors = OcCheck.ErrorsSince(DateTime.Now.AddDays(-7.0));
		TextBlock ocRecentErrors = OcRecentErrors;
		string text;
		if (hardwareErrors.Error != null)
		{
			text = "Não foi possível ler o registro: " + hardwareErrors.Error;
		}
		else
		{
			text = ((hardwareErrors.Total == 0) ? "Nenhum erro de hardware nem reinício do driver de vídeo nos últimos 7 dias." : $"Últimos 7 dias: {hardwareErrors.Whea} erros WHEA · {hardwareErrors.GpuResets} reinícios do driver de vídeo.\nÚltimo: {hardwareErrors.LastDetail}");
		}
		ocRecentErrors.Text = text;
	}

	private void UpdateOcHistory()
	{
		List<BenchRecord> source = _settings.BenchRecords.Where((BenchRecord r) => r.Test == "oc").ToList();
		OcHistory.ItemsSource = source.Select((BenchRecord r) => new MeterItem($"{r.Date:dd/MM HH:mm}", r.Summary, r.Score)).ToList();
	}

	private async void BtnOcCheck_Click(object sender, RoutedEventArgs e)
	{
		if (_ocRunning)
		{
			_ocCts?.Cancel();
			return;
		}
		if (TestRunning || AnyBenchRunning)
		{
			OcStatus.Text = "Termine o outro teste antes.";
			return;
		}
		string text = CheckedTag(OcTargetPanel) ?? "cpu";
		bool flag = ((text == "cpu" || text == "both") ? true : false);
		bool cpu = flag;
		flag = ((text == "gpu" || text == "both") ? true : false);
		bool gpu = flag;
		TimeSpan duration = TimeSpan.FromMinutes(int.Parse(CheckedTag(OcDurPanel) ?? "15"));
		string label = ((OcLabel.Text.Trim().Length > 0) ? OcLabel.Text.Trim() : "sem descrição");
		_ocCts = new CancellationTokenSource();
		CancellationToken ct = _ocCts.Token;
		_ocRunning = true;
		BtnOcCheck.Content = "CANCELAR";
		BtnOcCheck.Style = (Style)FindResource("OutlineButton");
		OcVerdictCard.Visibility = Visibility.Collapsed;
		StackPanel ocTargetPanel = OcTargetPanel;
		bool flag2 = (OcDurPanel.IsEnabled = false);
		flag = flag2;
		ocTargetPanel.IsEnabled = flag;
		DateTime start = DateTime.Now;
		double? cpuScore = null;
		double? gpuScore = null;
		float maxCpu = 0f;
		float maxGpu = 0f;
		double sumCpuW = 0.0;
		double sumGpuW = 0.0;
		int nCpuW = 0;
		int nGpuW = 0;
		string fail = null;
		StressTester stress = new StressTester();
		GpuBurn burn = new GpuBurn();
		try
		{
			if (cpu)
			{
				OcStatus.Text = "Medindo a nota da CPU (render multi-core)…";
				BenchResult benchResult = await Task.Run(() => _bench.Run(Environment.ProcessorCount, TimeSpan.Zero, ct), ct);
				if (!ct.IsCancellationRequested)
				{
					cpuScore = benchResult.Score;
				}
			}
			if (gpu && !ct.IsCancellationRequested)
			{
				OcStatus.Text = "Medindo a nota da GPU (cena de jogo, 30 s)…";
				GpuGameBench gb = new GpuGameBench
				{
					Duration = TimeSpan.FromSeconds(30.0),
					AdapterName = ((_gpus.Count > 0) ? _gpus[0].Name : "")
				};
				_gpuBenchActive = true;
				using (ct.Register(gb.Cancel))
				{
					GpuGameResult gpuGameResult = await Task.Factory.StartNew((Func<GpuGameResult>)gb.Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
					if (gpuGameResult != null)
					{
						gpuScore = gpuGameResult.Score;
					}
					else if (!gb.Cancelled)
					{
						fail = "a cena de GPU falhou (" + gb.Error + ")";
					}
				}
				_gpuBenchActive = false;
			}
			if (fail == null && !ct.IsCancellationRequested)
			{
				if (cpu)
				{
					stress.Start(cpu: true, ram: true);
				}
				if (gpu)
				{
					burn.Start(new GpuBurnSettings
					{
						Width = 1920,
						Height = 1080,
						Msaa = 4,
						Shells = 128,
						Iterations = 48,
						AdapterName = ((_gpus.Count > 0) ? _gpus[0].Name : "")
					});
				}
				Stopwatch sw = Stopwatch.StartNew();
				float cw = 0f;
				float gw = 0f;
				while (sw.Elapsed < duration && !ct.IsCancellationRequested)
				{
					await Task.Delay(1000);
					float? cpuTemperature = _monitor.CpuTemperature;
					float? num = MainGpuTemp();
					if (cpuTemperature.HasValue)
					{
						float valueOrDefault = cpuTemperature.GetValueOrDefault();
						maxCpu = Math.Max(maxCpu, valueOrDefault);
					}
					if (num.HasValue)
					{
						float valueOrDefault2 = num.GetValueOrDefault();
						maxGpu = Math.Max(maxGpu, valueOrDefault2);
					}
					float? cpuPower = _monitor.CpuPower;
					int num2;
					if (cpuPower.HasValue)
					{
						cw = cpuPower.GetValueOrDefault();
						num2 = 1;
					}
					else
					{
						num2 = 0;
					}
					if (((uint)num2 & (cpu ? 1u : 0u)) != 0)
					{
						sumCpuW += (double)cw;
						nCpuW++;
					}
					cpuPower = MainGpuPower();
					int num3;
					if (cpuPower.HasValue)
					{
						gw = cpuPower.GetValueOrDefault();
						num3 = 1;
					}
					else
					{
						num3 = 0;
					}
					if (((uint)num3 & (gpu ? 1u : 0u)) != 0)
					{
						sumGpuW += (double)gw;
						nGpuW++;
					}
					OcProgress.ScaleX = sw.Elapsed / duration;
					OcStatus.Text = $"Stress {((cpu & gpu) ? "CPU + RAM + GPU" : (cpu ? "CPU + RAM" : "GPU"))} · {sw.Elapsed:mm\\:ss} de {duration:mm\\:ss} · máx. CPU {((maxCpu > 0f) ? $"{maxCpu:0} °C" : "—")} · máx. GPU {((maxGpu > 0f) ? $"{maxGpu:0} °C" : "—")}";
					if (cpu && cpuTemperature >= 95f)
					{
						fail = $"CPU chegou a {cpuTemperature:0} °C (limite de segurança 95 °C)";
						break;
					}
					if (gpu && num >= 88f)
					{
						fail = $"GPU chegou a {num:0} °C (limite de segurança 88 °C)";
						break;
					}
					if (cpu && stress.MemoryErrors > 0)
					{
						fail = $"{stress.MemoryErrors} erros de memória (RAM/EXPO instável)";
						break;
					}
					if (gpu && !burn.Running)
					{
						fail = ((burn.Error != null) ? ("a GPU falhou (" + burn.Error + ")") : "a janela do teste de GPU foi fechada");
						break;
					}
					if ((int)sw.Elapsed.TotalSeconds % 5 == 0)
					{
						HardwareErrors hardwareErrors = OcCheck.ErrorsSince(start);
						if (hardwareErrors.Total > 0)
						{
							fail = $"o Windows registrou {hardwareErrors.Whea} erros WHEA e {hardwareErrors.GpuResets} reinícios do driver de vídeo";
							break;
						}
					}
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			stress.Stop();
			burn.Stop();
			_gpuBenchActive = false;
		}
		_ocRunning = false;
		BtnOcCheck.Content = "INICIAR VERIFICAÇÃO";
		BtnOcCheck.Style = (Style)FindResource("PrimaryButton");
		StackPanel ocTargetPanel2 = OcTargetPanel;
		flag2 = (OcDurPanel.IsEnabled = true);
		flag = flag2;
		ocTargetPanel2.IsEnabled = flag;
		OcProgress.ScaleX = 0.0;
		if (ct.IsCancellationRequested && fail == null)
		{
			OcStatus.Text = "Verificação cancelada.";
			return;
		}
		HardwareErrors hardwareErrors2 = OcCheck.ErrorsSince(start);
		if (fail == null && hardwareErrors2.Total > 0)
		{
			fail = $"o Windows registrou {hardwareErrors2.Whea} erros WHEA e {hardwareErrors2.GpuResets} reinícios do driver de vídeo";
		}
		bool flag5 = fail == null;
		OcStatus.Text = $"Concluído em {DateTime.Now - start:mm\\:ss}.";
		OcVerdict.Text = (flag5 ? "ESTÁVEL" : "INSTÁVEL");
		OcVerdictWhy.Text = (flag5 ? $"Nenhum erro em {duration.TotalMinutes:0} min de stress. Para Curve Optimizer, use o PC normalmente por um dia e confira os erros recentes." : ("Motivo: " + fail + ". Volte um passo no ajuste e teste de novo."));
		OcVerdictCard.Background = (flag5 ? Card : Fg);
		TextBlock ocVerdict = OcVerdict;
		System.Windows.Media.Brush brush = (OcVerdictWhy.Foreground = (flag5 ? Fg : Ink));
		System.Windows.Media.Brush foreground = brush;
		ocVerdict.Foreground = foreground;
		List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>
		{
			new KeyValuePair<string, string>("Configuração", label)
		};
		if (cpuScore.HasValue)
		{
			double valueOrDefault3 = cpuScore.GetValueOrDefault();
			list.Add(new KeyValuePair<string, string>("Nota CPU (render)", $"{valueOrDefault3:N0} pts"));
		}
		if (gpuScore.HasValue)
		{
			double valueOrDefault4 = gpuScore.GetValueOrDefault();
			list.Add(new KeyValuePair<string, string>("Nota GPU (cena)", $"{valueOrDefault4:N0} pts"));
		}
		if (maxCpu > 0f)
		{
			list.Add(new KeyValuePair<string, string>("Temp. máx. CPU", $"{maxCpu:0} °C"));
		}
		if (maxGpu > 0f)
		{
			list.Add(new KeyValuePair<string, string>("Temp. máx. GPU", $"{maxGpu:0} °C"));
		}
		if (nCpuW > 0)
		{
			list.Add(new KeyValuePair<string, string>("Consumo médio CPU", $"{sumCpuW / (double)nCpuW:0} W"));
		}
		if (nGpuW > 0)
		{
			list.Add(new KeyValuePair<string, string>("Consumo médio GPU", $"{sumGpuW / (double)nGpuW:0} W"));
		}
		list.Add(new KeyValuePair<string, string>("Erros WHEA / resets de vídeo", $"{hardwareErrors2.Whea} / {hardwareErrors2.GpuResets}"));
		OcResult.ItemsSource = list;
		OcResult.Foreground = (flag5 ? Fg : Ink);
		OcVerdictCard.Visibility = Visibility.Visible;
		string[] array = new string[5]
		{
			flag5 ? "estável" : "INSTÁVEL",
			" · ",
			label,
			null,
			null
		};
		object obj2;
		if (cpuScore.HasValue)
		{
			double valueOrDefault5 = cpuScore.GetValueOrDefault();
			obj2 = $" · CPU {valueOrDefault5:N0}";
		}
		else
		{
			obj2 = "";
		}
		array[3] = (string)obj2;
		object obj3;
		if (gpuScore.HasValue)
		{
			double valueOrDefault6 = gpuScore.GetValueOrDefault();
			obj3 = $" · GPU {valueOrDefault6:N0}";
		}
		else
		{
			obj3 = "";
		}
		array[4] = (string)obj3;
		string summary = string.Concat(array);
		_settings.BenchRecords.Insert(0, new BenchRecord(DateTime.Now, "oc", flag5 ? 1.0 : 0.15, summary));
		_settings.Save();
		UpdateOcHistory();
		UpdateRecentErrors();
	}

	private void BuildOcGuide()
	{
		(string Maker, string Model) tuple = OcCheck.Motherboard();
		string item = tuple.Maker;
		string item2 = tuple.Model;
		bool flag = item.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase);
		bool flag2 = item.Contains("ASUS", StringComparison.OrdinalIgnoreCase);
		bool flag3 = item.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) || item.Contains("MSI", StringComparison.OrdinalIgnoreCase);
		string text = GameCatalog.ShortCpuName(_cpuName);
		bool flag4 = _cpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase);
		string text2 = ((flag ? "Gigabyte" : (item.Split(' ').FirstOrDefault() ?? "")) + " " + item2).Trim();
		OcGuideTitle.Text = "GUIA: OVERCLOCK NA BIOS · " + text2.ToUpperInvariant() + " · " + text.ToUpperInvariant();
		OcGuideIntro.Text = "Faça uma mudança por vez e rode o verificador depois de cada uma. Os menus podem mudar um pouco com a versão da BIOS. Valores são pontos de partida seguros, não garantia: cada chip é diferente.";
		string text3;
		if (flag)
		{
			text3 = "Tweaker ? Advanced CPU Settings ? Precision Boost Overdrive  (ou Settings ? AMD Overclocking)";
		}
		else if (flag2)
		{
			text3 = "Advanced ? AMD Overclocking ? Precision Boost Overdrive  (ou AI Tweaker ? PBO)";
		}
		else
		{
			text3 = (flag3 ? "OC ? Advanced CPU Configuration ? AMD Overclocking ? Precision Boost Overdrive" : "Advanced ? AMD Overclocking ? Precision Boost Overdrive");
		}
		string where;
		if (flag)
		{
			where = "Tweaker ? Extreme Memory Profile (X.M.P.) ? EXPO1";
		}
		else if (flag2)
		{
			where = "AI Tweaker ? Ai Overclock Tuner ? EXPO I";
		}
		else
		{
			where = (flag3 ? "OC ? A-XMP/EXPO ? Profile 1" : "procure EXPO / XMP no menu de memória");
		}
		List<GuideStep> list = new List<GuideStep>
		{
			new GuideStep("0", "Antes de começar", flag ? "Save & Exit ? Save Profiles" : "Save & Exit ? Save Profile", "Salve o perfil atual da BIOS. Se o PC não ligar depois de um ajuste, espere 3 tentativas (muitas placas voltam sozinhas) ou limpe a CMOS (jumper CLR_CMOS na placa ou tirando a bateria por 1 minuto)."),
			new GuideStep("1", "Ative o perfil da memória (EXPO)", where, "É o maior ganho em jogos para a maioria dos PCs: a RAM sai do padrão lento e vai para a velocidade da etiqueta. Teste: verificador \"CPU + RAM\" por 15 min (o teste de RAM acusa erro se o perfil não for estável).")
		};
		if (flag4)
		{
			list.Add(new GuideStep("2", "Precision Boost Overdrive (PBO)", text3, "Precision Boost Overdrive ? Advanced. PBO Limits ? Motherboard. Isso libera os limites de energia para o boost durar mais. Teste com o verificador."));
			list.Add(new GuideStep("3", "Curve Optimizer (undervolt da CPU)", text3 + " ? Curve Optimizer", "All Cores ? Negative ? 10. Teste. Se passar, tente 15, 20, 25… até falhar; volte 5 a partir de onde falhou. No " + text + " costuma ficar entre -15 e -30. Instabilidade de Curve Optimizer aparece muito em uso leve: use o PC normalmente por um dia e veja \"Erros de hardware recentes\" aqui — erro WHEA = volte um passo."));
			list.Add(new GuideStep("4", "Max CPU Boost Clock Override (opcional)", text3, "Depois do Curve Optimizer estável: +100 MHz, teste; depois +200 MHz. Ganho pequeno; pode exigir reduzir o Curve Optimizer."));
			list.Add(new GuideStep("5", "Alternativa fria: Eco Mode", text3 + " ? Eco Mode", "Em vez de mais desempenho: limita a CPU a 65 W (ou 105 W). Perde ~5–10% em tarefas pesadas multi-core, quase nada em jogos, e roda bem mais frio e silencioso."));
		}
		else
		{
			list.Add(new GuideStep("2", "Processador Intel", "Tweaker / Ai Tweaker / OC ? CPU", "Para Intel, prefira undervolt (offset negativo de tensão) e limites de energia (PL1/PL2) em vez de aumentar multiplicadores. Teste cada passo com o verificador."));
		}
		OcGuide.ItemsSource = list;
	}

	private void BuildOverlayPicker()
	{
		OvItemsPanel.Children.Clear();
		foreach (IGrouping<string, OverlayMetric> group in from overlayMetric in OverlayMetrics.All
			group overlayMetric by overlayMetric.Group)
		{
			OvItemsPanel.Children.Add(new TextBlock
			{
				Text = group.Key.ToUpperInvariant(),
				Style = (Style)FindResource("Label"),
				Margin = new Thickness(0.0, 10.0, 0.0, 4.0)
			});
			WrapPanel wrap = new WrapPanel();
			foreach (OverlayMetric m in group)
			{
				System.Windows.Controls.CheckBox cb = new System.Windows.Controls.CheckBox
				{
					Content = m.Label,
					Tag = m.Key,
					IsChecked = OvShow.Contains(m.Key),
					Margin = new Thickness(0.0, 0.0, 18.0, 6.0)
				};
				cb.Click += (object _, RoutedEventArgs _) =>
				{
					if (cb.IsChecked == true)
					{
						OvShow.Add(m.Key);
					}
					else
					{
						OvShow.Remove(m.Key);
					}
					SaveOverlayItems();
				};
				wrap.Children.Add(cb);
			}
			OvItemsPanel.Children.Add(wrap);
		}
	}

	private void SaveOverlayItems()
	{
		_settings.OverlayItems = OverlayMetrics.All.Select((OverlayMetric m) => m.Key).Where(OvShow.Contains).ToList();
		_settings.Save();
		UpdateOverlay();
	}

	private void OvPreset_Click(object sender, RoutedEventArgs e)
	{
		string text = (string)((FrameworkElement)sender).Tag;
		HashSet<string> hashSet;
		if (text == "all")
		{
			hashSet = OverlayMetrics.All.Select((OverlayMetric m) => m.Key).ToHashSet();
		}
		else
		{
			hashSet = ((!(text == "min")) ? OverlayMetrics.Defaults() : new HashSet<string> { "fps", "frametime", "low1" });
		}
		HashSet<string> keys = hashSet;
		_ovShow = keys;
		SaveOverlayItems();
		BuildOverlayPicker();
	}

	private SensorRow? GpuRow(SensorType type, string name, bool startsWith = false)
	{
		if (_gpus.Count != 0)
		{
			return _monitor.Rows.FirstOrDefault((SensorRow r) => r.Hardware == _gpus[0].Name && r.Type == type && (startsWith ? r.Name.StartsWith(name) : (r.Name == name)) && r.RawValue.HasValue);
		}
		return null;
	}

	private uint? MainGpuClock(int nvmlType, string lhmName)
	{
		if (_gpus.Count > 0 && _gpus[0].NvmlHandle != IntPtr.Zero)
		{
			uint? num = Nvml.Clock(_gpus[0].NvmlHandle, nvmlType);
			if (num.HasValue)
			{
				return num.GetValueOrDefault();
			}
		}
		float? num2 = GpuRow(SensorType.Clock, lhmName)?.RawValue;
		if (num2.HasValue)
		{
			float v = num2.GetValueOrDefault();
			if (v > 0f)
			{
				return (uint)v;
			}
		}
		return null;
	}

	private (double Used, double Total)? MainGpuVram()
	{
		if (_gpus.Count == 0)
		{
			return null;
		}
		if (_gpus[0].NvmlHandle != IntPtr.Zero)
		{
			Nvml.Memory? memory = Nvml.MemoryInfo(_gpus[0].NvmlHandle);
			if (memory.HasValue)
			{
				Nvml.Memory m = memory.GetValueOrDefault();
				if (m.Total != 0)
				{
					return ((double)m.Used / 1073741824.0, (double)m.Total / 1073741824.0);
				}
			}
		}
		float? num = GpuRow(SensorType.SmallData, "GPU Memory Used")?.RawValue;
		if (num.HasValue)
		{
			float u = num.GetValueOrDefault();
			num = GpuRow(SensorType.SmallData, "GPU Memory Total")?.RawValue;
			if (num.HasValue)
			{
				float t = num.GetValueOrDefault();
				if (t > 0f)
				{
					return ((double)u / 1024.0, (double)t / 1024.0);
				}
			}
		}
		return null;
	}

	private (float? Avg, float? Max) CpuClocks()
	{
		List<float> cores = (from r in _monitor.Rows
			where r.HardwareType == HardwareType.Cpu && r.Type == SensorType.Clock && r.Name.StartsWith("Core #") && !r.Name.Contains("Effective") && r.RawValue > 0f
			select r.RawValue.Value).ToList();
		if (cores.Count <= 0)
		{
			return (Avg: null, Max: null);
		}
		return (Avg: cores.Average(), Max: cores.Max());
	}

	private List<OverlayLine> BuildOverlayLines(FrameStats? f)
	{
		HashSet<string> ovShow = OvShow;
		LoadSample s = BottleneckAnalyzer.Sample(_monitor, _gpus.FirstOrDefault(), f);
		List<OverlayLine> lines = new List<OverlayLine>();
		List<string> gpu = new List<string>();
		if (ovShow.Contains("gpu_load"))
		{
			gpu.Add(P(s.GpuLoad));
		}
		if (ovShow.Contains("gpu_temp"))
		{
			gpu.Add(T(s.GpuTemp));
		}
		if (ovShow.Contains("gpu_hotspot"))
		{
			gpu.Add("HS " + T(GpuRow(SensorType.Temperature, "GPU Hot Spot")?.RawValue));
		}
		if (ovShow.Contains("gpu_clock"))
		{
			uint? num = MainGpuClock(0, "GPU Core");
			string item;
			if (num.HasValue)
			{
				uint c = num.GetValueOrDefault();
				item = $"{c} MHz";
			}
			else
			{
				item = "— MHz";
			}
			gpu.Add(item);
		}
		if (ovShow.Contains("gpu_memclock"))
		{
			uint? num = MainGpuClock(2, "GPU Memory");
			string item2;
			if (num.HasValue)
			{
				uint mc = num.GetValueOrDefault();
				item2 = $"mem {mc} MHz";
			}
			else
			{
				item2 = "mem —";
			}
			gpu.Add(item2);
		}
		if (ovShow.Contains("gpu_power"))
		{
			float? num2 = MainGpuPower();
			string item3;
			if (num2.HasValue)
			{
				float w = num2.GetValueOrDefault();
				item3 = $"{w:0} W";
			}
			else
			{
				item3 = "— W";
			}
			gpu.Add(item3);
		}
		if (ovShow.Contains("gpu_volt"))
		{
			float? num2 = GpuRow(SensorType.Voltage, "GPU Core")?.RawValue;
			string item4;
			if (num2.HasValue)
			{
				float v = num2.GetValueOrDefault();
				item4 = $"{v:0.000} V";
			}
			else
			{
				item4 = "— V";
			}
			gpu.Add(item4);
		}
		string item5;
		if (ovShow.Contains("gpu_fan"))
		{
			float? num2 = GpuRow(SensorType.Fan, "GPU Fan", startsWith: true)?.RawValue;
			if (num2.HasValue)
			{
				float rpm = num2.GetValueOrDefault();
				if (rpm > 0f)
				{
					item5 = $"{rpm:0} RPM";
					goto IL_0370;
				}
			}
			num2 = GpuRow(SensorType.Control, "GPU Fan", startsWith: true)?.RawValue;
			if (num2.HasValue)
			{
				float pct = num2.GetValueOrDefault();
				item5 = $"fan {pct:0}%";
			}
			else
			{
				item5 = "fan —";
			}
			goto IL_0370;
		}
		goto IL_0375;
		IL_0375:
		if (ovShow.Contains("gpu_perfcap"))
		{
			string cap = MainGpuPerfCap();
			if (cap != null && cap.Length > 0)
			{
				gpu.Add(cap);
			}
		}
		Line("GPU", gpu);
		List<string> vram = new List<string>();
		(double, double)? vr = MainGpuVram();
		if (ovShow.Contains("vram_used"))
		{
			string item6;
			if (vr.HasValue)
			{
				(double, double) a = vr.GetValueOrDefault();
				item6 = $"{a.Item1:0.0} GB";
			}
			else
			{
				item6 = "—";
			}
			vram.Add(item6);
		}
		if (ovShow.Contains("vram_pct"))
		{
			vram.Add(P(s.VramPct));
		}
		if (ovShow.Contains("vram_temp"))
		{
			vram.Add(T(GpuRow(SensorType.Temperature, "GPU Memory")?.RawValue));
		}
		Line("VRAM", vram);
		List<string> cpu = new List<string>();
		var (avg, max) = CpuClocks();
		if (ovShow.Contains("cpu_load"))
		{
			cpu.Add(P(s.CpuTotal));
		}
		if (ovShow.Contains("cpu_maxcore"))
		{
			float? num2 = s.CpuMaxCore;
			string item7;
			if (num2.HasValue)
			{
				float mx = num2.GetValueOrDefault();
				item7 = $"núcleo {mx:0}%";
			}
			else
			{
				item7 = "núcleo —";
			}
			cpu.Add(item7);
		}
		if (ovShow.Contains("cpu_temp"))
		{
			cpu.Add(T(s.CpuTemp));
		}
		if (ovShow.Contains("cpu_clock"))
		{
			string item8;
			if (avg.HasValue)
			{
				float ca = avg.GetValueOrDefault();
				item8 = $"{ca / 1000f:0.00} GHz";
			}
			else
			{
				item8 = "— GHz";
			}
			cpu.Add(item8);
		}
		if (ovShow.Contains("cpu_clockmax"))
		{
			string item9;
			if (max.HasValue)
			{
				float cm = max.GetValueOrDefault();
				item9 = $"pico {cm / 1000f:0.00} GHz";
			}
			else
			{
				item9 = "pico —";
			}
			cpu.Add(item9);
		}
		string item10;
		if (ovShow.Contains("cpu_power"))
		{
			float? num2 = _monitor.CpuPower;
			if (num2.HasValue)
			{
				float cw = num2.GetValueOrDefault();
				if (cw > 0f)
				{
					item10 = $"{cw:0} W";
					goto IL_069c;
				}
			}
			item10 = "— W";
			goto IL_069c;
		}
		goto IL_06a1;
		IL_069c:
		cpu.Add(item10);
		goto IL_06a1;
		IL_0370:
		gpu.Add(item5);
		goto IL_0375;
		IL_06a1:
		Line("CPU", cpu);
		List<string> ram = new List<string>();
		var (total, availB) = Optimizer.MemoryStatus();
		if (ovShow.Contains("ram_used"))
		{
			ram.Add((total > 0) ? $"{(double)(total - availB) / 1073741824.0:0.0} GB" : "—");
		}
		if (ovShow.Contains("ram_pct"))
		{
			ram.Add(P(s.RamPct));
		}
		Line("RAM", ram);
		return lines;
		void Line(string label, List<string> parts)
		{
			if (parts.Count > 0)
			{
				lines.Add(new OverlayLine(label, string.Join("  ", parts)));
			}
		}
		static string P(double? num3)
		{
			if (num3.HasValue)
			{
				double x = num3.GetValueOrDefault();
				return $"{x:0}%";
			}
			return "—";
		}
		static string T(float? num3)
		{
			if (num3.HasValue)
			{
				float x = num3.GetValueOrDefault();
				return $"{x:0} °C";
			}
			return "—";
		}
	}

	private static string PresetName(string p)
	{
		if (!(p == "normal"))
		{
			if (p == "extrema")
			{
				return "Extrema";
			}
			return "Alta";
		}
		return "Normal";
	}

	private List<MeterItem> RtHistoryItems()
	{
		List<BenchRecord> list = (from r in _settings.BenchRecords
			where r.Test.StartsWith("rt:")
			orderby r.Date descending
			select r).ToList();
		Dictionary<string, double> best = (from r in list
			group r by r.Test).ToDictionary((IGrouping<string, BenchRecord> g) => g.Key, (IGrouping<string, BenchRecord> g) => g.Max((BenchRecord r) => r.Score));
		return list.Select((BenchRecord r) => new MeterItem($"{r.Date:dd/MM HH:mm}", r.Summary, (best[r.Test] > 0.0) ? (r.Score / best[r.Test]) : 0.0)).ToList();
	}

	private async void BtnRtBench_Click(object sender, RoutedEventArgs e)
	{
		if (_gameBenchRunning)
		{
			_rtBench?.Cancel();
			return;
		}
		if (AnyBenchRunning || TestRunning)
		{
			RtStatus.Text = "Termine o outro teste antes.";
			return;
		}
		string adapterName = ((_gpus.Count > 0) ? _gpus[0].Name : "");
		RtStatus.Text = "Verificando suporte a ray tracing…";
		string why = await Task.Run(() => GpuRtBench.CheckSupport(adapterName));
		if (why != null)
		{
			RtStatus.Text = why;
			return;
		}
		int[] size = (CheckedTag(RtResPanel) ?? "2560x1440").Split('x').Select(int.Parse).ToArray();
		string preset = CheckedTag(RtPresetPanel) ?? "alta";
		bool quick = CheckedTag(RtModePanel) == "quick";
		_rtBench = new GpuRtBench
		{
			Width = size[0],
			Height = size[1],
			Preset = preset,
			AdapterName = adapterName,
			Passes = 3,
			Warmup = TimeSpan.FromSeconds(quick ? 5 : 20),
			PassDuration = TimeSpan.FromSeconds(quick ? 10 : 30)
		};
		GpuRtBench bench = _rtBench;
		SetGameBenchRunning(BtnRtBench, running: true);
		RtResult.ItemsSource = null;
		RtVerdict.Text = "";
		DispatcherTimer timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250.0), DispatcherPriority.Normal, (object? _, EventArgs _) =>
		{
			RtFpsNow.Text = ((bench.CurrentFps > 0.0) ? $"{bench.CurrentFps:0}" : "—");
			RtPhase.Text = ((bench.Phase.Length > 0) ? bench.Phase : "—");
			RtProgress.ScaleX = bench.Progress;
			RtStatus.Text = $"Rodando · {size[0]}x{size[1]} · {PresetName(preset)} · {bench.Progress:P0}";
		}, Dispatcher);
		timer.Start();
		_gpuBenchActive = true;
		bool overlayWasOn = ChkOverlay.IsChecked == true;
		if (overlayWasOn)
		{
			ChkOverlay.IsChecked = false;
		}
		GpuRtResult r = await Task.Factory.StartNew((Func<GpuRtResult>)bench.Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
		timer.Stop();
		_gpuBenchActive = false;
		if (overlayWasOn)
		{
			ChkOverlay.IsChecked = true;
		}
		SetGameBenchRunning(BtnRtBench, running: false);
		RtProgress.ScaleX = 0.0;
		RtFpsNow.Text = "—";
		RtPhase.Text = "—";
		if (r == null)
		{
			RtStatus.Text = (bench.Cancelled ? "Cancelado" : ("Falhou: " + bench.Error));
			return;
		}
		string driver = await Task.Run(() => DriverVersionOf(r.Adapter));
		RtStatus.Text = "Concluído · " + r.Adapter + ((driver.Length > 0) ? (" · driver " + driver) : "");
		RtVerdict.Text = (r.Reliable ? $"✔ Resultado confiável: as 3 passadas variaram só {r.VariationPct:0.0}%." : ($"⚠ Variação de {r.VariationPct:0.0}% entre as passadas (acima de 3%). Algo atrapalhou: programa em segundo plano, " + "temperatura ou limite de energia. Feche outros programas e rode de novo antes de comparar."));
		List<KeyValuePair<string, string>> rows = new List<KeyValuePair<string, string>>
		{
			new KeyValuePair<string, string>("Nota", $"{r.Score:N0} pts"),
			new KeyValuePair<string, string>("FPS (mediana)", $"{r.MedianFps:0.0}"),
			new KeyValuePair<string, string>("1% low (mediana)", $"{r.MedianLow1:0.0}"),
			new KeyValuePair<string, string>("Variação entre passadas", $"{r.VariationPct:0.0}%"),
			new KeyValuePair<string, string>("Raios por segundo (estimado)", $"{r.GigaRaysPerSec:0.0} bilhões"),
			new KeyValuePair<string, string>("Preset · resolução", $"{PresetName(r.Preset)} · {r.Width}x{r.Height}")
		};
		for (int i = 0; i < r.Passes.Count; i++)
		{
			rows.Add(new KeyValuePair<string, string>($"Passada {i + 1}", $"{r.Passes[i].AvgFps:0.0} FPS · 1% low {r.Passes[i].Low1Fps:0.0}"));
		}
		RtResult.ItemsSource = rows;
		SaveBenchRecord($"rt:{r.Preset}:{r.Height}", r.Score, $"{r.Score:N0} pts · {r.MedianFps:0} FPS · {PresetName(r.Preset)} {r.Height}p" + (r.Reliable ? "" : " · instável"));
	}

	private static string DriverVersionOf(string adapter)
	{
		try
		{
			using ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController");
			foreach (ManagementObject mo in s.Get())
			{
				if ((mo["Name"] is string n && adapter.Contains(n.Trim(), StringComparison.OrdinalIgnoreCase)) || (mo["Name"] is string n2 && n2.Contains(adapter, StringComparison.OrdinalIgnoreCase)))
				{
					return mo["DriverVersion"]?.ToString() ?? "";
				}
			}
		}
		catch
		{
		}
		return "";
	}

	private void InitSettings()
	{
		bool autoStart = StartupManager.IsAppAutoStartEnabled();
		if (ChkSettingsAutoStart != null)
		{
			ChkSettingsAutoStart.IsChecked = autoStart;
		}
		if (ChkAppAutoStart != null)
		{
			ChkAppAutoStart.IsChecked = autoStart;
		}
		if (ChkSettingsLightTheme != null)
		{
			ChkSettingsLightTheme.IsChecked = _settings.LightTheme;
		}
		if (ChkSettingsSidebarCollapsed != null)
		{
			ChkSettingsSidebarCollapsed.IsChecked = _sidebarCollapsed;
		}
		if (TxtSettingsExePath != null)
		{
			TxtSettingsExePath.Text = Environment.ProcessPath ?? AppContext.BaseDirectory;
		}
	}

	private void AppAutoStart_Click(object sender, RoutedEventArgs e)
	{
		if (!(sender is System.Windows.Controls.CheckBox { IsChecked: var isChecked }))
		{
			return;
		}
		bool turnOn = isChecked == true;
		try
		{
			StartupManager.SetAppAutoStart(turnOn);
			string msg = (turnOn ? "Triarch configurado para iniciar em segundo plano na bandeja (Agendador de Tarefas + Registro)." : "Início automático do Triarch com o Windows desativado.");
			if (StartupResult != null)
			{
				StartupResult.Text = msg;
			}
			if (SettingsAutoStartStatus != null)
			{
				SettingsAutoStartStatus.Text = msg;
			}
		}
		catch (Exception ex)
		{
			string err = "Não foi possível alterar o registro de inicialização: " + ex.Message;
			if (StartupResult != null)
			{
				StartupResult.Text = err;
			}
			if (SettingsAutoStartStatus != null)
			{
				SettingsAutoStartStatus.Text = err;
			}
		}
		bool actual = StartupManager.IsAppAutoStartEnabled();
		if (ChkAppAutoStart != null)
		{
			ChkAppAutoStart.IsChecked = actual;
		}
		if (ChkSettingsAutoStart != null)
		{
			ChkSettingsAutoStart.IsChecked = actual;
		}
		if (PageStartup != null && PageStartup.IsVisible)
		{
			ScanStartup();
		}
	}

	private void SettingsLightTheme_Click(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.CheckBox { IsChecked: var isChecked })
		{
			bool light = isChecked == true;
			if (ThemeManager.IsLight != light)
			{
				Theme_Click(sender, e);
			}
		}
	}

	private void SettingsSidebarCollapsed_Click(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.CheckBox { IsChecked: var isChecked })
		{
			bool collapsed = isChecked == true;
			if (_sidebarCollapsed != collapsed)
			{
				ToggleSidebar_Click(sender, e);
			}
		}
	}

	private void ScanStartup()
	{
		bool autoStart = StartupManager.IsAppAutoStartEnabled();
		if (ChkAppAutoStart != null)
		{
			ChkAppAutoStart.IsChecked = autoStart;
		}
		if (ChkSettingsAutoStart != null)
		{
			ChkSettingsAutoStart.IsChecked = autoStart;
		}
		List<StartupEntry> list;
		try
		{
			list = StartupManager.List();
		}
		catch (Exception ex)
		{
			StartupResult.Text = "Não foi possível listar: " + ex.Message;
			return;
		}
		StartupList.ItemsSource = list;
		int on = list.Count((StartupEntry e) => e.Enabled);
		StartupSummary.Text = ((list.Count == 0) ? "NENHUM PROGRAMA NA INICIALIZAÇÃO" : $"{list.Count} PROGRAMAS · {on} ATIVADOS · {list.Count - on} DESATIVADOS");
		BtnStartupRestore.IsEnabled = _settings.StartupRemoved.Count > 0;
		BtnStartupRestore.Content = ((_settings.StartupRemoved.Count > 0) ? $"RESTAURAR REMOVIDOS ({_settings.StartupRemoved.Count})" : "RESTAURAR REMOVIDOS");
	}

	private void BtnStartupScan_Click(object sender, RoutedEventArgs e)
	{
		StartupResult.Text = "";
		ScanStartup();
	}

	private void StartupToggle_Click(object sender, RoutedEventArgs e)
	{
		if (!(sender is System.Windows.Controls.CheckBox { Tag: StartupEntry item, IsChecked: var isChecked } box))
		{
			return;
		}
		bool turnOn = isChecked == true;
		if (item.Machine && !RequireAdmin())
		{
			box.IsChecked = item.Enabled;
			return;
		}
		try
		{
			StartupManager.SetEnabled(item, turnOn);
			StartupResult.Text = item.Name + ": " + (turnOn ? "ativado" : "desativado") + ". Vale na próxima inicialização do Windows.";
		}
		catch (Exception ex)
		{
			StartupResult.Text = item.Name + ": falhou (" + ex.Message + ").";
		}
		ScanStartup();
	}

	private void StartupRemove_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { Tag: StartupEntry item } && (!item.Machine || RequireAdmin()) && System.Windows.MessageBox.Show($"Remover \"{item.Name}\" da inicialização do Windows?\n\n{item.Command}\n\n" + "O programa continua instalado, só deixa de abrir sozinho. Dá para desfazer em \"Restaurar removidos\".\n\nSe só quiser testar, prefira desligar o interruptor.", "Inicialização", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
		{
			try
			{
				_settings.StartupRemoved.Add(StartupManager.Remove(item));
				_settings.Save();
				StartupResult.Text = item.Name + " removido da inicialização.";
			}
			catch (Exception ex)
			{
				StartupResult.Text = item.Name + ": não foi possível remover (" + ex.Message + ").";
			}
			ScanStartup();
		}
	}

	private void BtnStartupRestore_Click(object sender, RoutedEventArgs e)
	{
		List<StartupBackup> removed = _settings.StartupRemoved;
		if (removed.Count == 0 || (removed.Any((StartupBackup startupBackup) => startupBackup.Machine) && !RequireAdmin()))
		{
			return;
		}
		string names = string.Join("\n", from startupBackup in removed.Take(12)
			select "  • " + startupBackup.Name) + ((removed.Count > 12) ? $"\n  … e mais {removed.Count - 12}" : "");
		if (System.Windows.MessageBox.Show($"Devolver {removed.Count} programas à inicialização do Windows?\n\n{names}", "Inicialização", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
		{
			return;
		}
		List<string> failed = new List<string>();
		foreach (StartupBackup b in removed.ToList())
		{
			try
			{
				StartupManager.Restore(b);
				removed.Remove(b);
			}
			catch (Exception ex)
			{
				failed.Add(b.Name + " (" + ex.Message + ")");
			}
		}
		_settings.Save();
		StartupResult.Text = ((failed.Count == 0) ? "Programas restaurados." : ("Não restaurados: " + string.Join("; ", failed)));
		ScanStartup();
	}

	private bool _hardwareLoaded;

	public MainWindow(bool startInTray = false, EventWaitHandle? activateEvent = null)
	{
		InitializeComponent();
		SetupTrayIcon(startInTray);
		if (startInTray)
		{
			ShowInTaskbar = false;
			Visibility = Visibility.Hidden;
			try
			{
				new WindowInteropHelper(this).EnsureHandle();
			}
			catch
			{
			}
		}
		if (activateEvent != null)
		{
			ThreadPool.RegisterWaitForSingleObject(activateEvent, delegate
			{
				base.Dispatcher.BeginInvoke(new Action(RestoreFromTray));
			}, null, -1, executeOnlyOnce: false);
		}
		ThemeManager.Apply(_settings.LightTheme);
		UpdateThemeButton();
		ApplySidebarState(_settings.SidebarCollapsed);
		ShowPage("PageDash");
		_monitor = new SensorMonitor();
		_view = CollectionViewSource.GetDefaultView(_monitor.Rows);
		_view.GroupDescriptions.Add(new PropertyGroupDescription("Hardware"));
		SensorGrid.ItemsSource = _view;
		SystemSub.Text = "Lendo informações do sistema…";
		StatusText.Text = "Detectando o hardware…";
		InitBench();
		RefreshDrives();
		Title = "Triarch";
		TitlePage.Text = "Visão geral";
		UpdateDashboard();
		Loaded += async (object _, RoutedEventArgs _) =>
		{
			await LoadHardwareAsync();
		};
		if (startInTray)
		{
			_ = LoadHardwareAsync();
		}
		_timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1.0)
		};
		_timer.Tick += (object? _, EventArgs _) =>
		{
			if (!_gpuBenchActive)
			{
				_monitor.Refresh();
				if (_monitor.IsOpen)
				{
					StatusText.Text = $"{_monitor.Rows.Count} sensores · atualizado {DateTime.Now:HH:mm:ss}";
				}
				UpdateStressPanel();
				UpdateGpuLive();
				UpdateCpuSensorWarning();
				CollectDashSample();
				UpdateBottleneck();
				UpdateSensorWindowLive();
				UpdateBottleneck();
				if (PageBench.IsVisible)
				{
					UpdateBenchLive();
				}
				if (PageOptimize.IsVisible)
				{
					UpdateRamStatus();
				}
			}
		};
		_timer.Start();
		StateChanged += (object? _, EventArgs _) =>
		{
			bool flag = WindowState == WindowState.Maximized;
			Root.Margin = (flag ? new Thickness(7.0) : new Thickness(0.0));
			Root.CornerRadius = (flag ? new CornerRadius(0.0) : ((CornerRadius)FindResource("RadiusWindow")));
			Root.BorderThickness = (flag ? new Thickness(0.0) : new Thickness(1.0));
			BtnMax.Content = (flag ? "\ue923" : "\ue922");
			UpdateRootClip();
		};
		Closed += (object? _, EventArgs _) =>
		{
			_trayIcon?.Dispose();
			_timer.Stop();
			_overlayTimer?.Stop();
			_stress.Stop();
			_burn.Stop();
			_benchCts?.Cancel();
			_overlay?.Close();
			_toast?.Close();
			_frames.Dispose();
			UnregisterHotKey(new WindowInteropHelper(this).Handle, 21061);
			_sensorWindow?.Close();
			_launchCts?.Cancel();
			_monitor.Dispose();
		};
	}

	private static System.Drawing.Icon LoadTrayAppIcon()
	{
		try
		{
			string? procPath = Environment.ProcessPath;
			if (!string.IsNullOrEmpty(procPath) && File.Exists(procPath))
			{
				System.Drawing.Icon? extracted = System.Drawing.Icon.ExtractAssociatedIcon(procPath);
				if (extracted != null)
				{
					return extracted;
				}
			}
		}
		catch
		{
		}
		try
		{
			StreamResourceInfo? sri = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/assets/app_icon.ico"));
			if (sri?.Stream != null)
			{
				return new System.Drawing.Icon(sri.Stream);
			}
		}
		catch
		{
		}
		return SystemIcons.Application;
	}

	private void SetupTrayIcon(bool startedInTray = false)
	{
		_trayIcon = new NotifyIcon
		{
			Icon = LoadTrayAppIcon(),
			Text = "Triarch · Monitor de Hardware e Infra",
			Visible = true
		};
		_trayIcon.MouseClick += (object? _, System.Windows.Forms.MouseEventArgs e) =>
		{
			if (e.Button == MouseButtons.Left)
			{
				RestoreFromTray();
			}
		};
		_trayIcon.DoubleClick += (object? _, EventArgs _) =>
		{
			RestoreFromTray();
		};
		ContextMenuStrip menu = new ContextMenuStrip();
		menu.Items.Add("Abrir Triarch", null, (object? _, EventArgs _) =>
		{
			RestoreFromTray();
		});
		menu.Items.Add("Monitor 2ª Tela (Sensores)", null, (object? _, EventArgs _) =>
		{
			BtnOpenSensorMonitor_Click(this, new RoutedEventArgs());
		});
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add("Sair do Triarch", null, (object? _, EventArgs _) =>
		{
			ShutdownApp();
		});
		_trayIcon.ContextMenuStrip = menu;
		if (startedInTray)
		{
			try
			{
				_trayIcon.ShowBalloonTip(3000, "Triarch ativo em 2º plano", "O Triarch iniciou com o Windows e está monitorando na bandeja do sistema.", ToolTipIcon.Info);
			}
			catch
			{
			}
		}
	}

	private void RestoreFromTray()
	{
		ShowInTaskbar = true;
		Visibility = Visibility.Visible;
		Show();
		if (WindowState == WindowState.Minimized)
		{
			WindowState = WindowState.Normal;
		}
		Activate();
		Topmost = true;
		Topmost = false;
		Focus();
	}

	private void ShutdownApp()
	{
		_isRealExit = true;
		if (_trayIcon != null)
		{
			_trayIcon.Visible = false;
			_trayIcon.Dispose();
		}
		System.Windows.Application.Current.Shutdown();
	}

	protected override void OnClosing(CancelEventArgs e)
	{
		if (!_isRealExit)
		{
			e.Cancel = true;
			ShowInTaskbar = false;
			Hide();
		}
		else
		{
			base.OnClosing(e);
		}
	}

	private async Task LoadHardwareAsync()
	{
		if (_hardwareLoaded)
		{
			return;
		}
		_hardwareLoaded = true;
		await Task.WhenAll(LoadInfo(), LoadSensors(), LoadGpu());
		async Task LoadGpu()
		{
			try
			{
				_gpus = await Task.Run((Func<List<GpuInfo>>)GpuInfoCollector.Collect);
			}
			catch
			{
				_gpus = new List<GpuInfo>();
			}
			for (int i = 0; i < _gpus.Count; i++)
			{
				if (_gpus.Count <= 1)
				{
					break;
				}
				GpuInfo g = _gpus[i];
				System.Windows.Controls.RadioButton chip = new System.Windows.Controls.RadioButton
				{
					Content = g.Name,
					GroupName = "Gpu",
					Style = (Style)FindResource("Chip"),
					IsChecked = (i == 0)
				};
				chip.Checked += (object _, RoutedEventArgs _) =>
				{
					ShowGpu(g);
				};
				GpuChips.Children.Add(chip);
			}
			if (_gpus.Count > 0)
			{
				ShowGpu(_gpus[0]);
			}
			InitGames();
			InitBottleneckAndOverlay();
			InitOc();
			UpdateDashboard();
		}
		async Task LoadInfo()
		{
			try
			{
				List<InfoNode> info = await Task.Run((Func<List<InfoNode>>)SystemInfoCollector.Collect);
				InfoCategories.ItemsSource = info;
				InfoCategories.SelectedIndex = 0;
				SystemSub.Text = $"{Environment.MachineName} · {info.Count} categorias";
			}
			catch (Exception ex)
			{
				SystemSub.Text = "Não foi possível ler as informações do sistema: " + ex.Message;
			}
		}
		async Task LoadSensors()
		{
			try
			{
				await _monitor.OpenAsync();
			}
			catch (Exception ex)
			{
				StatusText.Text = "Sensores indisponíveis: " + ex.Message;
				return;
			}
			_monitor.Refresh();
			UpdateCpuSensorWarning();
			BuildHardwareFilterChips();
			ApplySensorFilter();
			AddChip("Todos", null, isChecked: true);
			foreach (string c in from x in _monitor.Rows.Select((SensorRow r) => r.Category).Distinct()
				orderby x
				select x)
			{
				AddChip(c, c);
			}
			UpdateDashboard();
		}
	}

	private void UpdateCpuSensorWarning()
	{
		if (_monitor.IsOpen)
		{
			SensorMonitor.CpuSensorProblem p = _monitor.DiagnoseCpuSensors();
			CpuSensorWarn.Visibility = ((p == SensorMonitor.CpuSensorProblem.None) ? Visibility.Collapsed : Visibility.Visible);
			TextBlock cpuSensorWarnText = CpuSensorWarnText;
			cpuSensorWarnText.Text = p switch
			{
				SensorMonitor.CpuSensorProblem.NotElevated => "Sem permissão de administrador.", 
				SensorMonitor.CpuSensorProblem.PawnIOMissing => "Falta o driver PawnIO.", 
				SensorMonitor.CpuSensorProblem.ReadFailed => "O driver não respondeu.", 
				_ => "", 
			};
		}
	}

	private void BtnFixCpuSensors_Click(object sender, RoutedEventArgs e)
	{
		SensorMonitor.CpuSensorProblem cpuSensorProblem = _monitor.DiagnoseCpuSensors();
		string why = SensorMonitor.Explain(cpuSensorProblem);
		if (cpuSensorProblem != SensorMonitor.CpuSensorProblem.PawnIOMissing)
		{
			System.Windows.MessageBox.Show(why, "Triarch", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		else if (!File.Exists(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft\\WindowsApps\\winget.exe")))
		{
			System.Windows.MessageBox.Show(why + "\n\nO instalador de pacotes do Windows (winget) não foi encontrado neste PC. Atualize o \"Instalador de Aplicativo\" pela Microsoft Store e use o botão de novo, ou instale o PawnIO (autor: namazso) manualmente.", "Triarch", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		else if (System.Windows.MessageBox.Show(why + "\n\nInstalar o PawnIO agora? Vai abrir uma janela com o instalador do Windows (winget). Quando terminar, feche o Triarch e abra de novo.", "Triarch", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
		{
			Process.Start(new ProcessStartInfo("cmd.exe", "/k winget install --id namazso.PawnIO -e --accept-package-agreements --accept-source-agreements && echo. && echo Pronto! Feche esta janela e reabra o Triarch.")
			{
				UseShellExecute = true
			});
		}
	}

	private void Min_Click(object sender, RoutedEventArgs e)
	{
		WindowState = WindowState.Minimized;
	}

	private void Max_Click(object sender, RoutedEventArgs e)
	{
		WindowState = ((WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateRootClip();
	}

	private void UpdateRootClip()
	{
		if (!(Root.ActualWidth <= 0.0) && !(Root.ActualHeight <= 0.0))
		{
			if (WindowState == WindowState.Maximized)
			{
				Root.Clip = null;
				return;
			}
			double r = Root.CornerRadius.TopLeft;
			RectangleGeometry clip = new RectangleGeometry(new Rect(0.0, 0.0, Root.ActualWidth, Root.ActualHeight), r, r);
			clip.Freeze();
			Root.Clip = clip;
		}
	}

	private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
	{
		ApplySidebarState(!_sidebarCollapsed);
		_settings.SidebarCollapsed = _sidebarCollapsed;
		if (ChkSettingsSidebarCollapsed != null)
		{
			ChkSettingsSidebarCollapsed.IsChecked = _sidebarCollapsed;
		}
		try
		{
			_settings.Save();
		}
		catch
		{
		}
	}

	private void NavGroup_Click(object sender, RoutedEventArgs e)
	{
		UpdateSidebarGroups();
	}

	private void ApplySidebarState(bool collapsed)
	{
		_sidebarCollapsed = collapsed;
		SideCol.Width = new GridLength(collapsed ? 74 : 264);
		SideFooter.Margin = (collapsed ? new Thickness(8.0, 6.0, 8.0, 10.0) : new Thickness(14.0, 6.0, 14.0, 12.0));
		SideFooterExpanded.Visibility = (collapsed ? Visibility.Collapsed : Visibility.Visible);
		SideVersion.Visibility = (collapsed ? Visibility.Collapsed : Visibility.Visible);
		TopTelemetryPill.Visibility = ((!collapsed) ? Visibility.Collapsed : Visibility.Visible);
		BtnSidebarCorner.Content = (collapsed ? "\ue76c" : "\ue76b");
		BtnSidebarCorner.HorizontalAlignment = (collapsed ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Right);
		BtnSidebarCorner.ToolTip = (collapsed ? "Expandir menu lateral" : "Recolher menu no canto");
		BtnSidebarTop.ToolTip = (collapsed ? "Expandir menu lateral" : "Recolher menu lateral");
		StyleNavItem(NavDash, null);
		(ToggleButton, StackPanel, Border)[] sidebarGroups = SidebarGroups;
		for (int i = 0; i < sidebarGroups.Length; i++)
		{
			(ToggleButton, StackPanel, Border) tuple = sidebarGroups[i];
			ToggleButton grp = tuple.Item1;
			StackPanel item = tuple.Item2;
			string grpTitle = grp.Content?.ToString() ?? "";
			foreach (System.Windows.Controls.RadioButton item2 in item.Children.OfType<System.Windows.Controls.RadioButton>())
			{
				StyleNavItem(item2, grpTitle);
			}
		}
		UpdateSidebarGroups();
		void StyleNavItem(System.Windows.Controls.RadioButton rb, string? groupTitle)
		{
			if (rb.Content is StackPanel sp && sp.Children.Count >= 2 && sp.Children[0] is TextBlock icon && sp.Children[1] is TextBlock label)
			{
				label.Visibility = (collapsed ? Visibility.Collapsed : Visibility.Visible);
				icon.Width = (collapsed ? double.NaN : 28.0);
				icon.FontSize = (collapsed ? 15 : 14);
				rb.Margin = (collapsed ? new Thickness(8.0, 2.0, 8.0, 2.0) : new Thickness(10.0, 2.0, 10.0, 2.0));
				rb.Padding = (collapsed ? new Thickness(0.0) : new Thickness(14.0, 0.0, 10.0, 0.0));
				rb.HorizontalContentAlignment = (collapsed ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Left);
				System.Windows.Controls.RadioButton radioButton = rb;
				object toolTip;
				if (collapsed)
				{
					toolTip = ((groupTitle != null) ? (groupTitle + " · " + label.Text) : label.Text);
				}
				else
				{
					toolTip = null;
				}
				radioButton.ToolTip = toolTip;
			}
		}
	}

	private void UpdateSidebarGroups()
	{
		(ToggleButton, StackPanel, Border)[] sidebarGroups = SidebarGroups;
		for (int i = 0; i < sidebarGroups.Length; i++)
		{
			(ToggleButton, StackPanel, Border) tuple = sidebarGroups[i];
			ToggleButton grp = tuple.Item1;
			StackPanel items = tuple.Item2;
			Border item = tuple.Item3;
			grp.Visibility = (_sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible);
			item.Visibility = ((!_sidebarCollapsed) ? Visibility.Collapsed : Visibility.Visible);
			items.Margin = (_sidebarCollapsed ? new Thickness(0.0, 0.0, 0.0, 2.0) : new Thickness(10.0, 0.0, 0.0, 4.0));
			items.Visibility = ((!_sidebarCollapsed && grp.IsChecked != true) ? Visibility.Collapsed : Visibility.Visible);
		}
	}

	private void Nav_Checked(object sender, RoutedEventArgs e)
	{
		if (PageSystem == null)
		{
			return;
		}
		FrameworkElement frameworkElement = (FrameworkElement)sender;
		string page = (string)frameworkElement.Tag;
		ShowPage(page);
		string name = ((!(frameworkElement is ContentControl { Content: System.Windows.Controls.Panel panel })) ? "" : (panel.Children.OfType<TextBlock>().LastOrDefault()?.Text ?? ""));
		if (frameworkElement.Parent is FrameworkElement { Name: var items } && items.EndsWith("Items"))
		{
			string text = items;
			if (FindName(text.Substring(0, text.Length - 5)) is ToggleButton group)
			{
				group.IsChecked = true;
				UpdateSidebarGroups();
				name = ((group == GrpSystem) ? "TRIARCH" : (group.Content?.ToString() ?? "")) + " · " + name;
			}
		}
		TitlePage.Text = name;
		MainWindow mainWindow = this;
		string title;
		if (string.IsNullOrWhiteSpace(name))
		{
			title = "Triarch";
		}
		else
		{
			title = (name.StartsWith("TRIARCH") ? name.Replace("TRIARCH", "Triarch") : ("Triarch · " + name));
		}
		mainWindow.Title = title;
		if (page == null)
		{
			return;
		}
		switch (page.Length)
		{
		case 8:
			switch (page[4])
			{
			case 'D':
				if (page == "PageDash")
				{
					UpdateDashboard();
				}
				break;
			case 'A':
				if (page == "PageApps")
				{
					InitApps();
				}
				break;
			}
			break;
		case 9:
			switch (page[4])
			{
			case 'D':
				if (page == "PageDisks")
				{
					RefreshDiskHealth();
				}
				break;
			case 'I':
				if (page == "PageInteg")
				{
				if (page == "PageInteg")
				{
				}
				}
				break;
			}
			break;
		case 10:
			switch (page[4])
			{
			case 'E':
				if (page == "PageEnergy")
				{
					RefreshEnergy();
				}
				break;
			case 'D':
				if (page == "PageDriver")
				{
					InitDriver();
				}
				break;
			}
			break;
		case 12:
			switch (page[4])
			{
			case 'L':
				if (page == "PageLauncher")
				{
					InitLauncher();
				}
				break;
			case 'S':
				if (page == "PageSettings")
				{
					InitSettings();
				}
				break;
			case 'O':
				if (page == "PageOptimize")
				{
					InitOptimize();
				}
				break;
			}
			break;
		case 6:
			if (page == "PageOc")
			{
				UpdateOcGpuState();
				UpdateRecentErrors();
			}
			break;
		case 11:
			if (page == "PageStartup")
			{
				ScanStartup();
			}
			break;
		case 7:
			break;
		}
	}

	private void ShowPage(string name)
	{
		FrameworkElement[] array = new FrameworkElement[19]
		{
			PageDash, PageSystem, PageSensors, PageGpu, PageGames, PageBottleneck, PageOverlay, PageBench, PageOptimize, PageOc,
			PageLauncher, PageDriver, PageStartup, PageApps, PageSettings, PageDisks, PageEnergy, PageNet, PageStress
		};
		foreach (FrameworkElement obj in array)
		{
			obj.Visibility = ((!(obj.Name == name)) ? Visibility.Collapsed : Visibility.Visible);
		}
	}

	private void InfoCategories_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (InfoCategories.SelectedItem is InfoNode node)
		{
			InfoTitle.Text = node.Header;
			InfoDetails.ItemsSource = node.Children;
			bool storage = node.Header == "Armazenamento";
			DrivesPanel.Visibility = ((!storage) ? Visibility.Collapsed : Visibility.Visible);
			if (storage)
			{
				RefreshDrives();
			}
		}
	}

	private async void RefreshDrives()
	{
		_drives = await Task.Run((Func<List<DriveCard>>)DriveMap.Read);
		DriveCards.ItemsSource = _drives;
		long total = _drives.Sum((DriveCard d) => d.Total);
		long free = _drives.Sum((DriveCard d) => d.Free);
		DrivesSummary.Text = $"{_drives.Count} unidades · {DriveCard.Fmt(free)} livres de {DriveCard.Fmt(total)}";
	}

	private void Toast(string title, string text, string icon)
	{
		AddEvent(icon, title, text.Split('\n')[0], "");
		if (_toast == null)
		{
			_toast = new ToastWindow();
		}
		_toast.Notify(title, text, icon);
	}

	private void OnDeviceChange(int ev, nint lParam)
	{
		if ((ev == 32768 || ev == 32772) && lParam != IntPtr.Zero && Marshal.ReadInt32(lParam, 4) == 2)
		{
			int mask = Marshal.ReadInt32(lParam, 12);
			List<string> letters = (from i in Enumerable.Range(0, 26)
				where (mask & (1 << i)) != 0
				select $"{(char)(ushort)(65 + i)}:").ToList();
			OnVolumes(letters, ev == 32768);
		}
	}

	private async void OnVolumes(List<string> letters, bool arrived)
	{
		if (arrived)
		{
			await Task.Delay(800);
		}
		List<DriveCard> before = _drives;
		List<DriveCard> now = await Task.Run((Func<List<DriveCard>>)DriveMap.Read);
		foreach (string l in letters)
		{
			if (arrived)
			{
				DriveCard d = now.FirstOrDefault((DriveCard driveCard) => driveCard.Letter.Equals(l, StringComparison.OrdinalIgnoreCase));
				if (d != null)
				{
					Toast(d.Removable ? "Pendrive conectado" : "Unidade conectada", $"{d.Title} · {DriveCard.Fmt(d.Free)} livres de {DriveCard.Fmt(d.Total)}" + ((d.Disk.Length > 0) ? ("\n" + d.Disk) : ""), "\ue88e");
					continue;
				}
			}
			if (!arrived)
			{
				DriveCard old = before.FirstOrDefault((DriveCard driveCard) => driveCard.Letter.Equals(l, StringComparison.OrdinalIgnoreCase));
				Toast("Unidade removida", (old != null) ? (old.Title + " pode ser desconectada.") : (l + " removida."), "\ue88e");
			}
		}
		_drives = now;
		if (InfoCategories.ItemsSource is IEnumerable<InfoNode> cats)
		{
			InfoNode storage = cats.FirstOrDefault((InfoNode c) => c.Header == "Armazenamento");
			if (storage != null)
			{
				await Task.Run(() =>
				{
					SystemInfoCollector.RefreshDisks(storage);
				});
				if (InfoCategories.SelectedItem == storage)
				{
					InfoDetails.ItemsSource = null;
					InfoDetails.ItemsSource = storage.Children;
				}
			}
		}
		if (DrivesPanel.IsVisible)
		{
			RefreshDrives();
		}
	}

	private void ShowGpu(GpuInfo g)
	{
		_gpu = g;
		GpuName.Text = g.Name;
		GpuSubtitle.Text = g.Subtitle;
		GpuVendor.Text = g.Vendor;
		GpuSections.ItemsSource = g.Sections;
		ListCollectionView live = new ListCollectionView(_monitor.Rows)
		{
			Filter = (object o) => ((SensorRow)o).Hardware == g.Name
		};
		live.SortDescriptions.Add(new SortDescription("Order", ListSortDirection.Ascending));
		live.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
		GpuLive.ItemsSource = live;
		GpuExtras.ItemsSource = g.LiveExtras();
	}

	private void UpdateGpuLive()
	{
		if (_gpus.Count > 0)
		{
			SensorRow load = _monitor.Rows.FirstOrDefault((SensorRow r) => r.Hardware == _gpus[0].Name && r.Type == SensorType.Load && r.Name == "GPU Core");
			TextBlock sideGpuTemp = SideGpuTemp;
			TextBlock topGpuTemp = TopGpuTemp;
			float? num = MainGpuTemp();
			string text;
			if (num.HasValue)
			{
				float t = num.GetValueOrDefault();
				text = $"{t:0.#} °C";
			}
			else
			{
				text = "—";
			}
			string text2 = text;
			topGpuTemp.Text = text;
			sideGpuTemp.Text = text2;
			TextBlock sideGpuLoad = SideGpuLoad;
			TextBlock topGpuLoad = TopGpuLoad;
			num = load?.RawValue;
			string text3;
			if (num.HasValue)
			{
				float l = num.GetValueOrDefault();
				text3 = $"{l:0} %";
			}
			else
			{
				text3 = "—";
			}
			text2 = text3;
			topGpuLoad.Text = text3;
			sideGpuLoad.Text = text2;
		}
		if (_gpu != null && PageGpu.IsVisible)
		{
			GpuExtras.ItemsSource = _gpu.LiveExtras();
		}
	}

	private void InitGames()
	{
		try
		{
			using ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
			using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = s.Get().GetEnumerator();
			if (managementObjectEnumerator.MoveNext())
			{
				ManagementObject mo = (ManagementObject)managementObjectEnumerator.Current;
				_cpuName = mo["Name"]?.ToString()?.Trim() ?? "";
			}
		}
		catch
		{
		}
		_gpuName = ((_gpus.Count > 0) ? _gpus[0].Name : "");
		_gpuIdx = GameCatalog.GpuPerf(_gpuName) ?? 50.0;
		_cpuIdx = GameCatalog.CpuPerf(_cpuName) ?? 80.0;
		GamesSub.Text = "FPS esperado no seu PC · " + GameCatalog.ShortGpuName(_gpuName) + " · " + GameCatalog.ShortCpuName(_cpuName);
		_fpsApi.ApiKey = _settings.HowManyFpsKey;
		if (_fpsApi.Configured)
		{
			ApiKeyBox.Password = _settings.HowManyFpsKey;
		}
		_gameRows = GameCatalog.Games.Select((GameInfo g) => new GameRow(g)).ToList();
		_gameView = new ListCollectionView(_gameRows);
		GameList.ItemsSource = _gameView;
		RefreshGames();
	}

	private void GameOption_Changed(object sender, RoutedEventArgs e)
	{
		if (_gameView != null)
		{
			RefreshGames();
		}
	}

	private void GameSearch_Changed(object sender, TextChangedEventArgs e)
	{
		if (_gameView != null)
		{
			string q = GameSearch.Text.Trim();
			_gameView.Filter = ((q.Length == 0) ? null : ((Predicate<object>)((object o) => ((GameRow)o).Name.Contains(q, StringComparison.OrdinalIgnoreCase))));
		}
	}

	private void ApiKeyBox_Changed(object sender, RoutedEventArgs e)
	{
		ApiKeyHint.Visibility = ((ApiKeyBox.Password.Length != 0) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void BtnApiKey_Click(object sender, RoutedEventArgs e)
	{
		_settings.HowManyFpsKey = ApiKeyBox.Password;
		_settings.Save();
		_fpsApi.ApiKey = _settings.HowManyFpsKey;
		RefreshGames();
	}

	private void RefreshGames()
	{
		string res = CheckedTag(GameResPanel) ?? "1920x1080";
		string preset = CheckedTag(GamePresetPanel) ?? "Alto";
		string up = CheckedTag(GameUpscalePanel) ?? "Native";
		foreach (GameRow row in _gameRows)
		{
			FpsEstimate est = GameCatalog.Estimate(row.Game, _gpuIdx, _cpuIdx, res, preset, up);
			string limiter = est.Limiter switch
			{
				Bottleneck.Cpu => "gargalo: CPU", 
				Bottleneck.Gpu => "gargalo: GPU", 
				Bottleneck.Engine => $"trava do jogo em {row.Game.EngineCap:0} FPS", 
				_ => "", 
			};
			row.Set(est.Avg, 0.0, "ESTIMATIVA", limiter);
		}
		_gameFetch?.Cancel();
		if (_fpsApi.Configured)
		{
			GameSourceTitle.Text = "Fonte: HowManyFPS (dados reais)";
			GameSourceText.Text = "FPS médio e 1% low medidos em sessões reais de jogo. Respostas ficam em cache por 7 dias. A API usa as configurações gráficas dela; \"Gráficos\" vale só para a estimativa.";
			BtnApiKey.Content = "ATUALIZAR CHAVE";
			_gameFetch = new CancellationTokenSource();
			FetchGamesAsync(res, up, _gameFetch.Token);
		}
		else
		{
			GameSourceTitle.Text = "Fonte: estimativa local (aproximada)";
			GameSourceText.Text = "Calculada a partir do desempenho relativo da sua GPU e CPU. Para FPS medido em jogo, peça uma chave da API em info@howmanyfps.com e cole ao lado.";
			BtnApiKey.Content = "SALVAR CHAVE";
		}
	}

	private async Task FetchGamesAsync(string res, string up, CancellationToken ct)
	{
		string cpu = GameCatalog.ShortCpuName(_cpuName);
		string gpu = GameCatalog.ShortGpuName(_gpuName);
		int ok = 0;
		foreach (GameRow row in _gameRows)
		{
			if (ct.IsCancellationRequested)
			{
				return;
			}
			try
			{
				HowManyFpsClient.Result r = await _fpsApi.GetAsync(cpu, gpu, row.Name, res, up, ct);
				row.Set(r.AvgFps, r.Min1Fps, "HOWMANYFPS", "");
				ok++;
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception ex2)
			{
				row.Set(row.Fps, 0.0, "ESTIMATIVA", "API: " + ex2.Message);
				if (ex2.Message.Contains("chave") || ex2.Message.Contains("cota"))
				{
					GameSourceText.Text = "A API recusou a consulta (" + ex2.Message + "). Mostrando a estimativa local.";
					return;
				}
			}
		}
		if (!ct.IsCancellationRequested)
		{
			GameSourceText.Text = $"{ok} de {_gameRows.Count} jogos com dados da HowManyFPS; os demais usam a estimativa local.";
		}
	}

	private void InitBottleneckAndOverlay()
	{
		_frames.Start();
		(double Ratio, string Text) tuple = BottleneckAnalyzer.Balance(_gpuIdx, _cpuIdx);
		double ratio = tuple.Ratio;
		string text = tuple.Text;
		_balanceRatio = ratio;
		BnCpuName.Text = "CPU · " + GameCatalog.ShortCpuName(_cpuName);
		BnGpuName.Text = "GPU · " + GameCatalog.ShortGpuName(_gpuName);
		BnBalanceText.Text = text;
		foreach (System.Windows.Controls.RadioButton item in OvCornerPanel.Children.OfType<System.Windows.Controls.RadioButton>())
		{
			item.IsChecked = (string)item.Tag == _settings.OverlayCorner;
		}
		foreach (System.Windows.Controls.RadioButton item2 in OvScalePanel.Children.OfType<System.Windows.Controls.RadioButton>())
		{
			item2.IsChecked = Math.Abs(double.Parse((string)item2.Tag, CultureInfo.InvariantCulture) - _settings.OverlayScale) < 0.01;
		}
		BuildOverlayPicker();
		_overlayReady = true;
		UpdateOverlayStatus(null);
	}

	private void BnBalanceCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		double w = BnBalanceCanvas.ActualWidth;
		double t = Math.Clamp((Math.Log(_balanceRatio) - Math.Log(0.4)) / (Math.Log(2.5) - Math.Log(0.4)), 0.0, 1.0);
		Canvas.SetLeft(BnBalanceDot, t * (w - BnBalanceDot.Width));
	}

	private static MeterItem Meter(string label, double? pct, string? suffix = null)
	{
		return new MeterItem(label, (!pct.HasValue) ? "—" : $"{pct:0} %{suffix}", Math.Clamp(pct.GetValueOrDefault() / 100.0, 0.0, 1.0));
	}

	private void UpdateBottleneck()
	{
		FrameStats frames = _frames.GetForegroundStats();
		LoadSample s = BottleneckAnalyzer.Sample(_monitor, _gpus.FirstOrDefault(), frames);
		Limiter lim = s.Classify();
		if (PageBottleneck.IsVisible || _bnSession != null)
		{
			BnLiveTitle.Text = BottleneckAnalyzer.Title(lim);
			BnLiveAdvice.Text = BottleneckAnalyzer.Advice(lim);
			BnLiveGame.Text = ((frames != null) ? $"{frames.ProcessName} · {frames.Fps:0} FPS · 1% low {frames.Low1Fps:0}" : "Nenhum jogo em primeiro plano (o FPS aparece quando o jogo estiver na tela)");
			bool flag = (uint)(lim - 1) <= 3u;
			bool alert = flag;
			BnLiveCard.Background = (alert ? Fg : Card);
			BnLiveTitle.Foreground = (alert ? Ink : Fg);
			TextBlock bnLiveLabel = BnLiveLabel;
			TextBlock bnLiveGame = BnLiveGame;
			System.Windows.Media.Brush brush = (BnLiveAdvice.Foreground = (alert ? Ink : Muted));
			System.Windows.Media.Brush foreground = (bnLiveGame.Foreground = brush);
			bnLiveLabel.Foreground = foreground;
			ItemsControl bnMeters = BnMeters;
			MeterItem[] array = new MeterItem[6]
			{
				Meter("Carga da GPU", s.GpuLoad),
				Meter("Carga total da CPU", s.CpuTotal),
				Meter((s.BusiestCore != null) ? ("Núcleo mais ocupado (" + s.BusiestCore + ")") : "Núcleo mais ocupado", s.CpuMaxCore),
				Meter("VRAM em uso", s.VramPct),
				Meter("RAM em uso", s.RamPct),
				null
			};
			float? gpuTemp = s.GpuTemp;
			string text;
			if (gpuTemp.HasValue)
			{
				float g = gpuTemp.GetValueOrDefault();
				text = $"{g:0} °C";
			}
			else
			{
				text = "—";
			}
			gpuTemp = s.CpuTemp;
			string text2;
			if (gpuTemp.HasValue)
			{
				float c = gpuTemp.GetValueOrDefault();
				text2 = $"{c:0} °C";
			}
			else
			{
				text2 = "—";
			}
			array[5] = new MeterItem("Temperaturas (GPU / CPU)", text + " / " + text2, Math.Clamp((double)Math.Max(s.GpuTemp.GetValueOrDefault(), s.CpuTemp.GetValueOrDefault()) / 100.0, 0.0, 1.0));
			bnMeters.ItemsSource = array;
		}
		if (_bnSession != null)
		{
			_bnSession.Add(s);
			TimeSpan el = DateTime.Now - _bnSession.Start;
			BnSessionStatus.Text = $"Coletando… {el:m\\:ss} de {_bnSession.Duration:m\\:ss} · {_bnSession.Count} amostras · " + ((frames != null) ? ("jogo detectado: " + frames.ProcessName) : "abra o jogo agora");
			if (_bnSession.Done)
			{
				FinishBottleneckSession();
			}
		}
	}

	private void BtnBnSession_Click(object sender, RoutedEventArgs e)
	{
		if (_bnSession != null)
		{
			_bnSession = null;
			BtnBnSession.Content = "INICIAR ANÁLISE";
			BtnBnSession.Style = (Style)FindResource("PrimaryButton");
			BnSessionStatus.Text = "Análise cancelada.";
		}
		else
		{
			int minutes = int.Parse(CheckedTag(BnDurationPanel) ?? "1");
			_bnSession = new BottleneckSession
			{
				Duration = TimeSpan.FromMinutes(minutes)
			};
			BnResult.Visibility = Visibility.Collapsed;
			BtnBnSession.Content = "CANCELAR";
			BtnBnSession.Style = (Style)FindResource("OutlineButton");
			BnSessionStatus.Text = "Coletando… abra o jogo agora.";
		}
	}

	private void FinishBottleneckSession()
	{
		BottleneckSession sess = _bnSession;
		_bnSession = null;
		BtnBnSession.Content = "INICIAR ANÁLISE";
		BtnBnSession.Style = (Style)FindResource("PrimaryButton");
		BnSessionStatus.Text = $"Análise concluída às {DateTime.Now:HH:mm} ({sess.Count} amostras).";
		Limiter main = sess.Main ?? Limiter.Capped;
		BnResultTitle.Text = "Resultado: " + BottleneckAnalyzer.Title(main);
		List<string> parts = new List<string>();
		double? avgFps = sess.AvgFps;
		if (avgFps.HasValue)
		{
			double fps = avgFps.GetValueOrDefault();
			parts.Add($"FPS médio {fps:0}");
		}
		avgFps = sess.Avg((LoadSample x) => x.GpuLoad);
		if (avgFps.HasValue)
		{
			double g = avgFps.GetValueOrDefault();
			parts.Add($"GPU média {g:0} %");
		}
		avgFps = sess.Avg((LoadSample x) => x.CpuTotal);
		if (avgFps.HasValue)
		{
			double c = avgFps.GetValueOrDefault();
			parts.Add($"CPU média {c:0} %");
		}
		avgFps = sess.Avg((LoadSample x) => x.CpuMaxCore);
		if (avgFps.HasValue)
		{
			double k = avgFps.GetValueOrDefault();
			parts.Add($"núcleo mais ocupado {k:0} %");
		}
		avgFps = sess.Max((LoadSample x) => x.VramPct);
		if (avgFps.HasValue)
		{
			double v = avgFps.GetValueOrDefault();
			parts.Add($"VRAM máx. {v:0} %");
		}
		avgFps = sess.Max((LoadSample x) => x.GpuTemp);
		if (avgFps.HasValue)
		{
			double gt = avgFps.GetValueOrDefault();
			parts.Add($"GPU máx. {gt:0} °C");
		}
		BnResultSummary.Text = string.Join(" · ", parts);
		BnDistribution.ItemsSource = (from d in sess.Distribution()
			select new MeterItem(BottleneckAnalyzer.Title(d.Limiter) + " — % do tempo", $"{d.Pct:0} %", d.Pct / 100.0)).ToList();
		BnResultAdvice.Text = BottleneckAnalyzer.Advice(main);
		BnResult.Visibility = Visibility.Visible;
	}

	private void InitBench()
	{
		_benchBitmap = new WriteableBitmap(960, 540, 96.0, 96.0, PixelFormats.Bgra32, null);
		BenchImage.Source = _benchBitmap;
		UpdateBenchHistory();
		UpdateBenchLive();
	}

	private async void BtnBench_Click(object sender, RoutedEventArgs e)
	{
		if (BenchRunning)
		{
			_benchCts.Cancel();
			return;
		}
		if (TestRunning || _gameBenchRunning)
		{
			BenchHint.Text = "Pare o outro teste antes de rodar o benchmark.";
			return;
		}
		string mode = CheckedTag(BenchModePanel) ?? "both";
		bool sustained = CheckedTag(BenchDurationPanel) == "10";
		TimeSpan minDuration = (sustained ? TimeSpan.FromMinutes(10.0) : TimeSpan.Zero);
		int threads = Environment.ProcessorCount;
		_benchCts = new CancellationTokenSource();
		CancellationToken ct = _benchCts.Token;
		BtnBench.Content = "PARAR";
		BtnBench.Style = (Style)FindResource("OutlineButton");
		WrapPanel benchModePanel = BenchModePanel;
		WrapPanel benchDurationPanel = BenchDurationPanel;
		bool flag = (BenchTestPanel.IsEnabled = false);
		bool isEnabled = (benchDurationPanel.IsEnabled = flag);
		benchModePanel.IsEnabled = isEnabled;
		BenchHint.Text = "";
		BenchIdle.Visibility = Visibility.Collapsed;
		TextBlock benchMt = BenchMt;
		string text = (BenchSt.Text = "—");
		benchMt.Text = text;
		BenchRatio.Text = "";
		_benchStart = DateTime.Now;
		if (_benchTimer == null)
		{
			_benchTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100.0), DispatcherPriority.Render, (object? _, EventArgs _) =>
			{
				UpdateBenchView();
			}, Dispatcher);
		}
		_benchTimer.Start();
		double? mt = null;
		double? st = null;
		try
		{
			text = mode;
			if ((text == "mt" || text == "both") ? true : false)
			{
				_benchPhase = $"Multi-core · {threads} threads";
				_benchPhaseStart = DateTime.Now;
				BenchResult r = await Task.Run(() => _bench.Run(threads, minDuration, ct), ct);
				if (!ct.IsCancellationRequested)
				{
					mt = r.Score;
					BenchMt.Text = $"{r.Score:N0}";
				}
			}
			isEnabled = !ct.IsCancellationRequested;
			if (isEnabled)
			{
				text = mode;
				flag = ((text == "st" || text == "both") ? true : false);
				isEnabled = flag;
			}
			if (isEnabled)
			{
				_benchPhase = "Single-core · 1 thread";
				_benchPhaseStart = DateTime.Now;
				BenchResult r2 = await Task.Run(() => _bench.Run(1, minDuration, ct), ct);
				if (!ct.IsCancellationRequested)
				{
					st = r2.Score;
					BenchSt.Text = $"{r2.Score:N0}";
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		_benchTimer.Stop();
		UpdateBenchView();
		bool isCancellationRequested = ct.IsCancellationRequested;
		_benchCts = null;
		BtnBench.Content = "INICIAR";
		BtnBench.Style = (Style)FindResource("PrimaryButton");
		WrapPanel benchModePanel2 = BenchModePanel;
		WrapPanel benchDurationPanel2 = BenchDurationPanel;
		flag = (BenchTestPanel.IsEnabled = true);
		isEnabled = (benchDurationPanel2.IsEnabled = flag);
		benchModePanel2.IsEnabled = isEnabled;
		BenchBuckets.Children.Clear();
		TimeSpan total = DateTime.Now - _benchStart;
		if (isCancellationRequested)
		{
			BenchStatus.Text = "Cancelado";
			return;
		}
		BenchStatus.Text = $"Concluído em {total:m\\:ss}";
		if (mt.HasValue)
		{
			double m = mt.GetValueOrDefault();
			if (st.HasValue)
			{
				double s = st.GetValueOrDefault();
				BenchRatio.Text = $"Multi ÷ single: {m / s:0.0}x com {threads} threads ({Environment.ProcessorCount / 2} núcleos com SMT costumam render 8–12x).";
			}
		}
		_settings.BenchRuns.Insert(0, new BenchRun(DateTime.Now, mt, st, sustained, GameCatalog.ShortCpuName(_cpuName)));
		MainWindow mainWindow = this;
		string value;
		if (mt.HasValue)
		{
			double mte = mt.GetValueOrDefault();
			value = $"{mte:N0} pts";
		}
		else if (st.HasValue)
		{
			double ste = st.GetValueOrDefault();
			value = $"{ste:N0} pts";
		}
		else
		{
			value = "";
		}
		mainWindow.AddEvent("\ue8b9", "TRIARCH BENCH", "Render de CPU", value);
		if (_settings.BenchRuns.Count > 10)
		{
			_settings.BenchRuns.RemoveRange(10, _settings.BenchRuns.Count - 10);
		}
		_settings.Save();
		UpdateBenchHistory();
	}

	private void UpdateBenchView()
	{
		_benchBitmap.WritePixels(new Int32Rect(0, 0, 960, 540), _bench.Pixels, 3840, 0);
		BenchBuckets.Children.Clear();
		int[] active = _bench.Active;
		foreach (int b in active)
		{
			if (b >= 0)
			{
				(int X, int Y, int W, int H) tuple = _bench.Buckets[b];
				int x = tuple.X;
				int y = tuple.Y;
				int w = tuple.W;
				int h = tuple.H;
				System.Windows.Shapes.Rectangle rect = new System.Windows.Shapes.Rectangle
				{
					Width = w,
					Height = h,
					Stroke = System.Windows.Media.Brushes.White,
					StrokeThickness = 1.5
				};
				Canvas.SetLeft(rect, x);
				Canvas.SetTop(rect, y);
				BenchBuckets.Children.Add(rect);
			}
		}
		if (BenchRunning)
		{
			double frac = ((_bench.BucketsTotal > 0) ? ((double)_bench.BucketsDone / (double)_bench.BucketsTotal) : 0.0);
			BenchProgress.ScaleX = frac;
			TimeSpan phaseElapsed = DateTime.Now - _benchPhaseStart;
			string eta = ((frac >= 0.03) ? $" · faltam ~{TimeSpan.FromSeconds(phaseElapsed.TotalSeconds * (1.0 - frac) / frac):m\\:ss}" : "");
			BenchStatus.Text = $"{_benchPhase} · {frac:P0}{eta}";
			BenchElapsed.Text = (DateTime.Now - _benchStart).ToString("mm\\:ss");
		}
		else
		{
			BenchProgress.ScaleX = 0.0;
		}
	}

	private void UpdateBenchLive()
	{
		List<float> clocks = (from r in _monitor.Rows
			where r.HardwareType == HardwareType.Cpu && r.Type == SensorType.Clock && r.Name.StartsWith("Core #") && r.RawValue > 0f
			select r.RawValue.Value).ToList();
		ItemsControl benchLive = BenchLive;
		KeyValuePair<string, string>[] array = new KeyValuePair<string, string>[6]
		{
			new KeyValuePair<string, string>("CPU", GameCatalog.ShortCpuName(_cpuName)),
			new KeyValuePair<string, string>("Threads", $"{Environment.ProcessorCount}"),
			default,
			default,
			default,
			default
		};
		float? cpuTemperature = _monitor.CpuTemperature;
		string value;
		if (cpuTemperature.HasValue)
		{
			float t = cpuTemperature.GetValueOrDefault();
			value = $"{t:0.#} °C";
		}
		else
		{
			value = "—";
		}
		array[2] = new KeyValuePair<string, string>("Temperatura", value);
		array[3] = new KeyValuePair<string, string>("Clock médio", (clocks.Count > 0) ? $"{clocks.Average():0} MHz" : "—");
		cpuTemperature = _monitor.CpuPower;
		string value2;
		if (cpuTemperature.HasValue)
		{
			float p = cpuTemperature.GetValueOrDefault();
			value2 = $"{p:0.#} W";
		}
		else
		{
			value2 = "—";
		}
		array[4] = new KeyValuePair<string, string>("Consumo", value2);
		cpuTemperature = _monitor.CpuLoad;
		string value3;
		if (cpuTemperature.HasValue)
		{
			float l = cpuTemperature.GetValueOrDefault();
			value3 = $"{l:0} %";
		}
		else
		{
			value3 = "—";
		}
		array[5] = new KeyValuePair<string, string>("Carga", value3);
		benchLive.ItemsSource = array;
	}

	private void UpdateBenchHistory()
	{
		List<BenchRun> runs = _settings.BenchRuns;
		BenchHistoryEmpty.Visibility = ((runs.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		double bestMt = runs.Max((BenchRun r) => r.Multi) ?? 1.0;
		double bestSt = runs.Max((BenchRun r) => r.Single) ?? 1.0;
		BenchHistory.ItemsSource = runs.Select((BenchRun r) =>
		{
			string label = $"{r.Date:dd/MM HH:mm}{(r.Sustained ? " · 10 min" : "")}";
			double? multi = r.Multi;
			string text;
			if (multi.HasValue)
			{
				double valueOrDefault = multi.GetValueOrDefault();
				text = $"{valueOrDefault:N0}";
			}
			else
			{
				text = "—";
			}
			multi = r.Single;
			string text2;
			if (multi.HasValue)
			{
				double valueOrDefault2 = multi.GetValueOrDefault();
				text2 = $"{valueOrDefault2:N0}";
			}
			else
			{
				text2 = "—";
			}
			string value = text + " / " + text2;
			multi = r.Multi;
			double num;
			if (multi.HasValue)
			{
				double valueOrDefault3 = multi.GetValueOrDefault();
				num = valueOrDefault3 / bestMt;
			}
			else
			{
				multi = r.Single;
				if (multi.HasValue)
				{
					double valueOrDefault4 = multi.GetValueOrDefault();
					num = valueOrDefault4 / bestSt;
				}
				else
				{
					num = 0.0;
				}
			}
			double fraction = num;
			return new MeterItem(label, value, fraction);
		}).ToList();
	}

	private void InitOptimize()
	{
		if (!_optInit)
		{
			_optInit = true;
			foreach (Tweak t in _tweaks)
			{
				try
				{
					t.Refresh();
				}
				catch (Exception ex)
				{
					t.Status = "Não foi possível ler: " + ex.Message;
				}
			}
			OptTweaks.ItemsSource = _tweaks;
		}
		RefreshApps();
		UpdateRamStatus();
		RefreshDduStateAsync();
	}

	private async void BtnAdvOptimizer_Click(object sender, RoutedEventArgs e)
	{
		BtnAdvOptimizer.IsEnabled = false;
		OptAdvStatus.Text = "Aguardando confirmação de Administrador (UAC) para abrir o PowerShell…";
		try
		{
			var (started, cancelled, errorMsg) = await Task.Run(() =>
			{
				try
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = "powershell.exe",
						Arguments = "-NoExit -Command \"irm https://christitus.com/win | iex\"",
						UseShellExecute = true,
						Verb = "runas"
					});
					return (true, false, "");
				}
				catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
				{
					return (false, true, "Solicitação de Administrador (UAC) cancelada pelo usuário.");
				}
				catch (Exception ex2)
				{
					return (false, false, ex2.Message);
				}
			});
			if (started)
			{
				OptAdvStatus.Text = "Janela externa do PowerShell iniciada como Administrador.";
				AddEvent("\ue9f3", "Otimizador Avançado", "Chris Titus WinUtil iniciado via PowerShell (RunAs)", "");
			}
			else if (cancelled)
			{
				OptAdvStatus.Text = errorMsg;
			}
			else
			{
				OptAdvStatus.Text = "Falha ao iniciar o PowerShell: " + errorMsg;
			}
		}
		finally
		{
			BtnAdvOptimizer.IsEnabled = true;
		}
	}

	private void UpdateRamStatus()
	{
		var (total, avail) = Optimizer.MemoryStatus();
		if (total != 0L)
		{
			double used = total - avail;
			OptRamText.Text = $"{used / 1073741824.0:0.0} de {(double)total / 1073741824.0:0.0} GB · {used / (double)total:P0}";
			OptRamBar.ScaleX = used / (double)total;
		}
	}

	private void RefreshApps()
	{
		_apps = Optimizer.ListApps();
		OptApps.ItemsSource = _apps;
	}

	private void BtnRefreshApps_Click(object sender, RoutedEventArgs e)
	{
		RefreshApps();
	}

	private async void BtnOptimize_Click(object sender, RoutedEventArgs e)
	{
		List<BackgroundApp> toClose = _apps.Where((BackgroundApp a) => a.Selected).ToList();
		string list = ((toClose.Count == 0) ? "nenhum programa marcado" : string.Join("\n", toClose.Select((BackgroundApp a) => "  • " + a.Title)));
		if (System.Windows.MessageBox.Show("O TRIARCH OTIMIZA vai:\n\n1. Limpar a memória em espera e enxugar a RAM dos programas.\n2. Fechar os programas marcados:\n" + list + "\n\nSalve o que estiver aberto nesses programas antes. Continuar?", "TRIARCH OTIMIZA", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
		{
			BtnOptimize.IsEnabled = false;
			BtnOptimize.Content = "OTIMIZANDO…";
			OptResult.Text = "";
			CloseResult closeResult = await Task.Run(() => Optimizer.CloseApps(toClose));
			CleanupResult mem = await Task.Run((Func<CleanupResult>)Optimizer.CleanMemory);
			BtnOptimize.IsEnabled = true;
			BtnOptimize.Content = "TRIARCH OTIMIZA";
			List<string> parts = new List<string> { $"Memória liberada: {(double)mem.FreedBytes / 1048576.0:N0} MB" };
			parts.Add(mem.StandbyPurged ? "memória em espera limpa" : ("memória em espera não limpa (" + mem.StandbyError + ")"));
			if (closeResult.Closed.Count > 0)
			{
				parts.Add("fechados: " + string.Join(", ", closeResult.Closed));
			}
			if (closeResult.NotClosed.Count > 0)
			{
				parts.Add("não fecharam (podem ter algo não salvo ou pedir confirmação): " + string.Join(", ", closeResult.NotClosed));
			}
			OptResult.Text = string.Join(" · ", parts) + ".";
			RefreshApps();
			UpdateRamStatus();
		}
	}

	private void Tweak_Click(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.CheckBox { Tag: Tweak t, IsChecked: var isChecked } box)
		{
			bool turnOn = isChecked == true;
			try
			{
				t.Write(turnOn, _settings);
				_settings.Save();
				t.Refresh();
				t.Status = ((t.IsOn == turnOn) ? ((turnOn ? "Aplicado." : "Desligado.") + (t.NeedsRestart ? " Reinicie o PC para valer." : "")) : "O Windows não aceitou a mudança (pode exigir administrador).");
			}
			catch (Exception ex)
			{
				t.Refresh();
				t.Status = "Falhou: " + ex.Message;
			}
			box.IsChecked = t.IsOn;
		}
	}

	private bool RequireAdmin()
	{
		if (SensorMonitor.IsElevated)
		{
			return true;
		}
		System.Windows.MessageBox.Show("Esta ação precisa do Triarch aberto como administrador.", "Triarch", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		return false;
	}

	private void BtnRestoreTweaks_Click(object sender, RoutedEventArgs e)
	{
		if (_settings.TweakBackup.Count == 0)
		{
			System.Windows.MessageBox.Show("Nenhum ajuste foi alterado pelo Triarch ainda.", "Otimização", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		else
		{
			if (System.Windows.MessageBox.Show("Voltar todos os ajustes do Windows para como estavam antes do Triarch alterá-los?", "Otimização", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
			{
				return;
			}
			bool restart = _tweaks.Any((Tweak t) => t.NeedsRestart && _settings.TweakBackup.ContainsKey(t.Id));
			WindowsTweaks.RestoreAll(_tweaks, _settings);
			foreach (Tweak tweak in _tweaks)
			{
				tweak.Refresh();
				tweak.Status = "Restaurado.";
			}
			if (restart)
			{
				System.Windows.MessageBox.Show("Alguns ajustes só voltam ao original depois de reiniciar o PC.", "TRIARCH OTIMIZA");
			}
		}
	}

	private void BenchTest_Checked(object sender, RoutedEventArgs e)
	{
		if (BenchPanelRender != null)
		{
			string test = (string)((FrameworkElement)sender).Tag;
			BenchPanelRender.Visibility = ((!(test == "render")) ? Visibility.Collapsed : Visibility.Visible);
			BenchPanelGpu.Visibility = ((!(test == "gpu")) ? Visibility.Collapsed : Visibility.Visible);
			BenchPanelRt.Visibility = ((!(test == "rt")) ? Visibility.Collapsed : Visibility.Visible);
			BenchPanelSim.Visibility = ((!(test == "sim")) ? Visibility.Collapsed : Visibility.Visible);
			BenchPanelMem.Visibility = ((!(test == "mem")) ? Visibility.Collapsed : Visibility.Visible);
			BenchPanelDisk.Visibility = ((!(test == "disk")) ? Visibility.Collapsed : Visibility.Visible);
			if (test == "disk")
			{
				InitDiskBench();
			}
			TextBlock benchSub = BenchSub;
			benchSub.Text = test switch
			{
				"gpu" => "Cena de jogo em Direct3D 11 para medir a placa de vídeo, no estilo 3DMark / Unigine", 
				"rt" => "Ray tracing por hardware (DXR 1.1, Direct3D 12): usa os núcleos de RT da placa, com 3 passadas e nota pela mediana", 
				"sim" => "Quanto FPS a CPU aguenta numa simulação de jogo com milhares de unidades", 
				"mem" => "Latência e banda da RAM e velocidade de carregamento do disco", 
				"disk" => "Velocidade de leitura e escrita do disco: sequencial e acessos aleatórios de 4 KB, no estilo CrystalDiskMark", 
				_ => "Benchmark de CPU por renderização 3D (path tracing), no mesmo princípio do Cinebench", 
			};
			UpdateGameBenchHistory();
		}
	}

	private void SaveBenchRecord(string test, double score, string summary)
	{
		AddEvent("\ue8b9", "Benchmark concluído", summary, "");
		_settings.BenchRecords.Insert(0, new BenchRecord(DateTime.Now, test, score, summary));
		_settings.BenchRecords = (from r in (from r in _settings.BenchRecords
				group r by r.Test).SelectMany((IGrouping<string, BenchRecord> g) => g.Take(10))
			orderby r.Date descending
			select r).ToList();
		_settings.Save();
		UpdateGameBenchHistory();
	}

	private void UpdateGameBenchHistory()
	{
		GbHistory.ItemsSource = For("gpu");
		RtHistory.ItemsSource = RtHistoryItems();
		SimHistory.ItemsSource = For("sim");
		MemHistory.ItemsSource = For("mem");
		DiskHistory.ItemsSource = For("disk");
		List<MeterItem> For(string test)
		{
			List<BenchRecord> list = _settings.BenchRecords.Where((BenchRecord r) => r.Test == test).ToList();
			double best = ((list.Count > 0) ? list.Max((BenchRecord r) => r.Score) : 1.0);
			return list.Select((BenchRecord r) => new MeterItem($"{r.Date:dd/MM HH:mm}", r.Summary, (best > 0.0) ? (r.Score / best) : 0.0)).ToList();
		}
	}

	private void SetGameBenchRunning(System.Windows.Controls.Button btn, bool running)
	{
		_gameBenchRunning = running;
		btn.Content = (running ? "CANCELAR" : "INICIAR");
		btn.Style = (Style)FindResource(running ? "OutlineButton" : "PrimaryButton");
		BenchTestPanel.IsEnabled = !running;
	}

	private async void BtnGpuBench_Click(object sender, RoutedEventArgs e)
	{
		if (_gameBenchRunning)
		{
			_gpuBench?.Cancel();
			return;
		}
		if (AnyBenchRunning || TestRunning)
		{
			GbStatus.Text = "Termine o outro teste antes.";
			return;
		}
		int[] size = (CheckedTag(GbResPanel) ?? "1920x1080").Split('x').Select(int.Parse).ToArray();
		int secs = int.Parse(CheckedTag(GbDurPanel) ?? "60");
		_gpuBench = new GpuGameBench
		{
			Width = size[0],
			Height = size[1],
			Duration = TimeSpan.FromSeconds(secs),
			AdapterName = ((_gpus.Count > 0) ? _gpus[0].Name : "")
		};
		SetGameBenchRunning(BtnGpuBench, running: true);
		GbResult.ItemsSource = null;
		GpuGameBench bench = _gpuBench;
		DispatcherTimer timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250.0), DispatcherPriority.Normal, (object? _, EventArgs _) =>
		{
			GbFpsNow.Text = ((bench.CurrentFps > 0.0) ? $"{bench.CurrentFps:0}" : "—");
			GbProgress.ScaleX = bench.Progress;
			GbStatus.Text = $"Rodando · {size[0]}x{size[1]} · {bench.Progress:P0}";
		}, Dispatcher);
		timer.Start();
		_gpuBenchActive = true;
		GbGpuNow.Text = "sensores pausados";
		bool overlayWasOn = ChkOverlay.IsChecked == true;
		if (overlayWasOn)
		{
			ChkOverlay.IsChecked = false;
		}
		GpuGameResult r = await Task.Factory.StartNew((Func<GpuGameResult>)bench.Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
		timer.Stop();
		_gpuBenchActive = false;
		if (overlayWasOn)
		{
			ChkOverlay.IsChecked = true;
		}
		GbGpuNow.Text = "—";
		SetGameBenchRunning(BtnGpuBench, running: false);
		GbProgress.ScaleX = 0.0;
		GbFpsNow.Text = "—";
		if (r == null)
		{
			GbStatus.Text = (bench.Cancelled ? "Cancelado" : ("Falhou: " + bench.Error));
			return;
		}
		GbStatus.Text = "Concluído · " + r.Adapter;
		GbResult.ItemsSource = new KeyValuePair<string, string>[5]
		{
			new KeyValuePair<string, string>("Nota", $"{r.Score:N0} pts"),
			new KeyValuePair<string, string>("FPS médio", $"{r.AvgFps:0.0}"),
			new KeyValuePair<string, string>("1% low", $"{r.Low1Fps:0.0}"),
			new KeyValuePair<string, string>("Resolução", $"{r.Width}x{r.Height}"),
			new KeyValuePair<string, string>("Frames", $"{r.Frames:N0}")
		};
		SaveBenchRecord("gpu", r.Score, $"{r.Score:N0} pts · {r.AvgFps:0} FPS · {r.Height}p");
	}

	private async void BtnSimBench_Click(object sender, RoutedEventArgs e)
	{
		if (_gameBenchRunning)
		{
			_gameBenchCts?.Cancel();
			return;
		}
		if (AnyBenchRunning || TestRunning)
		{
			SimStatus.Text = "Termine o outro teste antes.";
			return;
		}
		int secs = int.Parse(CheckedTag(SimDurPanel) ?? "20");
		if (_simBitmap == null)
		{
			_simBitmap = new WriteableBitmap(440, 440, 96.0, 96.0, PixelFormats.Bgra32, null);
		}
		if (_simPixels.Length == 0)
		{
			_simPixels = new int[193600];
		}
		SimImage.Source = _simBitmap;
		SimIdle.Visibility = Visibility.Collapsed;
		GameSimBench sim = new GameSimBench();
		_gameBenchCts = new CancellationTokenSource();
		CancellationToken ct = _gameBenchCts.Token;
		SetGameBenchRunning(BtnSimBench, running: true);
		SimResult.ItemsSource = null;
		SimStatus.Text = "Aquecendo (2 s)…";
		DispatcherTimer timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100.0), DispatcherPriority.Render, (object? _, EventArgs _) =>
		{
			DrawSim(sim);
			SimProgress.ScaleX = sim.Progress;
			if (sim.FramesDone > 0)
			{
				SimStatus.Text = $"Simulando · {sim.Progress:P0}";
			}
			SimFpsNow.Text = ((sim.CurrentFps > 0.0) ? $"{sim.CurrentFps:0} FPS" : "");
		}, Dispatcher);
		timer.Start();
		GameSimResult r = await Task.Run(() => sim.Run(TimeSpan.FromSeconds(secs), ct));
		timer.Stop();
		bool isCancellationRequested = ct.IsCancellationRequested;
		SetGameBenchRunning(BtnSimBench, running: false);
		SimProgress.ScaleX = 0.0;
		SimFpsNow.Text = "";
		if (isCancellationRequested)
		{
			SimStatus.Text = "Cancelado";
			return;
		}
		SimStatus.Text = $"Concluído · {r.Frames:N0} frames em {r.Seconds:0} s";
		SimResult.ItemsSource = new KeyValuePair<string, string>[5]
		{
			new KeyValuePair<string, string>("FPS médio (nota)", $"{r.AvgFps:0}"),
			new KeyValuePair<string, string>("1% low", $"{r.Low1Fps:0}"),
			new KeyValuePair<string, string>("Thread principal", $"{r.MainThreadMs:0.00} ms/frame"),
			new KeyValuePair<string, string>("Física + IA (paralelo)", $"{r.ParallelMs:0.00} ms/frame"),
			new KeyValuePair<string, string>("Unidades", $"{30000:N0}")
		};
		SaveBenchRecord("sim", r.Score, $"{r.AvgFps:0} FPS · 1% low {r.Low1Fps:0}");
	}

	private void DrawSim(GameSimBench sim)
	{
		Array.Fill(_simPixels, -16448251);
		float[] xs = sim.ViewX;
		float[] ys = sim.ViewY;
		byte[] team = sim.ViewTeam;
		float k = 0.44f;
		for (int i = 0; i < 30000; i++)
		{
			int px = (int)(xs[i] * k);
			int py = (int)(ys[i] * k);
			if ((uint)px < 440u && (uint)py < 440u)
			{
				_simPixels[py * 440 + px] = ((team[i] == 0) ? (-657931) : (-10855846));
			}
		}
		_simBitmap.WritePixels(new Int32Rect(0, 0, 440, 440), _simPixels, 1760, 0);
	}

	private async void BtnMemBench_Click(object sender, RoutedEventArgs e)
	{
		if (_gameBenchRunning)
		{
			_gameBenchCts?.Cancel();
			return;
		}
		if (AnyBenchRunning || TestRunning)
		{
			MemStatus.Text = "Termine o outro teste antes.";
			return;
		}
		_gameBenchCts = new CancellationTokenSource();
		CancellationToken ct = _gameBenchCts.Token;
		SetGameBenchRunning(BtnMemBench, running: true);
		List<MemRow> rows = new List<MemRow>();
		(string Phase, double Frac) progress = (Phase: "", Frac: 0.0);
		DispatcherTimer timer = new DispatcherTimer(TimeSpan.FromMilliseconds(150.0), DispatcherPriority.Normal, (object? _, EventArgs _) =>
		{
			MemStatus.Text = progress.Phase;
			MemProgress.ScaleX = progress.Frac;
		}, Dispatcher);
		timer.Start();
		try
		{
			progress = (Phase: "Medindo latência da RAM…", Frac: 0.05);
			double lat = await Task.Run(() => MemoryBench.MeasureLatencyNs(ct));
			ct.ThrowIfCancellationRequested();
			rows.Add(new MemRow("Latência da RAM", $"{lat:0.0} ns", "Acesso aleatório. Menor é melhor; pesa muito em jogos competitivos."));
			Show();
			progress = (Phase: "Medindo banda da RAM…", Frac: 0.2);
			(double Read, double Copy) bw = await Task.Run(() => MemoryBench.MeasureBandwidth(ct));
			ct.ThrowIfCancellationRequested();
			rows.Add(new MemRow("Banda de leitura da RAM", $"{bw.Read:0.0} GB/s", "Todas as threads lendo em sequência."));
			rows.Add(new MemRow("Banda de cópia da RAM", $"{bw.Copy:0.0} GB/s", "Leitura + escrita."));
			Show();
			LoadResult load = await Task.Run(() => MemoryBench.MeasureLoading((string phase, double f) =>
			{
				progress = (Phase: phase, Frac: 0.35 + f * 0.6);
			}, ct));
			rows.Add(new MemRow("SSD " + load.Drive + " · leitura sequencial", $"{load.SeqReadMBs:N0} MB/s", "Pacote de 512 MB, sem cache do Windows."));
			rows.Add(new MemRow("Assets pequenos", $"{load.FilesPerSec:N0} arq/s", $"Mil arquivos de 64 KB com 8 threads ({load.SmallFilesMBs:N0} MB/s), como engines modernas."));
			rows.Add(new MemRow("Carregamento do mapa", $"{load.MapLoadSeconds:0.00} s", "Pacote + assets: quanto menor, mais rápido o jogo carrega."));
			Show();
			MemStatus.Text = "Concluído · arquivos de teste apagados";
			SaveBenchRecord("mem", 1000.0 / lat + 10.0 / load.MapLoadSeconds, $"{lat:0} ns · {bw.Read:0} GB/s · mapa {load.MapLoadSeconds:0.00} s");
		}
		catch (OperationCanceledException)
		{
			MemStatus.Text = "Cancelado (arquivos de teste apagados)";
		}
		catch (Exception ex2)
		{
			MemStatus.Text = "Falhou: " + ex2.Message;
		}
		finally
		{
			timer.Stop();
			MemProgress.ScaleX = 0.0;
			SetGameBenchRunning(BtnMemBench, running: false);
		}
		void Show()
		{
			MemResult.ItemsSource = rows.ToList();
		}
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		nint handle = new WindowInteropHelper(this).Handle;
		HwndSource.FromHwnd(handle)?.AddHook(HotkeyHook);
		RegisterHotKey(handle, 21061, 16390u, 79u);
	}

	private nint HotkeyHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		if (msg == 786 && ((IntPtr)wParam).ToInt32() == 21061)
		{
			ChkOverlay.IsChecked = ChkOverlay.IsChecked != true;
			handled = true;
		}
		else if (msg == 537)
		{
			OnDeviceChange(((IntPtr)wParam).ToInt32(), lParam);
		}
		return IntPtr.Zero;
	}

	private void ChkOverlay_Changed(object sender, RoutedEventArgs e)
	{
		if (ChkOverlay.IsChecked == true)
		{
			if (_overlay == null)
			{
				_overlay = new OverlayWindow();
			}
			ApplyOverlayOptions();
			_overlay.Show();
			if (_overlayTimer == null)
			{
				_overlayTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500.0), DispatcherPriority.Normal, (object? _, EventArgs _) =>
				{
					UpdateOverlay();
				}, Dispatcher);
			}
			_overlayTimer.Start();
			UpdateOverlay();
		}
		else
		{
			_overlayTimer?.Stop();
			_overlay?.Hide();
		}
	}

	private void OvOption_Changed(object sender, RoutedEventArgs e)
	{
		if (_overlayReady)
		{
			_settings.OverlayCorner = CheckedTag(OvCornerPanel) ?? "TL";
			_settings.OverlayScale = double.Parse(CheckedTag(OvScalePanel) ?? "1", CultureInfo.InvariantCulture);
			_settings.Save();
			ApplyOverlayOptions();
		}
	}

	private void ApplyOverlayOptions()
	{
		if (_overlay != null)
		{
			_overlay.Corner = _settings.OverlayCorner;
			_overlay.SetScale(_settings.OverlayScale);
			_overlay.Reposition();
		}
	}

	private void UpdateOverlay()
	{
		FrameStats f = _frames.GetForegroundStats();
		if (_overlay != null && _overlay.IsVisible)
		{
			_overlay.Update(f, BuildOverlayLines(f), OvShow, OvShow.Contains("clock") ? DateTime.Now.ToString("HH:mm") : null);
			_overlay.KeepOnTop();
		}
		UpdateOverlayStatus(f);
	}

	private void UpdateOverlayStatus(FrameStats? f)
	{
		if (_frames.Error != null)
		{
			OvStatus.Text = "Detecção de FPS indisponível: " + _frames.Error;
		}
		else if (f != null)
		{
			OvStatus.Text = $"Ativa · {f.ProcessName}: {f.Fps:0} FPS ({f.FrameTimeMs:0.0} ms) · 1% low {f.Low1Fps:0}";
		}
		else
		{
			OvStatus.Text = "Ativa · aguardando um jogo em primeiro plano";
		}
	}

	[DllImport("user32.dll")]
	private static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);

	[DllImport("user32.dll")]
	private static extern bool UnregisterHotKey(nint hWnd, int id);

	private void AddChip(string label, string? category, bool isChecked = false)
	{
		System.Windows.Controls.RadioButton chip = new System.Windows.Controls.RadioButton
		{
			Content = label,
			GroupName = "Category",
			Style = (Style)FindResource("Chip"),
			IsChecked = isChecked
		};
		chip.Checked += (object _, RoutedEventArgs _) =>
		{
			_category = category;
			ApplySensorFilter();
		};
		CategoryChips.Children.Add(chip);
	}

	private void SensorSearch_Changed(object sender, TextChangedEventArgs e)
	{
		_search = SensorSearch.Text.Trim();
		ApplySensorFilter();
	}

	private void ApplySensorFilter()
	{
		if (_view == null)
		{
			return;
		}
		if (_category == null && _search.Length == 0 && HiddenSensorHw.Count == 0)
		{
			_view.Filter = null;
			return;
		}
		_view.Filter = (object o) =>
		{
			SensorRow sensorRow = (SensorRow)o;
			return !HiddenSensorHw.Contains(sensorRow.Hardware) && (_category == null || sensorRow.Category == _category) && (_search.Length == 0 || sensorRow.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) || sensorRow.Hardware.Contains(_search, StringComparison.OrdinalIgnoreCase));
		};
	}

	private void BuildHardwareFilterChips()
	{
		if (HardwareChips == null)
		{
			return;
		}
		HardwareChips.Children.Clear();
		List<(string Hardware, HardwareType Type)> hws = (from r in _monitor.Rows
			group r by r.Hardware into g
			select (Hardware: g.Key, Type: g.First().HardwareType)).ToList();
		foreach (var item in hws)
		{
			string hw = item.Hardware;
			string prefix = SensorRow.HardwareGroupPrefix(item.Type);
			System.Windows.Controls.CheckBox cb = new System.Windows.Controls.CheckBox
			{
				Content = $"{prefix}: {hw}",
				IsChecked = !HiddenSensorHw.Contains(hw),
				Margin = new Thickness(0.0, 0.0, 14.0, 6.0),
				VerticalAlignment = VerticalAlignment.Center
			};
			cb.Click += delegate
			{
				if (cb.IsChecked == true)
				{
					HiddenSensorHw.Remove(hw);
				}
				else
				{
					HiddenSensorHw.Add(hw);
				}
				SaveHiddenSensorHw();
			};
			HardwareChips.Children.Add(cb);
		}
	}

	private void SaveHiddenSensorHw()
	{
		_settings.HiddenSensorHardware = HiddenSensorHw.ToList();
		try
		{
			_settings.Save();
		}
		catch
		{
		}
		BuildHardwareFilterChips();
		ApplySensorFilter();
		if (_sensorWindow != null && _sensorWindow.IsLoaded)
		{
			_sensorWindow.BuildFilterChips();
			_sensorWindow.RebuildGroups();
		}
	}

	private void BtnSensorPresetCpuGpu_Click(object sender, RoutedEventArgs e)
	{
		HiddenSensorHw.Clear();
		foreach (SensorRow r in _monitor.Rows)
		{
			bool isCpuOrGpu = r.HardwareType == HardwareType.Cpu || r.HardwareType == HardwareType.GpuNvidia || r.HardwareType == HardwareType.GpuAmd || r.HardwareType == HardwareType.GpuIntel;
			if (!isCpuOrGpu)
			{
				HiddenSensorHw.Add(r.Hardware);
			}
		}
		SaveHiddenSensorHw();
	}

	private void BtnSensorPresetAll_Click(object sender, RoutedEventArgs e)
	{
		HiddenSensorHw.Clear();
		SaveHiddenSensorHw();
	}

	private void BtnSensorResetMinMax_Click(object sender, RoutedEventArgs e)
	{
		_monitor.ResetMinMax();
	}

	private void BtnOpenSensorMonitor_Click(object sender, RoutedEventArgs e)
	{
		if (_sensorWindow != null && _sensorWindow.IsLoaded)
		{
			if (_sensorWindow.WindowState == WindowState.Minimized)
			{
				_sensorWindow.WindowState = WindowState.Normal;
			}
			_sensorWindow.Activate();
			return;
		}
		_sensorWindow = new SensorMonitorWindow(_monitor, _settings, HiddenSensorHw, delegate
		{
			BuildHardwareFilterChips();
			ApplySensorFilter();
		});
		_sensorWindow.Closed += delegate
		{
			_sensorWindow = null;
		};
		_sensorWindow.Show();
		UpdateSensorWindowLive();
	}

	private void UpdateSensorWindowLive()
	{
		if (_sensorWindow != null && _sensorWindow.IsLoaded)
		{
			FrameStats? f = _frames.GetForegroundStats();
			float? gpuLoad = ((_gpus.Count > 0) ? _monitor.Rows.FirstOrDefault((SensorRow r) => r.Hardware == _gpus[0].Name && r.Type == SensorType.Load && r.Name == "GPU Core")?.RawValue : null);
			_sensorWindow.UpdateLiveTelemetry(f?.Fps, _monitor.CpuTemperature, _monitor.CpuLoad, MainGpuTemp(), gpuLoad);
		}
	}

	private void UnusedSensorAnchor()
	{
		_view.Filter = (object o) => true;
		if (false)
		{
		};
	}

	private void BtnStress_Click(object sender, RoutedEventArgs e)
	{
		if (TestRunning)
		{
			StopStress("Parado pelo usuário");
		}
		else
		{
			StartStress();
		}
	}

	private void ChkRam_Click(object sender, RoutedEventArgs e)
	{
		if (RamFractionPanel != null)
		{
			RamFractionPanel.IsEnabled = ChkRam.IsChecked == true;
		}
	}

	private void ChkGpu_Click(object sender, RoutedEventArgs e)
	{
		if (GpuOptions != null)
		{
			WrapPanel gpuOptions = GpuOptions;
			bool isEnabled = (GpuTempLimit.IsEnabled = ChkGpu.IsChecked == true);
			gpuOptions.IsEnabled = isEnabled;
		}
		if (PageStress != null)
		{
			DrawChart();
		}
	}

	private static string? CheckedTag(System.Windows.Controls.Panel panel)
	{
		return panel.Children.OfType<System.Windows.Controls.RadioButton>().FirstOrDefault((System.Windows.Controls.RadioButton r) => r.IsChecked == true)?.Tag as string;
	}

	private void StartStress()
	{
		StressHint.Text = "";
		bool cpu = ChkCpu.IsChecked == true;
		bool ram = ChkRam.IsChecked == true;
		bool gpu = ChkGpu.IsChecked == true;
		if (!cpu && !ram && !gpu)
		{
			StressHint.Text = "Ative CPU, RAM ou GPU.";
			return;
		}
		if (!double.TryParse(TempLimit.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out _tempLimit) || _tempLimit < 40.0 || _tempLimit > 110.0)
		{
			StressHint.Text = "Limite da CPU deve estar entre 40 e 110 °C.";
			return;
		}
		if (gpu && (!double.TryParse(GpuTempLimit.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out _gpuTempLimit) || _gpuTempLimit < 40.0 || _gpuTempLimit > 100.0))
		{
			StressHint.Text = "Limite da GPU deve estar entre 40 e 100 °C.";
			return;
		}
		if (!double.TryParse(TxtDuration.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var minutes) || minutes < 0.0)
		{
			StressHint.Text = "Duração inválida.";
			return;
		}
		if ((cpu | ram) && !_monitor.CpuTemperature.HasValue)
		{
			string why = (SensorMonitor.IsPawnIOInstalled ? "" : "\n\nCausa provável: o driver PawnIO não está instalado. Instale com:\nwinget install namazso.PawnIO\ne reabra o Triarch.");
			if (System.Windows.MessageBox.Show("Nenhum sensor de temperatura da CPU foi encontrado, então o corte automático por temperatura da CPU NÃO funcionará. Continuar mesmo assim?" + why, "Triarch", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
			{
				return;
			}
		}
		if (gpu && !MainGpuTemp().HasValue && System.Windows.MessageBox.Show("Nenhum sensor de temperatura da GPU foi encontrado, então o corte automático por temperatura da GPU NÃO funcionará. Continuar mesmo assim?", "Triarch", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			return;
		}
		double fraction = double.Parse(CheckedTag(RamFractionPanel) ?? "0.6", CultureInfo.InvariantCulture);
		_stressDuration = TimeSpan.FromMinutes(minutes);
		_stressStart = DateTime.Now;
		_stressMaxTemp = 0f;
		_gpuMaxTemp = 0f;
		_tempHistory.Clear();
		_gpuTempHistory.Clear();
		if (cpu | ram)
		{
			_stress.Start(cpu, ram, fraction);
		}
		string gpuDesc = "não";
		if (gpu)
		{
			string res = CheckedTag(GpuResPanel) ?? "1920x1080";
			int msaa = int.Parse(CheckedTag(GpuMsaaPanel) ?? "4");
			int[] intensity = (CheckedTag(GpuIntensityPanel) ?? "128,48").Split(',').Select(int.Parse).ToArray();
			bool full = res == "full";
			int[] size = (full ? new int[2] : res.Split('x').Select(int.Parse).ToArray());
			_burn.Start(new GpuBurnSettings
			{
				Fullscreen = full,
				Width = size[0],
				Height = size[1],
				Msaa = msaa,
				Shells = intensity[0],
				Iterations = intensity[1],
				AdapterName = ((_gpus.Count > 0) ? _gpus[0].Name : "")
			});
			_gpuBurnOn = true;
			gpuDesc = $"sim ({(full ? "tela cheia" : res)}, {((msaa > 1) ? $"MSAA {msaa}x" : "sem MSAA")}, {GpuIntensityPanel.Children.OfType<System.Windows.Controls.RadioButton>().First((System.Windows.Controls.RadioButton r) => r.IsChecked == true).Content}, limite {_gpuTempLimit:0} °C)";
		}
		SetStressControls(running: true);
		SetStatus("Rodando", alert: false);
		Log($"Iniciado · CPU {(cpu ? $"sim (limite {_tempLimit:0} °C)" : "não")} · RAM {(ram ? $"sim ({fraction:P0} da livre)" : "não")} · GPU {gpuDesc} · {((minutes > 0.0) ? $"{minutes:0.##} min" : "sem limite de tempo")}");
	}

	private void StopStress(string reason)
	{
		if (TestRunning)
		{
			AddEvent("\ue945", "Stress test", reason, reason.StartsWith("ABORTADO") ? "abortado" : "ok");
		}
		if (TestRunning)
		{
			long errors = _stress.MemoryErrors;
			long passes = _stress.MemoryPasses;
			bool hadRam = _stress.MemoryAllocatedBytes > 0;
			bool hadCpu = _stress.Running;
			_stress.Stop();
			string gpuSummary = "";
			if (_gpuBurnOn)
			{
				_burn.Stop();
				_gpuBurnOn = false;
				gpuSummary = $" · GPU: {_burn.Frames:N0} frames, média {_burn.AverageFps:0} FPS, temp. máx. {_gpuMaxTemp:0.#} °C";
			}
			SetStressControls(running: false);
			bool aborted = reason.StartsWith("ABORTADO");
			SetStatus(aborted ? "Abortado" : "Parado", aborted);
			Log($"{reason} · duração {DateTime.Now - _stressStart:hh\\:mm\\:ss}" + (hadCpu ? $" · CPU temp. máx. {_stressMaxTemp:0.#} °C" : "") + (hadRam ? $" · RAM {passes} passes, {errors} erros" : "") + gpuSummary);
		}
	}

	private void SetStressControls(bool running)
	{
		BtnStress.Content = (running ? "PARAR" : "INICIAR");
		BtnStress.Style = (Style)FindResource(running ? "OutlineButton" : "PrimaryButton");
		System.Windows.Controls.CheckBox chkCpu = ChkCpu;
		System.Windows.Controls.CheckBox chkRam = ChkRam;
		bool flag = (ChkGpu.IsEnabled = !running);
		bool isEnabled = (chkRam.IsEnabled = flag);
		chkCpu.IsEnabled = isEnabled;
		RamFractionPanel.IsEnabled = !running && ChkRam.IsChecked == true;
		WrapPanel gpuOptions = GpuOptions;
		isEnabled = (GpuTempLimit.IsEnabled = !running && ChkGpu.IsChecked == true);
		gpuOptions.IsEnabled = isEnabled;
		System.Windows.Controls.TextBox tempLimit = TempLimit;
		isEnabled = (TxtDuration.IsEnabled = !running);
		tempLimit.IsEnabled = isEnabled;
		SideStress.Text = (running ? "rodando" : "parado");
		TopStressBadge.Visibility = ((!running) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void SetStatus(string text, bool alert)
	{
		StStatus.Text = text;
		StatusCard.Background = (alert ? Fg : Card);
		StStatus.Foreground = (alert ? Ink : Fg);
		StStatusLabel.Foreground = (alert ? Ink : Muted);
	}

	private float? MainGpuTemp()
	{
		if (_gpus.Count == 0)
		{
			return null;
		}
		List<SensorRow> temps = _monitor.Rows.Where((SensorRow r) => r.Hardware == _gpus[0].Name && r.Type == SensorType.Temperature).ToList();
		return (temps.FirstOrDefault((SensorRow r) => r.Name == "GPU Core") ?? temps.FirstOrDefault())?.RawValue;
	}

	private float? MainGpuPower()
	{
		if (_gpus.Count == 0)
		{
			return null;
		}
		GpuInfo g = _gpus[0];
		if (g.NvmlHandle != IntPtr.Zero)
		{
			uint? num = Nvml.PowerUsageMw(g.NvmlHandle);
			if (num.HasValue)
			{
				uint mw = num.GetValueOrDefault();
				return (float)mw / 1000f;
			}
		}
		return _monitor.Rows.FirstOrDefault((SensorRow r) => r.Hardware == g.Name && r.Type == SensorType.Power)?.RawValue;
	}

	private string MainGpuPerfCap()
	{
		if (_gpus.Count == 0)
		{
			return "";
		}
		return _gpus[0].LiveExtras().FirstOrDefault((KeyValuePair<string, string> kv) => kv.Key.StartsWith("PerfCap")).Value ?? "";
	}

	private void UpdateStressPanel()
	{
		float? temp = _monitor.CpuTemperature;
		float? load = _monitor.CpuLoad;
		float? power = _monitor.CpuPower;
		float? gTemp = MainGpuTemp();
		float? gPower = MainGpuPower();
		if (temp.HasValue)
		{
			float t = temp.GetValueOrDefault();
			_tempHistory.Add(t);
			if (_tempHistory.Count > 300)
			{
				_tempHistory.RemoveAt(0);
			}
			if (_stress.Running && t > _stressMaxTemp)
			{
				_stressMaxTemp = t;
			}
		}
		if (gTemp.HasValue)
		{
			float gt = gTemp.GetValueOrDefault();
			_gpuTempHistory.Add(gt);
			if (_gpuTempHistory.Count > 300)
			{
				_gpuTempHistory.RemoveAt(0);
			}
			if (_gpuBurnOn && gt > _gpuMaxTemp)
			{
				_gpuMaxTemp = gt;
			}
		}
		TextBlock stTemp = StTemp;
		TextBlock sideTemp = SideTemp;
		string text = (TopTemp.Text = ((!temp.HasValue) ? "—" : $"{temp:0.#} °C"));
		string text3 = (sideTemp.Text = text);
		stTemp.Text = text3;
		StTempMax.Text = ((_stressMaxTemp > 0f) ? $"máx. {_stressMaxTemp:0.#} °C" : "máx. —");
		TextBlock sideLoad = SideLoad;
		text3 = (TopLoad.Text = ((!load.HasValue) ? "—" : $"{load:0} %"));
		sideLoad.Text = text3;
		StLoad.Text = ((!load.HasValue) ? "carga —" : $"carga {load:0} %");
		StPower.Text = ((!power.HasValue) ? "—" : $"{power:0.#} W");
		StGpuTemp.Text = ((!gTemp.HasValue) ? "—" : $"{gTemp:0.#} °C");
		StGpuTempMax.Text = ((_gpuMaxTemp > 0f) ? $"máx. {_gpuMaxTemp:0.#} °C" : "máx. —");
		StGpuPower.Text = ((!gPower.HasValue) ? "—" : $"{gPower:0.#} W");
		string cap = MainGpuPerfCap();
		StGpuCap.Text = ((cap.Length > 0) ? ("limitador: " + cap) : "—");
		StFps.Text = ((_gpuBurnOn && _burn.Fps > 0.0) ? $"{_burn.Fps:0}" : "—");
		StFpsAvg.Text = ((_gpuBurnOn && _burn.AverageFps > 0.0) ? $"média {_burn.AverageFps:0}" : "média —");
		if (TestRunning)
		{
			TimeSpan elapsed = DateTime.Now - _stressStart;
			StElapsed.Text = elapsed.ToString("hh\\:mm\\:ss");
			StRam.Text = ((_stress.MemoryAllocatedBytes > 0) ? $"{_stress.MemoryPasses} / {_stress.MemoryErrors}" : "—");
			if (_stress.Running && (double?)temp >= _tempLimit)
			{
				StopStress($"ABORTADO: CPU a {temp:0.#} °C atingiu o limite de {_tempLimit:0} °C");
			}
			else if (_gpuBurnOn && (double?)gTemp >= _gpuTempLimit)
			{
				StopStress($"ABORTADO: GPU a {gTemp:0.#} °C atingiu o limite de {_gpuTempLimit:0} °C");
			}
			else if (_stress.MemoryErrors > 0)
			{
				StopStress($"ABORTADO: {_stress.MemoryErrors} erros de memória detectados");
			}
			else if (_gpuBurnOn && !_burn.Running)
			{
				StopStress((_burn.Error != null) ? ("ABORTADO: falha na GPU (" + _burn.Error + ")") : "Janela da GPU fechada pelo usuário");
			}
			else if (_stressDuration > TimeSpan.Zero && elapsed >= _stressDuration)
			{
				StopStress("Concluído (tempo esgotado)");
			}
		}
		if (PageStress.IsVisible)
		{
			DrawChart();
		}
	}

	private void Log(string msg)
	{
		StressLog.Items.Add($"{DateTime.Now:HH:mm:ss}  {msg}");
		System.Windows.Controls.ListBox stressLog = StressLog;
		ItemCollection items = StressLog.Items;
		stressLog.ScrollIntoView(items[items.Count - 1]);
	}

	private void TempChart_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		DrawChart();
	}

	private void DrawChart()
	{
		TempChart.Children.Clear();
		double w = TempChart.ActualWidth;
		double h = TempChart.ActualHeight;
		double plotH;
		double yMin;
		double yMax;
		double step;
		SolidColorBrush accentBrush;
		if (!(w < 50.0) && !(h < 40.0))
		{
			double plotW = w - 36.0;
			plotH = h - 6.0 - 6.0;
			yMin = 20.0;
			yMax = Math.Max(110.0, Math.Max(_tempLimit, _gpuTempLimit) + 10.0);
			System.Windows.Media.Brush gridBrush = (System.Windows.Media.Brush)FindResource("Line");
			for (double v = yMin; v <= yMax; v += 20.0)
			{
				TempChart.Children.Add(new Line
				{
					X1 = 36.0,
					X2 = w,
					Y1 = Y(v),
					Y2 = Y(v),
					Stroke = gridBrush
				});
				TextBlock lbl = new TextBlock
				{
					Text = $"{v:0}°",
					FontSize = 10.0,
					Foreground = (System.Windows.Media.Brush)FindResource("Faint"),
					FontFamily = (System.Windows.Media.FontFamily)FindResource("Mono")
				};
				Canvas.SetLeft(lbl, 0.0);
				Canvas.SetTop(lbl, Y(v) - 7.0);
				TempChart.Children.Add(lbl);
			}
			LimitLine(_tempLimit, Muted);
			if (ChkGpu.IsChecked == true)
			{
				LimitLine(_gpuTempLimit, (System.Windows.Media.Brush)FindResource("Faint"));
			}
			step = plotW / 299.0;
			accentBrush = (SolidColorBrush)FindResource("Accent");
			Series(_gpuTempHistory, Muted, fill: false);
			Series(_tempHistory, accentBrush, fill: true);
		}
		void LimitLine(double v2, System.Windows.Media.Brush b)
		{
			TempChart.Children.Add(new Line
			{
				X1 = 36.0,
				X2 = w,
				Y1 = Y(v2),
				Y2 = Y(v2),
				Stroke = b,
				StrokeThickness = 1.0,
				StrokeDashArray = new DoubleCollection { 4.0, 3.0 }
			});
		}
		void Series(List<double> data, System.Windows.Media.Brush stroke, bool fill)
		{
			if (data.Count >= 2)
			{
				int offset = 300 - data.Count;
				Polyline line = new Polyline
				{
					Stroke = stroke,
					StrokeThickness = 1.8,
					StrokeLineJoin = PenLineJoin.Round
				};
				for (int i = 0; i < data.Count; i++)
				{
					line.Points.Add(new System.Windows.Point(36.0 + (double)(offset + i) * step, Y(data[i])));
				}
				if (fill)
				{
					Polygon area = new Polygon
					{
						Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(36, accentBrush.Color.R, accentBrush.Color.G, accentBrush.Color.B))
					};
					foreach (System.Windows.Point p in line.Points)
					{
						area.Points.Add(p);
					}
					PointCollection points = area.Points;
					PointCollection points2 = line.Points;
					points.Add(new System.Windows.Point(points2[points2.Count - 1].X, 6.0 + plotH));
					area.Points.Add(new System.Windows.Point(line.Points[0].X, 6.0 + plotH));
					TempChart.Children.Add(area);
				}
				TempChart.Children.Add(line);
			}
		}
		double Y(double value)
		{
			return 6.0 + plotH * (1.0 - (Math.Clamp(value, yMin, yMax) - yMin) / (yMax - yMin));
		}
	}
}
