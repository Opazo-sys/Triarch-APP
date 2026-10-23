using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Triarch;

public static class SteamInfo
{
	public static bool IsRunning => Process.GetProcessesByName("steam").Length != 0;

	public static string? SteamPath()
	{
		try
		{
			string p = Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam")?.GetValue("SteamPath") as string;
			if (p == null)
			{
				p = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Valve\\Steam")?.GetValue("InstallPath") as string;
			}
			if (p != null)
			{
				p = Path.GetFullPath(p.Replace('/', '\\'));
			}
			if (p != null && Directory.Exists(p))
			{
				return p;
			}
		}
		catch
		{
		}
		string def = "C:\\Program Files (x86)\\Steam";
		if (!Directory.Exists(def))
		{
			return null;
		}
		return def;
	}

	public static List<string> LibraryFolders(string steam)
	{
		List<string> list = new List<string> { steam };
		try
		{
			string f = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
			if (File.Exists(f))
			{
				VdfNode root = Vdf.Parse(File.ReadAllText(f)).Child("libraryfolders");
				if (root != null)
				{
					foreach (KeyValuePair<string, VdfNode> child in root.Children)
					{
						child.Deconstruct(out var _, out var value);
						string p = value.Get("path");
						if (p != null && Directory.Exists(p.Replace('/', '\\')) && !list.Contains(p, StringComparer.OrdinalIgnoreCase))
						{
							list.Add(p.Replace('/', '\\'));
						}
					}
				}
			}
		}
		catch
		{
		}
		return list;
	}

	private static string? LocalConfig()
	{
		string steam = SteamPath();
		if (steam == null)
		{
			return null;
		}
		string ud = Path.Combine(steam, "userdata");
		if (!Directory.Exists(ud))
		{
			return null;
		}
		return (from d in Directory.GetDirectories(ud)
			select Path.Combine(d, "config", "localconfig.vdf")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
	}

	private static VdfNode? AppsNode(VdfNode root, bool create)
	{
		VdfNode n = root.Child("UserLocalConfigStore");
		string[] array = new string[4] { "Software", "Valve", "Steam", "apps" };
		foreach (string key in array)
		{
			if (n == null)
			{
				return null;
			}
			n = (create ? n.GetOrAddChild(key) : n.Child(key));
		}
		return n;
	}

	public static string? GetLaunchOptions(string appId)
	{
		try
		{
			string f = LocalConfig();
			if (f == null)
			{
				return null;
			}
			return AppsNode(Vdf.Parse(File.ReadAllText(f)), create: false)?.Child(appId)?.Get("LaunchOptions");
		}
		catch
		{
			return null;
		}
	}

	public static string? SetLaunchOptions(string appId, string options)
	{
		if (IsRunning)
		{
			return "A Steam está aberta. Feche-a para gravar as opções (ela sobrescreve o arquivo ao sair).";
		}
		string f = LocalConfig();
		if (f == null)
		{
			return "Não achei o localconfig.vdf da Steam (entre na Steam ao menos uma vez neste PC).";
		}
		try
		{
			VdfNode root = Vdf.Parse(File.ReadAllText(f));
			VdfNode app = AppsNode(root, create: true).GetOrAddChild(appId);
			if (string.IsNullOrWhiteSpace(options))
			{
				app.Remove("LaunchOptions");
			}
			else
			{
				app.Set("LaunchOptions", options.Trim());
			}
			File.Copy(f, f + ".triarch.bak", overwrite: true);
			File.WriteAllText(f, Vdf.Serialize(root), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			return null;
		}
		catch (Exception ex)
		{
			return "Não consegui gravar: " + ex.Message;
		}
	}

	public static async Task<bool> ShutdownAsync()
	{
		string steam = SteamPath();
		if (steam == null)
		{
			return false;
		}
		try
		{
			Process.Start(new ProcessStartInfo(Path.Combine(steam, "steam.exe"), "-shutdown")
			{
				UseShellExecute = true
			});
		}
		catch
		{
		}
		for (int i = 0; i < 50; i++)
		{
			if (!IsRunning)
			{
				break;
			}
			await Task.Delay(500);
		}
		return !IsRunning;
	}
}
