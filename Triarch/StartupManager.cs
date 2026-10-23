using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace Triarch;

public static class StartupManager
{
	public const string AppRunValueName = "Triarch";

	public const string ScheduledTaskName = "TriarchAutoStart";

	private const string RunPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string ApprovedPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\";

	public static string BackupFolder => Path.Combine(AppSettings.Folder, "Startup");

	public static bool IsAppAutoStartEnabled()
	{
		try
		{
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: false);
			bool hasRunValue = !string.IsNullOrWhiteSpace(key?.GetValue("Triarch") as string);
			bool approved = IsApproved(machine: false, "Run", "Triarch");
			if (hasRunValue)
			{
				return approved;
			}
			return IsScheduledTaskPresent() && approved;
		}
		catch
		{
			return false;
		}
	}

	public static void EnsureAutoStartSynced()
	{
		try
		{
			if (IsAppAutoStartEnabled())
			{
				SetAppAutoStart(enable: true);
			}
		}
		catch
		{
		}
	}

	public static void SetAppAutoStart(bool enable)
	{
		using RegistryKey key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true) ?? throw new InvalidOperationException("Não foi possível acessar HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Run");
		if (enable)
		{
			string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(AppContext.BaseDirectory, "Triarch.exe");
			key.SetValue("Triarch", "\"" + exePath + "\" --tray", RegistryValueKind.String);
			try
			{
				using RegistryKey? ap = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run", writable: true);
				ap?.DeleteValue("Triarch", throwOnMissingValue: false);
			}
			catch
			{
			}
			CreateOrUpdateScheduledTask(exePath);
			return;
		}
		key.DeleteValue("Triarch", throwOnMissingValue: false);
		DeleteScheduledTask();
	}

	private static bool IsScheduledTaskPresent()
	{
		try
		{
			ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", "/Query /TN \"TriarchAutoStart\"")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			using Process? p = Process.Start(psi);
			if (p == null)
			{
				return false;
			}
			p.WaitForExit(4000);
			return p.ExitCode == 0;
		}
		catch
		{
			return false;
		}
	}

	private static void CreateOrUpdateScheduledTask(string exePath)
	{
		string xmlPath = Path.Combine(Path.GetTempPath(), "TriarchAutoStart-" + Guid.NewGuid().ToString("N") + ".xml");
		try
		{
			string userId = WindowsIdentity.GetCurrent().Name;
			string workDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
			string escUser = SecurityElement.Escape(userId) ?? userId;
			string escCmd = SecurityElement.Escape(exePath) ?? exePath;
			string escDir = SecurityElement.Escape(workDir) ?? workDir;
			string xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Inicia o Triarch em segundo plano na bandeja do sistema ao fazer login no Windows.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{escUser}</UserId>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{escUser}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{escCmd}</Command>
      <Arguments>--tray</Arguments>
      <WorkingDirectory>{escDir}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
			File.WriteAllText(xmlPath, xml, Encoding.Unicode);
			ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", $"/Create /TN \"{ScheduledTaskName}\" /XML \"{xmlPath}\" /F")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			using Process? p = Process.Start(psi);
			p?.WaitForExit(6000);
		}
		catch
		{
		}
		finally
		{
			try
			{
				if (File.Exists(xmlPath))
				{
					File.Delete(xmlPath);
				}
			}
			catch
			{
			}
		}
	}

	public static void DeleteScheduledTask()
	{
		try
		{
			ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", $"/Delete /TN \"{ScheduledTaskName}\" /F")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			using Process? p = Process.Start(psi);
			p?.WaitForExit(4000);
		}
		catch
		{
		}
	}

	public static List<StartupEntry> List()
	{
		List<StartupEntry> list = new List<StartupEntry>();
		ReadRun(list, RegistryHive.CurrentUser, RegistryView.Default, "Run", machine: false, "Registro · usuário");
		ReadRun(list, RegistryHive.LocalMachine, RegistryView.Registry64, "Run", machine: true, "Registro · todos os usuários");
		ReadRun(list, RegistryHive.LocalMachine, RegistryView.Registry32, "Run32", machine: true, "Registro · todos os usuários (32 bits)");
		ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), machine: false, "Pasta Inicializar · usuário");
		ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), machine: true, "Pasta Inicializar · todos os usuários");
		return list.OrderBy((StartupEntry e) => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	private static void ReadRun(List<StartupEntry> list, RegistryHive hive, RegistryView view, string kind, bool machine, string source)
	{
		try
		{
			using RegistryKey? key = RegistryKey.OpenBaseKey(hive, view).OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
			if (key == null)
			{
				return;
			}
			string[] valueNames = key.GetValueNames();
			foreach (string name in valueNames)
			{
				if (!string.IsNullOrEmpty(name))
				{
					string cmd = key.GetValue(name)?.ToString() ?? "";
					string? exe = ExeFromCommand(cmd);
					list.Add(new StartupEntry
					{
						Name = name,
						Command = cmd,
						Source = source,
						Machine = machine,
						Kind = kind,
						Enabled = IsApproved(machine, kind, name),
						Detail = Describe(exe),
						FileMissing = (exe != null && !File.Exists(exe))
					});
				}
			}
		}
		catch
		{
		}
	}

	private static void ReadFolder(List<StartupEntry> list, string folder, bool machine, string source)
	{
		try
		{
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				return;
			}
			string[] files = Directory.GetFiles(folder);
			foreach (string file in files)
			{
				string fn = Path.GetFileName(file);
				if (!fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
				{
					string? target = (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(file) : null);
					string? exe = ((target != null) ? ExeFromCommand(target) : file);
					list.Add(new StartupEntry
					{
						Name = Path.GetFileNameWithoutExtension(fn),
						Command = (target ?? file),
						Source = source,
						Machine = machine,
						Kind = "Folder",
						FilePath = file,
						Enabled = IsApproved(machine, "StartupFolder", fn),
						Detail = Describe(exe),
						FileMissing = (target != null && exe != null && !File.Exists(exe))
					});
				}
			}
		}
		catch
		{
		}
	}

	private static string ApprovedKind(StartupEntry e)
	{
		if (!(e.Kind == "Folder"))
		{
			return e.Kind;
		}
		return "StartupFolder";
	}

	private static string ApprovedName(StartupEntry e)
	{
		if (!(e.Kind == "Folder"))
		{
			return e.Name;
		}
		return Path.GetFileName(e.FilePath);
	}

	private static bool IsApproved(bool machine, string kind, string name)
	{
		try
		{
			using RegistryKey? key = (machine ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\" + kind);
			return !(key?.GetValue(name) is byte[] b) || b.Length <= 0 || (b[0] & 1) == 0;
		}
		catch
		{
			return true;
		}
	}

	public static void SetEnabled(StartupEntry e, bool enabled)
	{
		byte[] data = new byte[12]
		{
			(byte)(enabled ? 2 : 3),
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0
		};
		if (!enabled)
		{
			BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
		}
		using RegistryKey key = (e.Machine ? Registry.LocalMachine : Registry.CurrentUser).CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\" + ApprovedKind(e));
		key.SetValue(ApprovedName(e), data, RegistryValueKind.Binary);
		e.Enabled = enabled;
		if (!e.Machine && e.Name.Equals("Triarch", StringComparison.OrdinalIgnoreCase))
		{
			if (enabled)
			{
				string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(AppContext.BaseDirectory, "Triarch.exe");
				CreateOrUpdateScheduledTask(exePath);
			}
			else
			{
				DeleteScheduledTask();
			}
		}
	}

	public static StartupBackup Remove(StartupEntry e)
	{
		StartupBackup b;
		if (e.Kind == "Folder")
		{
			Directory.CreateDirectory(BackupFolder);
			string copy = Path.Combine(BackupFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Path.GetFileName(e.FilePath)}");
			File.Move(e.FilePath, copy);
			b = new StartupBackup
			{
				Kind = e.Kind,
				Machine = e.Machine,
				Name = e.Name,
				Backup = copy,
				Original = e.FilePath
			};
		}
		else
		{
			using RegistryKey key = RegistryKey.OpenBaseKey(e.Machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, (e.Kind == "Run32") ? RegistryView.Registry32 : (e.Machine ? RegistryView.Registry64 : RegistryView.Default)).OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true) ?? throw new InvalidOperationException("Chave Run não encontrada.");
			key.DeleteValue(e.Name);
			b = new StartupBackup
			{
				Kind = e.Kind,
				Machine = e.Machine,
				Name = e.Name,
				Command = e.Command
			};
		}
		try
		{
			using RegistryKey? ap = (e.Machine ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\" + ApprovedKind(e), writable: true);
			ap?.DeleteValue(ApprovedName(e), throwOnMissingValue: false);
		}
		catch
		{
		}
		if (!e.Machine && e.Name.Equals("Triarch", StringComparison.OrdinalIgnoreCase))
		{
			DeleteScheduledTask();
		}
		return b;
	}

	public static void Restore(StartupBackup b)
	{
		if (b.Kind == "Folder")
		{
			if (b.Backup != null && b.Original != null && File.Exists(b.Backup))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(b.Original)!);
				File.Move(b.Backup, b.Original, overwrite: true);
			}
			return;
		}
		using RegistryKey key = RegistryKey.OpenBaseKey(b.Machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, (b.Kind == "Run32") ? RegistryView.Registry32 : (b.Machine ? RegistryView.Registry64 : RegistryView.Default)).CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
		key.SetValue(b.Name, b.Command ?? "", RegistryValueKind.String);
	}

	public static string? ExeFromCommand(string cmd)
	{
		cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
		if (cmd.Length == 0)
		{
			return null;
		}
		string path;
		if (cmd[0] == '"')
		{
			int end = cmd.IndexOf('"', 1);
			if (end <= 1)
			{
				return null;
			}
			path = cmd.Substring(1, end - 1);
		}
		else
		{
			int exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
			int sp = cmd.IndexOf(' ');
			if (exe > 0)
			{
				path = cmd.Substring(0, exe + 4);
			}
			else
			{
				path = ((sp > 0) ? cmd.Substring(0, sp) : cmd);
			}
		}
		if (Path.IsPathRooted(path))
		{
			return path;
		}
		string sys = Path.Combine(Environment.SystemDirectory, path);
		if (!File.Exists(sys))
		{
			return null;
		}
		return sys;
	}

	private static string Describe(string? exe)
	{
		try
		{
			if (exe == null || !File.Exists(exe))
			{
				return "";
			}
			FileVersionInfo v = FileVersionInfo.GetVersionInfo(exe);
			return string.Join(" · ", (from s in new string?[2] { v.FileDescription, v.CompanyName }
				where !string.IsNullOrWhiteSpace(s)
				select s!.Trim()).Distinct());
		}
		catch
		{
			return "";
		}
	}

	private static string? ShortcutTarget(string lnk)
	{
		try
		{
			Type? type = Type.GetTypeFromProgID("WScript.Shell");
			if (type == null)
			{
				return null;
			}
			dynamic? shell = Activator.CreateInstance(type);
			if (shell == null)
			{
				return null;
			}
			dynamic sc = shell.CreateShortcut(lnk);
			string target = sc.TargetPath;
			string args = sc.Arguments;
			if (string.IsNullOrEmpty(target))
			{
				return null;
			}
			return string.IsNullOrEmpty(args) ? target : ("\"" + target + "\" " + args);
		}
		catch
		{
			return null;
		}
	}
}
