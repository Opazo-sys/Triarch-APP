using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Triarch;

public partial class OverlayWindow : Window, IComponentConnector
{
	private struct RECT
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private struct POINT
	{
		public int X;

		public int Y;
	}

	private struct MONITORINFO
	{
		public int cbSize;

		public RECT rcMonitor;

		public RECT rcWork;

		public uint dwFlags;
	}

	private delegate void WinEventProc(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time);

	private nint _hwnd;

	private nint _hook;

	private nint _monitor;

	private readonly WinEventProc _onForeground;

	private const int GWL_EXSTYLE = -20;

	private const int WS_EX_TRANSPARENT = 32;

	private const int WS_EX_LAYERED = 524288;

	private const int WS_EX_TOOLWINDOW = 128;

	private const int WS_EX_NOACTIVATE = 134217728;

	private static readonly nint HWND_TOPMOST = new IntPtr(-1);

	private const uint SWP_NOSIZE = 1u;

	private const uint SWP_NOACTIVATE = 16u;

	private const uint SWP_NOOWNERZORDER = 512u;

	private const uint EVENT_SYSTEM_FOREGROUND = 3u;

	private const uint WINEVENT_OUTOFCONTEXT = 0u;

	private const uint MONITOR_DEFAULTTOPRIMARY = 1u;

	private const uint MONITOR_DEFAULTTONEAREST = 2u;

	public string Corner { get; set; } = "TL";

	public OverlayWindow()
	{
		InitializeComponent();
		_onForeground = (nint _, uint _, nint hwnd, int _, int _, uint _, uint _) =>
		{
			TrackGame(hwnd);
			KeepOnTop();
		};
		SourceInitialized += (object? _, EventArgs _) =>
		{
			_hwnd = new WindowInteropHelper(this).Handle;
			int windowLong = GetWindowLong(_hwnd, -20);
			SetWindowLong(_hwnd, -20, windowLong | 0x20 | 0x80000 | 0x80 | 0x8000000);
		};
		SizeChanged += (object _, SizeChangedEventArgs _) =>
		{
			Reposition();
		};
		IsVisibleChanged += (object _, DependencyPropertyChangedEventArgs _) =>
		{
			if (IsVisible && _hook == IntPtr.Zero)
			{
				_hook = SetWinEventHook(3u, 3u, IntPtr.Zero, _onForeground, 0u, 0u, 0u);
				TrackGame(GetForegroundWindow());
				KeepOnTop();
			}
			else if (!IsVisible)
			{
				Unhook();
			}
		};
		Closed += (object? _, EventArgs _) =>
		{
			Unhook();
		};
	}

	public void SetScale(double s)
	{
		ScaleTransform scale = Scale;
		double scaleX = (Scale.ScaleY = s);
		scale.ScaleX = scaleX;
	}

	public void Reposition()
	{
		KeepOnTop();
	}

