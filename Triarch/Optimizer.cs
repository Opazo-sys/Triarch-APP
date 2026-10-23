using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Triarch;

public static class Optimizer
{
	private struct MEMORYSTATUSEX
	{
		public uint dwLength;

		public uint dwMemoryLoad;

		public ulong ullTotalPhys;

		public ulong ullAvailPhys;

		public ulong ullTotalPageFile;

		public ulong ullAvailPageFile;

		public ulong ullTotalVirtual;

		public ulong ullAvailVirtual;

		public ulong ullAvailExtendedVirtual;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	private struct TOKEN_PRIVILEGES
	{
		public uint PrivilegeCount;

		public long Luid;

		public uint Attributes;
	}

	private static readonly HashSet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
		"svchost", "dwm", "explorer", "fontdrvhost", "sihost", "ctfmon", "taskhostw", "RuntimeBroker", "StartMenuExperienceHost", "ShellExperienceHost",
		"SearchHost", "SearchIndexer", "TextInputHost", "SecurityHealthService", "SecurityHealthSystray", "MsMpEng", "NisSrv", "SgrmBroker", "audiodg", "conhost",
		"dllhost", "spoolsv", "WmiPrvSE", "LockApp", "ApplicationFrameHost", "SystemSettings", "smartscreen", "MemCompression", "Secure System", "WUDFHost",
		"dasHost", "nvcontainer", "NVDisplay.Container", "atiesrxx", "atieclxx", "amdfendrsr", "RadeonSoftware", "igfxEM", "PawnIO", "Triarch",
		"cmd", "powershell", "WindowsTerminal", "OpenConsole", "devenv", "Code", "dotnet"
	};

