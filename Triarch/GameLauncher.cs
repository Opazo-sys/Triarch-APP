using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Triarch;

public static class GameLauncher
{
	private const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

	private static readonly string[] IgnoredExe = new string[14]
	{
		"unins", "crash", "redist", "vc_", "vcredist", "dxsetup", "dotnet", "easyanticheat", "beservice", "cef",
		"setup", "uninstall", "launcher_helper", "notification"
	};

	public static string BuildArgs(GameProfile p)
	{
		List<string> a = new List<string>();
		int w = 0;
		int h = 0;
		Match m = Regex.Match(p.Res ?? "", "^(\\d+)x(\\d+)$");
		if (m.Success)
		{
			w = int.Parse(m.Groups[1].Value);
			h = int.Parse(m.Groups[2].Value);
		}
		string style = p.Style;
		if (!(style == "unity"))
		{
			if (style == "unreal")
			{
				if (p.WinMode == "full")
				{
					a.Add("-fullscreen");
				}
				else
				{
					string winMode = p.WinMode;
					if ((winMode == "window" || winMode == "borderless") ? true : false)
					{
						a.Add("-windowed");
					}
				}
				if (w > 0)
				{
					a.Add($"-ResX={w} -ResY={h}");
				}
			}
			else
			{
				if (p.WinMode == "full")
				{
					a.Add("-fullscreen");
				}
				else if (p.WinMode == "window")
				{
					a.Add("-windowed");
				}
				else if (p.WinMode == "borderless")
				{
					a.Add("-windowed -noborder");
				}
				if (w > 0)
				{
					a.Add($"-w {w} -h {h}");
				}
				if (p.Hz > 0)
				{
					a.Add($"-freq {p.Hz}");
				}
			}
		}
		else
		{
			if (p.WinMode == "full")
			{
				a.Add("-screen-fullscreen 1");
			}
			else if (p.WinMode == "window")
			{
				a.Add("-screen-fullscreen 0");
			}
			else if (p.WinMode == "borderless")
			{
				a.Add("-popupwindow");
			}
			if (w > 0)
			{
				a.Add($"-screen-width {w} -screen-height {h}");
			}
		}
		if (!string.IsNullOrWhiteSpace(p.Extra))
		{
			a.Add(p.Extra.Trim());
		}
		return string.Join(" ", a);
	}

	public static string? FindMainExe(string dir, string gameName)
	{
		try
		{
			if (!Directory.Exists(dir))
			{
				return null;
			}
			List<FileInfo> exes = (from f in new DirectoryInfo(dir).EnumerateFiles("*.exe", new EnumerationOptions
				{
					RecurseSubdirectories = true,
					MaxRecursionDepth = 4,
					IgnoreInaccessible = true
				})
				where !IgnoredExe.Any((string i) => f.Name.Contains(i, StringComparison.OrdinalIgnoreCase))
				select f).ToList();
			if (exes.Count == 0)
			{
				return null;
			}
			string norm = Regex.Replace(gameName.ToLowerInvariant(), "[^a-z0-9]", "");
			return ((from f in exes.Where((FileInfo f) =>
				{
					string text = Regex.Replace(Path.GetFileNameWithoutExtension(f.Name).ToLowerInvariant(), "[^a-z0-9]", "");
					return text.Length >= 3 && (norm.Contains(text) || text.Contains(norm));
				})
				orderby f.Length descending
				select f).FirstOrDefault() ?? exes.OrderByDescending((FileInfo f) => f.Length).First()).FullName;
		}
		catch
		{
			return null;
		}
	}

	public static void SetGpuPreference(string exe, bool high)
	{
		using RegistryKey k = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\DirectX\\UserGpuPreferences");
		if (high)
		{
			k.SetValue(exe, "GpuPreference=2;", RegistryValueKind.String);
		}
		else if (k.GetValue(exe) is string v && v.StartsWith("GpuPreference=2", StringComparison.Ordinal))
		{
			k.DeleteValue(exe, throwOnMissingValue: false);
		}
	}

