using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;

namespace Triarch;

public partial class App : Application
{
	private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

	private static Mutex? _single;

	private static EventWaitHandle? _activateEvent;

	public const string ActivateEventName = "Local\\Triarch.ActivateEvent";

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern nint FindWindow(string? cls, string title);

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(nint h, int cmd);

	[DllImport("user32.dll")]
	private static extern bool IsIconic(nint h);

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(nint h);

	protected override void OnStartup(StartupEventArgs e)
	{
		bool startInTray = false;
		foreach (string arg in e.Args)
		{
			if (string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "-tray", StringComparison.OrdinalIgnoreCase))
			{
				startInTray = true;
				break;
			}
		}
		if (startInTray && !StartupManager.IsAppAutoStartEnabled())
		{
			StartupManager.DeleteScheduledTask();
			Shutdown();
			return;
		}
		bool first;
		try
		{
			_single = new Mutex(initiallyOwned: true, "Local\\Triarch.SingleInstance", out first);
		}
		catch (UnauthorizedAccessException)
		{
			first = false;
		}
		if (!first)
		{
			try
			{
				using EventWaitHandle evt = EventWaitHandle.OpenExisting(ActivateEventName);
				evt.Set();
			}
			catch
			{
			}
			nint h = FindWindow(null, "Triarch");
			if (h == IntPtr.Zero)
			{
				EnumWindows(delegate(nint wnd, nint _)
				{
					StringBuilder sb = new StringBuilder(256);
					if (GetWindowText(wnd, sb, sb.Capacity) > 0)
					{
						string title = sb.ToString();
						if (title == "Triarch" || title.StartsWith("Triarch · ", StringComparison.Ordinal))
						{
							h = wnd;
							return false;
						}
					}
					return true;
				}, IntPtr.Zero);
			}
			if (h != IntPtr.Zero)
			{
				if (IsIconic(h))
				{
					ShowWindow(h, 9);
				}
				else
				{
					ShowWindow(h, 5);
				}
				SetForegroundWindow(h);
			}
			Shutdown();
		}
		else
		{
			base.OnStartup(e);
			ShutdownMode = ShutdownMode.OnExplicitShutdown;
			StartupManager.EnsureAutoStartSynced();
			try
			{
				_activateEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ActivateEventName);
			}
			catch
			{
			}
			MainWindow main = new MainWindow(startInTray, _activateEvent);
			MainWindow = main;
			if (!startInTray)
			{
				main.Show();
			}
		}
	}
}
