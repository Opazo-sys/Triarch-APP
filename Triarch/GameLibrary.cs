using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace Triarch;

public static class GameLibrary
{
	public static List<LibGame> Scan(IEnumerable<CustomGame> custom)
	{
		List<LibGame> all = new List<LibGame>();
		try
		{
			all.AddRange(Steam());
		}
		catch
		{
		}
		try
		{
			all.AddRange(Epic());
		}
		catch
		{
		}
		try
		{
			all.AddRange(Gog());
		}
		catch
		{
		}
		try
		{
			all.AddRange(Ubisoft());
		}
		catch
		{
		}
		foreach (CustomGame c in custom)
		{
			if (File.Exists(c.Exe))
			{
				all.Add(new LibGame
				{
					Store = "Manual",
					Id = c.Exe,
					Name = (string.IsNullOrWhiteSpace(c.Name) ? Path.GetFileNameWithoutExtension(c.Exe) : c.Name),
					InstallDir = (Path.GetDirectoryName(c.Exe) ?? ""),
					ExePath = c.Exe,
					Args = c.Args
				});
			}
		}
		return all.OrderBy((LibGame g) => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	private static IEnumerable<LibGame> Steam()
	{
		string steam = SteamInfo.SteamPath();
		if (steam == null)
		{
			yield break;
		}
		foreach (string lib in SteamInfo.LibraryFolders(steam))
		{
			string apps = Path.Combine(lib, "steamapps");
			if (!Directory.Exists(apps))
			{
				continue;
			}
			foreach (string acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
			{
				LibGame g = null;
				try
				{
					VdfNode st = Vdf.Parse(File.ReadAllText(acf)).Child("AppState");
					if (st == null)
					{
						continue;
					}
					string id = st.Get("appid");
					string name = st.Get("name");
					string dir = st.Get("installdir");
					if (id != null && name != null && dir != null && (!long.TryParse(st.Get("StateFlags"), out var flags) || (flags & 4) != 0L) && !(id == "228980") && !name.StartsWith("Steamworks Common", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase))
					{
						string full = Path.Combine(apps, "common", dir);
						if (Directory.Exists(full))
						{
							g = new LibGame
							{
								Store = "Steam",
								Id = id,
								Name = name,
								InstallDir = full
							};
							goto IL_01ba;
						}
					}
				}
				catch
				{
					goto IL_01ba;
				}
				continue;
				IL_01ba:
				if (g != null)
				{
					yield return g;
				}
			}
		}
	}

	private static IEnumerable<LibGame> Epic()
	{
		string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
		if (!Directory.Exists(dir))
		{
			yield break;
		}
		foreach (string f in Directory.EnumerateFiles(dir, "*.item"))
		{
			LibGame g = null;
			try
			{
				using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(f));
				JsonElement r = doc.RootElement;
				string name = S("DisplayName");
				string loc = S("InstallLocation");
				string app = S("AppName");
				string ns = S("CatalogNamespace");
				string cat = S("CatalogItemId");
				string exe = S("LaunchExecutable");
				if (name == null || loc == null || app == null || !Directory.Exists(loc))
				{
					continue;
				}
				string exePath = ((!string.IsNullOrEmpty(exe)) ? Path.Combine(loc, exe) : null);
				g = new LibGame
				{
					Store = "Epic Games",
					Id = app,
					Name = name,
					InstallDir = loc,
					ExePath = ((exePath != null && File.Exists(exePath)) ? exePath : null),
					LaunchUri = ((ns != null && cat != null) ? $"com.epicgames.launcher://apps/{ns}%3A{cat}%3A{app}?action=launch&silent=true" : null)
				};
				goto IL_0202;
				string? S(string k)
				{
					if (!r.TryGetProperty(k, out var v) || v.ValueKind != JsonValueKind.String)
					{
						return null;
					}
					return v.GetString();
				}
			}
			catch
			{
				goto IL_0202;
			}
			IL_0202:
			if (g != null)
			{
				yield return g;
			}
		}
	}

	private static IEnumerable<LibGame> Gog()
	{
		using RegistryKey root = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\GOG.com\\Games");
		if (root == null)
		{
			yield break;
		}
		string[] subKeyNames = root.GetSubKeyNames();
		foreach (string sub in subKeyNames)
		{
			LibGame g = null;
			try
			{
				using RegistryKey k = root.OpenSubKey(sub);
				string name = k?.GetValue("gameName") as string;
				string path = k?.GetValue("path") as string;
				string exe = k?.GetValue("exe") as string;
				if (name == null || path == null || !Directory.Exists(path))
				{
					continue;
				}
				g = new LibGame
				{
					Store = "GOG",
					Id = sub,
					Name = name,
					InstallDir = path,
					ExePath = ((exe != null && File.Exists(exe)) ? exe : null),
					Args = ((k?.GetValue("launchParam") as string) ?? "")
				};
				goto IL_0164;
			}
			catch
			{
				goto IL_0164;
			}
			IL_0164:
			if (g != null)
			{
				yield return g;
			}
		}
	}

	private static IEnumerable<LibGame> Ubisoft()
	{
		using RegistryKey root = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Ubisoft\\Launcher\\Installs");
		if (root == null)
		{
			yield break;
		}
		string[] subKeyNames = root.GetSubKeyNames();
		foreach (string sub in subKeyNames)
		{
			LibGame g = null;
			try
			{
				using RegistryKey k = root.OpenSubKey(sub);
				string dir = (k?.GetValue("InstallDir") as string)?.Replace('/', '\\').TrimEnd('\\');
				if (dir == null || !Directory.Exists(dir))
				{
					continue;
				}
				g = new LibGame
				{
					Store = "Ubisoft Connect",
					Id = sub,
					Name = Path.GetFileName(dir),
					InstallDir = dir,
					LaunchUri = "uplay://launch/" + sub + "/0"
				};
				goto IL_011c;
			}
			catch
			{
				goto IL_011c;
			}
			IL_011c:
			if (g != null)
			{
				yield return g;
			}
		}
	}
}