	public static void SetDisableFso(string exe, bool disable)
	{
		using RegistryKey k = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers");
		List<string> tokens = (from t in ((k.GetValue(exe) as string) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
			where t != "~"
			select t).ToList();
		if (disable && !tokens.Contains("DISABLEDXMAXIMIZEDWINDOWEDMODE"))
		{
			tokens.Add("DISABLEDXMAXIMIZEDWINDOWEDMODE");
		}
		if (!disable)
		{
			tokens.Remove("DISABLEDXMAXIMIZEDWINDOWEDMODE");
		}
		if (tokens.Count == 0)
		{
			k.DeleteValue(exe, throwOnMissingValue: false);
		}
		else
		{
			k.SetValue(exe, "~ " + string.Join(" ", tokens), RegistryValueKind.String);
		}
	}

	private static string PowerCfg(string args)
	{
		using Process p = Process.Start(new ProcessStartInfo("powercfg.exe", args)
		{
			RedirectStandardOutput = true,
			UseShellExecute = false,
			CreateNoWindow = true
		});
		string result = p.StandardOutput.ReadToEnd();
		p.WaitForExit(5000);
		return result;
	}

	private static string? ActiveScheme()
	{
		Match m = Regex.Match(PowerCfg("/getactivescheme"), "[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
		if (!m.Success)
		{
			return null;
		}
		return m.Value;
	}

	private static Process? FindGameProcess(string dir, string? exe)
	{
		Process found = null;
		Process[] processes = Process.GetProcesses();
		foreach (Process pr in processes)
		{
			bool keep = false;
			try
			{
				string path = pr.MainModule?.FileName;
				if (found == null && path != null && ((exe != null && path.Equals(exe, StringComparison.OrdinalIgnoreCase)) || path.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
				{
					found = pr;
					keep = true;
				}
			}
			catch
			{
			}
			if (!keep)
			{
				pr.Dispose();
			}
		}
		return found;
	}

	public static async Task RunAsync(LibGame g, GameProfile p, Action<string> status, Action<string> learnedExe, CancellationToken ct)
	{
		string oldPlan = null;
		List<(Process Proc, bool Close)> helpers = new List<(Process, bool)>();
		try
		{
			status("Aplicando o perfil…");
			if (p.HighPerfPlan)
			{
				oldPlan = ActiveScheme();
				if (!string.Equals(oldPlan, "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", StringComparison.OrdinalIgnoreCase))
				{
					if (!PowerCfg("/list").Contains("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", StringComparison.OrdinalIgnoreCase))
					{
						PowerCfg("-duplicatescheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
					}
					PowerCfg("/setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
				}
				else
				{
					oldPlan = null;
				}
			}
			string exe = p.ExePath ?? g.ExePath ?? FindMainExe(g.InstallDir, g.Name);
			if (exe != null && File.Exists(exe))
			{
				try
				{
					SetGpuPreference(exe, p.GpuHigh);
				}
				catch
				{
				}
				try
				{
					SetDisableFso(exe, p.DisableFso);
				}
				catch
				{
				}
			}
			foreach (HelperProgram h in p.Helpers.Where((HelperProgram helperProgram) => File.Exists(helperProgram.Path)))
			{
				status("Abrindo " + Path.GetFileName(h.Path) + "…");
				try
				{
					Process proc = Process.Start(new ProcessStartInfo(h.Path, h.Args)
					{
						UseShellExecute = true,
						WorkingDirectory = (Path.GetDirectoryName(h.Path) ?? "")
					});
					if (proc != null)
					{
						helpers.Add((proc, h.CloseWithGame));
					}
				}
				catch
				{
				}
			}
			if (helpers.Count > 0)
			{
				await Task.Delay(1500, ct);
			}
			status("Iniciando " + g.Name + "…");
			string args = (g.Args + " " + BuildArgs(p)).Trim();
			if (g.IsSteam)
			{
				Process.Start(new ProcessStartInfo("steam://rungameid/" + g.Id)
				{
					UseShellExecute = true
				});
			}
			else if (g.LaunchUri != null)
			{
				Process.Start(new ProcessStartInfo(g.LaunchUri)
				{
					UseShellExecute = true
				});
			}
			else
			{
				if (exe == null)
				{
					throw new InvalidOperationException("Não achei o executável deste jogo. Escolha o .exe na seção Desempenho.");
				}
				if (args.Length == 0)
				{
					Process.Start("explorer.exe", "\"" + exe + "\"");
				}
				else
				{
					Process.Start(new ProcessStartInfo(exe, args)
					{
						UseShellExecute = true,
						WorkingDirectory = (Path.GetDirectoryName(exe) ?? "")
					});
				}
			}
			Process game = null;
			for (int i = 0; i < 120; i++)
			{
				if (game != null)
				{
					break;
				}
				await Task.Delay(1500, ct);
				game = FindGameProcess(g.InstallDir, p.ExePath ?? g.ExePath);
			}
			if (game == null)
			{
				status("Não detectei o processo do jogo (ele pode ter demorado a abrir). Os ajustes continuam aplicados.");
				return;
			}
			while (game != null)
			{
				try
				{
					string real = game.MainModule?.FileName;
					if (real != null)
					{
						learnedExe(real);
					}
					Process process = game;
					string priority = p.Priority;
					ProcessPriorityClass priorityClass;
					if (priority == "high")
					{
						priorityClass = ProcessPriorityClass.High;
					}
					else
					{
						priorityClass = ((!(priority == "above")) ? ProcessPriorityClass.Normal : ProcessPriorityClass.AboveNormal);
					}
					process.PriorityClass = priorityClass;
				}
				catch
				{
				}
				status(g.Name + " em execução" + ((p.Priority != "normal") ? (" · prioridade " + ((p.Priority == "high") ? "alta" : "acima do normal")) : "") + ".");
				try
				{
					await game.WaitForExitAsync(ct);
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch
				{
				}
				game.Dispose();
				game = null;
				for (int i = 0; i < 6; i++)
				{
					if (game != null)
					{
						break;
					}
					await Task.Delay(1000, ct);
					game = FindGameProcess(g.InstallDir, p.ExePath ?? g.ExePath);
				}
			}
			status(g.Name + " encerrado. Ajustes temporários desfeitos.");
		}
		catch (OperationCanceledException)
		{
			status("Acompanhamento cancelado.");
		}
		catch (Exception ex3)
		{
			status("Falhou: " + ex3.Message);
		}
		finally
		{
			if (oldPlan != null)
			{
				try
				{
					PowerCfg("/setactive " + oldPlan);
				}
				catch
				{
				}
			}
			foreach (var item in helpers)
			{
				var (proc2, _) = item;
				if (!item.Close)
				{
					continue;
				}
				try
				{
					if (!proc2.HasExited)
					{
						proc2.CloseMainWindow();
						if (!proc2.WaitForExit(1500))
						{
							proc2.Kill();
						}
					}
				}
				catch
				{
				}
			}
		}
	}
}
