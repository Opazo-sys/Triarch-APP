using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace Triarch;

public sealed class FrameMonitor : IDisposable
{
	private const string SessionName = "Triarch-Frames";

	private static readonly Guid DxgiProvider = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");

	private static readonly Guid D3D9Provider = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");

	private const int DxgiPresentStart = 42;

	private const int D3D9PresentStart = 1;

	private const double HistoryMs = 10000.0;

	private TraceEventSession? _session;

	private Thread? _thread;

	private readonly object _lock = new object();

	private readonly Dictionary<int, Queue<double>> _presents = new Dictionary<int, Queue<double>>();

	private readonly Dictionary<int, long> _lastSeen = new Dictionary<int, long>();

	public bool Running => _thread?.IsAlive ?? false;

	public string? Error { get; private set; }

	public void Start()
	{
		if (Running)
		{
			return;
		}
		try
		{
			_session = new TraceEventSession("Triarch-Frames")
			{
				StopOnDispose = true
			};
			_session.EnableProvider(DxgiProvider, TraceEventLevel.Informational, ulong.MaxValue);
			_session.EnableProvider(D3D9Provider, TraceEventLevel.Informational, ulong.MaxValue);
			_session.Source.AllEvents += OnEvent;
			_thread = new Thread(() =>
			{
				try
				{
					_session.Source.Process();
				}
				catch (Exception ex2)
				{
					Error = ex2.Message;
				}
			})
			{
				IsBackground = true,
				Name = "ETW frames"
			};
			_thread.Start();
			Error = null;
		}
		catch (Exception ex)
		{
			Error = ex.Message;
			_session?.Dispose();
			_session = null;
		}
	}

	private void OnEvent(TraceEvent e)
	{
		int id = (int)e.ID;
		if ((!(e.ProviderGuid == DxgiProvider) || id != 42) && (!(e.ProviderGuid == D3D9Provider) || id != 1))
		{
			return;
		}
		double t = e.TimeStampRelativeMSec;
		lock (_lock)
		{
			if (!_presents.TryGetValue(e.ProcessID, out Queue<double> q))
			{
				q = (_presents[e.ProcessID] = new Queue<double>());
			}
			q.Enqueue(t);
			while (q.Count > 0 && q.Peek() < t - 10000.0)
			{
				q.Dequeue();
			}
			_lastSeen[e.ProcessID] = Stopwatch.GetTimestamp();
		}
	}

	public FrameStats? GetStats(int pid)
	{
		double[] ts;
		lock (_lock)
		{
			if (!_presents.TryGetValue(pid, out Queue<double> q) || q.Count < 3)
			{
				return null;
			}
			if (Stopwatch.GetElapsedTime(_lastSeen[pid]).TotalSeconds > 2.0)
			{
				return null;
			}
			ts = q.ToArray();
		}
		double last = ts[^1];
		List<double> frameTimes = new List<double>(ts.Length);
		for (int i = 1; i < ts.Length; i++)
		{
			frameTimes.Add(ts[i] - ts[i - 1]);
		}
		int first = Array.FindIndex(ts, (double x) => x >= last - 1000.0);
		double span = last - ts[first];
		int frames = ts.Length - 1 - first;
		double fps = ((span > 0.0) ? ((double)(frames * 1000) / span) : 0.0);
		double[] sorted = frameTimes.OrderBy((double x) => x).ToArray();
		double p99 = sorted[(int)Math.Min(sorted.Length - 1, Math.Floor((double)sorted.Length * 0.99))];
		double low1 = ((p99 > 0.0) ? (1000.0 / p99) : 0.0);
		List<double> recent = frameTimes.Skip(Math.Max(0, frameTimes.Count - 120)).ToList();
		return new FrameStats(pid, ProcessName(pid), fps, (fps > 0.0) ? (1000.0 / fps) : 0.0, low1, recent);
	}

	public FrameStats? GetForegroundStats()
	{
		nint hwnd = GetForegroundWindow();
		if (hwnd == IntPtr.Zero)
		{
			return null;
		}
		GetWindowThreadProcessId(hwnd, out var pid);
		if (pid != 0 && pid != Environment.ProcessId)
		{
			return GetStats((int)pid);
		}
		return null;
	}

	private static string ProcessName(int pid)
	{
		try
		{
			return Process.GetProcessById(pid).ProcessName;
		}
		catch
		{
			return $"PID {pid}";
		}
	}

	public void Dispose()
	{
		try
		{
			_session?.Source.StopProcessing();
		}
		catch
		{
		}
		_session?.Dispose();
		_session = null;
	}

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);
}