	public void KeepOnTop()
	{
		if (_hwnd != IntPtr.Zero && IsVisible)
		{
			if (_monitor == IntPtr.Zero)
			{
				_monitor = MonitorFromPoint(default, 1u);
			}
			MONITORINFO mi = new MONITORINFO
			{
				cbSize = Marshal.SizeOf<MONITORINFO>()
			};
			if (GetMonitorInfo(_monitor, ref mi) && GetWindowRect(_hwnd, out var wr))
			{
				int w = wr.Right - wr.Left;
				int h = wr.Bottom - wr.Top;
				int m = (int)Math.Round(12.0 * VisualTreeHelper.GetDpi(this).DpiScaleX);
				RECT r = mi.rcMonitor;
				int x = (Corner.EndsWith("R") ? (r.Right - w - m) : (r.Left + m));
				int y = (Corner.StartsWith("B") ? (r.Bottom - h - m) : (r.Top + m));
				SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, 529u);
			}
		}
	}

	private void TrackGame(nint fg)
	{
		if (fg != IntPtr.Zero)
		{
			GetWindowThreadProcessId(fg, out var pid);
			if (pid != (uint)Environment.ProcessId)
			{
				_monitor = MonitorFromWindow(fg, 2u);
			}
		}
	}

	private void Unhook()
	{
		if (_hook != IntPtr.Zero)
		{
			UnhookWinEvent(_hook);
			_hook = IntPtr.Zero;
		}
	}

	public void Update(FrameStats? f, IReadOnlyList<OverlayLine> lines, IReadOnlySet<string> show, string? extra)
	{
		bool fps = show.Contains("fps");
		bool ft = show.Contains("frametime");
		OFpsRow.Visibility = V(fps | ft);
		TextBlock oFps = OFps;
		Visibility visibility = (OFpsUnit.Visibility = V(fps));
		oFps.Visibility = visibility;
		OFrameTime.Visibility = V(ft);
		OFrameTime.Margin = new Thickness(fps ? 12 : 0, 0.0, 0.0, fps ? 4 : 0);
		OLow.Visibility = V(show.Contains("low1"));
		OGraph.Visibility = V(show.Contains("graph"));
		if (f != null)
		{
			OFps.Text = $"{f.Fps:0}";
			OFrameTime.Text = $"{f.FrameTimeMs:0.0} ms";
			OLow.Text = $"1% low {f.Low1Fps:0}";
		}
		else
		{
			OFps.Text = "—";
			OFrameTime.Text = (fps ? "" : "— ms");
			OLow.Text = "aguardando jogo";
		}
		List<string> foot = new List<string>();
		if (show.Contains("game") && f != null)
		{
			foot.Add(f.ProcessName);
		}
		if (extra != null)
		{
			foot.Add(extra);
		}
		OGame.Text = string.Join("  ·  ", foot);
		OGame.Visibility = V(foot.Count > 0);
		ORows.ItemsSource = lines;
		ORows.Visibility = V(lines.Count > 0);
		if (OGraph.Visibility == Visibility.Visible)
		{
			DrawGraph(f?.RecentFrameTimes);
		}
		static Visibility V(bool on)
		{
			if (!on)
			{
				return Visibility.Collapsed;
			}
			return Visibility.Visible;
		}
	}

	private void DrawGraph(IReadOnlyList<double>? ft)
	{
		OGraph.Children.Clear();
		double w = OGraph.Width;
		double h = OGraph.Height;
		OGraph.Children.Add(new Line
		{
			X1 = 0.0,
			X2 = w,
			Y1 = h - 0.5,
			Y2 = h - 0.5,
			Stroke = new SolidColorBrush(Color.FromArgb(48, byte.MaxValue, byte.MaxValue, byte.MaxValue))
		});
		if (ft != null && ft.Count >= 2)
		{
			double max = Math.Max(ft.Max(), ft.Average() * 2.0);
			Polyline line = new Polyline
			{
				Stroke = Brushes.White,
				StrokeThickness = 1.0
			};
			double step = w / (double)(ft.Count - 1);
			for (int i = 0; i < ft.Count; i++)
			{
				line.Points.Add(new Point((double)i * step, h - 1.0 - (h - 2.0) * Math.Min(1.0, ft[i] / max)));
			}
			OGraph.Children.Add(line);
		}
	}

	[DllImport("user32.dll")]
	private static extern int GetWindowLong(nint h, int index);

	[DllImport("user32.dll")]
	private static extern int SetWindowLong(nint h, int index, int value);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(nint h, out RECT r);

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint h, out uint pid);

	[DllImport("user32.dll")]
	private static extern nint MonitorFromWindow(nint h, uint flags);

	[DllImport("user32.dll")]
	private static extern nint MonitorFromPoint(POINT p, uint flags);

	[DllImport("user32.dll")]
	private static extern bool GetMonitorInfo(nint mon, ref MONITORINFO mi);

	[DllImport("user32.dll")]
	private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint pid, uint thread, uint flags);

	[DllImport("user32.dll")]
	private static extern bool UnhookWinEvent(nint hook);
}