	private static readonly Dictionary<string, string> SuggestedApps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["OneDrive"] = "Sincronização do OneDrive (pode usar disco e rede)",
		["ms-teams"] = "Microsoft Teams",
		["Teams"] = "Microsoft Teams (clássico)",
		["Skype"] = "Skype",
		["PhoneExperienceHost"] = "Vincular ao Celular",
		["YourPhone"] = "Vincular ao Celular",
		["Widgets"] = "Widgets do Windows",
		["WidgetService"] = "Serviço de Widgets",
		["AdobeARM"] = "Atualizador da Adobe",
		["CCXProcess"] = "Adobe Creative Cloud",
		["Creative Cloud"] = "Adobe Creative Cloud",
		["AdobeIPCBroker"] = "Adobe (comunicação em segundo plano)",
		["GoogleDriveFS"] = "Google Drive",
		["Dropbox"] = "Dropbox",
		["Cortana"] = "Cortana",
		["OfficeClickToRun"] = "Atualizador do Office"
	};

	public static List<BackgroundApp> ListApps()
	{
		int session = Process.GetCurrentProcess().SessionId;
		Dictionary<string, List<Process>> groups = new Dictionary<string, List<Process>>(StringComparer.OrdinalIgnoreCase);
		Process[] processes = Process.GetProcesses();
		foreach (Process p in processes)
		{
			try
			{
				if (p.SessionId == session && !Protected.Contains(p.ProcessName))
				{
					if (!groups.TryGetValue(p.ProcessName, out var list))
					{
						list = (groups[p.ProcessName] = new List<Process>());
					}
					list.Add(p);
				}
			}
			catch
			{
			}
		}
		List<BackgroundApp> result = new List<BackgroundApp>();
		foreach (var (name, procs) in groups)
		{
			bool hasWindow = procs.Any((Process p2) => SafeWindow(p2) != IntPtr.Zero);
			bool suggested = SuggestedApps.ContainsKey(name);
			if (!hasWindow && !suggested)
			{
				continue;
			}
			string title = name;
			try
			{
				string d = procs[0].MainModule?.FileVersionInfo.FileDescription;
				title = ((d != null && d.Length > 0) ? d : name);
			}
			catch
			{
			}
			string detail;
			if (suggested)
			{
				detail = SuggestedApps[name];
			}
			else
			{
				detail = (hasWindow ? "Programa aberto" : "");
			}
			if (procs.Count > 1)
			{
				detail += $" · {procs.Count} processos";
			}
			long mem = procs.Sum((Process process) =>
			{
				try
				{
					return process.WorkingSet64;
				}
				catch
				{
					return 0L;
				}
			});
			result.Add(new BackgroundApp
			{
				ProcessName = name,
				Title = title,
				Detail = detail,
				MemoryBytes = mem,
				Count = procs.Count,
				HasWindow = hasWindow,
				Suggested = suggested,
				Selected = suggested
			});
		}
		return (from a in result
			orderby a.Suggested descending, a.MemoryBytes descending
			select a).ToList();
	}

	private static nint SafeWindow(Process p)
	{
		try
		{
			return p.MainWindowHandle;
		}
		catch
		{
			return IntPtr.Zero;
		}
	}

	public static CloseResult CloseApps(IEnumerable<BackgroundApp> apps)
	{
		List<string> closed = new List<string>();
		List<string> notClosed = new List<string>();
		foreach (BackgroundApp app in apps)
		{
			Process[] processesByName = Process.GetProcessesByName(app.ProcessName);
			foreach (Process p in processesByName)
			{
				try
				{
					if (SafeWindow(p) != IntPtr.Zero)
					{
						p.CloseMainWindow();
					}
					else if (app.Suggested)
					{
						p.Kill();
					}
				}
				catch
				{
				}
			}
			Stopwatch sw = Stopwatch.StartNew();
			while (sw.Elapsed.TotalSeconds < 4.0 && Process.GetProcessesByName(app.ProcessName).Length != 0)
			{
				Thread.Sleep(200);
			}
			if (Process.GetProcessesByName(app.ProcessName).Length == 0)
			{
				closed.Add(app.Title);
			}
			else
			{
				notClosed.Add(app.Title);
			}
		}
		return new CloseResult(closed, notClosed);
	}

	public static CleanupResult CleanMemory()
	{
		long before = AvailableBytes();
		int trimmed = 0;
		int self = Environment.ProcessId;
		Process[] processes = Process.GetProcesses();
		foreach (Process p in processes)
		{
			try
			{
				if (p.Id != self && p.Id > 4 && EmptyWorkingSet(p.Handle))
				{
					trimmed++;
				}
			}
			catch
			{
			}
		}
		bool purged = false;
		string error = null;
		try
		{
			EnablePrivilege("SeProfileSingleProcessPrivilege");
			int command = 4;
			int status = NtSetSystemInformation(80, ref command, 4);
			purged = status == 0;
			if (!purged)
			{
				error = $"o Windows recusou (0x{status:X8}); é preciso rodar como administrador";
			}
		}
		catch (Exception ex)
		{
			error = ex.Message;
		}
		Thread.Sleep(300);
		return new CleanupResult(Math.Max(0L, AvailableBytes() - before), trimmed, purged, error);
	}

	public static (long Total, long Available) MemoryStatus()
	{
		MEMORYSTATUSEX m = new MEMORYSTATUSEX
		{
			dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
		};
		if (!GlobalMemoryStatusEx(ref m))
		{
			return (Total: 0L, Available: 0L);
		}
		return (Total: (long)m.ullTotalPhys, Available: (long)m.ullAvailPhys);
	}

	private static long AvailableBytes()
	{
		return MemoryStatus().Available;
	}

	private static void EnablePrivilege(string name)
	{
		if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 40u, out var token))
		{
			throw new Win32Exception();
		}
		try
		{
			if (!LookupPrivilegeValue(null, name, out var luid))
			{
				throw new Win32Exception();
			}
			TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES
			{
				PrivilegeCount = 1u,
				Luid = luid,
				Attributes = 2u
			};
			AdjustTokenPrivileges(token, disableAll: false, ref tp, 0u, IntPtr.Zero, IntPtr.Zero);
		}
		finally
		{
			CloseHandle(token);
		}
	}

	[DllImport("psapi.dll")]
	private static extern bool EmptyWorkingSet(nint hProcess);

	[DllImport("ntdll.dll")]
	private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

	[DllImport("kernel32.dll")]
	private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

	[DllImport("kernel32.dll")]
	private static extern bool CloseHandle(nint h);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool OpenProcessToken(nint process, uint access, out nint token);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool AdjustTokenPrivileges(nint token, bool disableAll, ref TOKEN_PRIVILEGES state, uint len, nint prev, nint retLen);
}
