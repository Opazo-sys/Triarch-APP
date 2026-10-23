using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Triarch;

public static class WindowsTweaks
{
	private const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

	private const string DxPath = "Software\\Microsoft\\DirectX\\UserGpuPreferences";

	public static List<Tweak> All()
	{
		return new List<Tweak>
		{
			new Tweak
			{
				Id = "power",
				Title = "Plano de energia: Alto desempenho",
				Description = "Mantém a CPU pronta para subir o clock na hora, sem economia agressiva de energia.",
				Effect = "Menos engasgos e 1% low melhor em alguns jogos. Em notebooks gasta mais bateria.",
				Read = () => ActiveScheme()?.Equals("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", StringComparison.OrdinalIgnoreCase),
				Write = (bool on, AppSettings s) =>
				{
					if (!s.TweakBackup.ContainsKey("power"))
					{
						s.TweakBackup["power"] = ActiveScheme();
					}
					if (on)
					{
						if (!PowerCfg("/list").Contains("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", StringComparison.OrdinalIgnoreCase))
						{
							PowerCfg("-duplicatescheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
						}
						PowerCfg("/setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
					}
					else
					{
						string text = s.TweakBackup["power"];
						if (text != null && !text.Equals("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", StringComparison.OrdinalIgnoreCase))
						{
							PowerCfg("/setactive " + text);
						}
						else
						{
							PowerCfg("/setactive 381b4222-f694-41f0-9685-ff5bb260df2e");
						}
					}
				},
				Restore = (AppSettings s) =>
				{
					string valueOrDefault = s.TweakBackup.GetValueOrDefault("power");
					if (valueOrDefault != null)
					{
						PowerCfg("/setactive " + valueOrDefault);
					}
				}
			},
			RegTweak("gamemode", "Modo de Jogo", "Dá prioridade ao jogo e evita que o Windows Update instale ou reinicie durante a partida.", "Recomendado ligado (é o padrão do Windows 11).", Registry.CurrentUser, "Software\\Microsoft\\GameBar", "AutoGameModeEnabled", 1, 0, absentMeansOn: true),
			new Tweak
			{
				Id = "gamedvr",
				Title = "Desativar gravação em segundo plano (Game DVR)",
				Description = "Desliga a captura contínua do Xbox Game Bar, que grava os últimos minutos de jogo o tempo todo.",
				Effect = "Recupera alguns FPS e reduz uso de GPU/disco. Você perde o \"gravar o que aconteceu\" do Game Bar.",
				Read = () => (ReadDword(Registry.CurrentUser, "System\\GameConfigStore", "GameDVR_Enabled") ?? 1) == 0 && (ReadDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR", "AppCaptureEnabled") ?? 1) == 0,
				Write = (bool on, AppSettings s) =>
				{
					SetDword(s, "gamedvr.a", Registry.CurrentUser, "System\\GameConfigStore", "GameDVR_Enabled", (!on) ? 1 : 0);
					SetDword(s, "gamedvr.b", Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR", "AppCaptureEnabled", (!on) ? 1 : 0);
				},
				Restore = (AppSettings s) =>
				{
					RestoreDword(s, "gamedvr.a", Registry.CurrentUser, "System\\GameConfigStore", "GameDVR_Enabled");
					RestoreDword(s, "gamedvr.b", Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR", "AppCaptureEnabled");
				}
			},
			RegTweak("hags", "Agendamento de GPU acelerado por hardware", "A própria placa de vídeo gerencia sua fila de trabalho, em vez da CPU.", "Necessário para o Frame Generation do DLSS 3/4. Pode reduzir latência. Requer GPU e driver compatíveis.", Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers", "HwSchMode", 2, 1, absentMeansOn: false, needsRestart: true),
			new Tweak
			{
				Id = "windowed",
				Title = "Otimizações para jogos em janela",
				Description = "Faz jogos DirectX 10/11 em janela ou \"sem bordas\" usarem o modelo de apresentação moderno (flip).",
				Effect = "Menor latência e suporte a VRR/G-Sync em janela. Recurso do Windows 11.",
				Read = () => DxSettings().GetValueOrDefault("SwapEffectUpgradeEnable") == "1",
				Write = (bool on, AppSettings s) =>
				{
					if (!s.TweakBackup.ContainsKey("windowed"))
					{
						s.TweakBackup["windowed"] = (Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\DirectX\\UserGpuPreferences")?.GetValue("DirectXUserGlobalSettings") as string) ?? "<ausente>";
					}
					Dictionary<string, string> dictionary = DxSettings();
					dictionary["SwapEffectUpgradeEnable"] = (on ? "1" : "0");
					using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\DirectX\\UserGpuPreferences");
					registryKey.SetValue("DirectXUserGlobalSettings", string.Concat(dictionary.Select((KeyValuePair<string, string> kv) => kv.Key + "=" + kv.Value + ";")));
				},
				Restore = (AppSettings s) =>
				{
					string valueOrDefault = s.TweakBackup.GetValueOrDefault("windowed");
					if (valueOrDefault == null)
					{
						return;
					}
					using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\DirectX\\UserGpuPreferences");
					if (valueOrDefault == "<ausente>")
					{
						registryKey.DeleteValue("DirectXUserGlobalSettings", throwOnMissingValue: false);
					}
					else
					{
						registryKey.SetValue("DirectXUserGlobalSettings", valueOrDefault);
					}
				}
			},
			new Tweak
			{
				Id = "mouse",
				Title = "Desligar aceleração do mouse",
				Description = "Desmarca \"Aprimorar precisão do ponteiro\": o cursor anda sempre a mesma distância para o mesmo movimento.",
				Effect = "Não muda FPS, mas deixa a mira consistente em jogos de tiro.",
				Read = () => ReadString(Registry.CurrentUser, "Control Panel\\Mouse", "MouseSpeed") == "0",
				Write = (bool on, AppSettings s) =>
				{
					string[] array = new string[3] { "MouseSpeed", "MouseThreshold1", "MouseThreshold2" };
					foreach (string text in array)
					{
						if (!s.TweakBackup.ContainsKey("mouse." + text))
						{
							s.TweakBackup["mouse." + text] = ReadString(Registry.CurrentUser, "Control Panel\\Mouse", text);
						}
					}
					ApplyMouse((!on) ? new string[3] { "1", "6", "10" } : new string[3] { "0", "0", "0" });
				},
				Restore = (AppSettings s) =>
				{
					if (s.TweakBackup.ContainsKey("mouse.MouseSpeed"))
					{
						ApplyMouse(new string[3] { "MouseSpeed", "MouseThreshold1", "MouseThreshold2" }.Select((string n) => s.TweakBackup.GetValueOrDefault("mouse." + n) ?? "0").ToArray());
					}
				}
			}
		};
	}

	public static void RestoreAll(IEnumerable<Tweak> tweaks, AppSettings s)
	{
		foreach (Tweak t in tweaks)
		{
			try
			{
				t.Restore(s);
			}
			catch
			{
			}
		}
		s.TweakBackup.Clear();
		s.Save();
	}

	private static Tweak RegTweak(string id, string title, string desc, string effect, RegistryKey hive, string path, string name, int on, int off, bool absentMeansOn, bool needsRestart = false)
	{
		return new Tweak
		{
			Id = id,
			Title = title,
			Description = desc,
			Effect = effect,
			NeedsRestart = needsRestart,
			Read = () =>
			{
				int? num = ReadDword(hive, path, name);
				bool value;
				if (num.HasValue)
				{
					int valueOrDefault = num.GetValueOrDefault();
					value = valueOrDefault == on;
				}
				else
				{
					value = absentMeansOn;
				}
				return value;
			},
			Write = (bool enable, AppSettings s) =>
			{
				SetDword(s, id, hive, path, name, enable ? on : off);
			},
			Restore = (AppSettings s) =>
			{
				RestoreDword(s, id, hive, path, name);
			}
		};
	}

	private static int? ReadDword(RegistryKey hive, string path, string name)
	{
		using RegistryKey k = hive.OpenSubKey(path);
		return (k?.GetValue(name) is int v) ? new int?(v) : ((int?)null);
	}

	private static string? ReadString(RegistryKey hive, string path, string name)
	{
		using RegistryKey k = hive.OpenSubKey(path);
		return k?.GetValue(name)?.ToString();
	}

	private static void SetDword(AppSettings s, string backupKey, RegistryKey hive, string path, string name, int value)
	{
		if (!s.TweakBackup.ContainsKey(backupKey))
		{
			s.TweakBackup[backupKey] = ReadDword(hive, path, name)?.ToString() ?? "<ausente>";
		}
		using RegistryKey k = hive.CreateSubKey(path);
		k.SetValue(name, value, RegistryValueKind.DWord);
	}

	private static void RestoreDword(AppSettings s, string backupKey, RegistryKey hive, string path, string name)
	{
		string prev = s.TweakBackup.GetValueOrDefault(backupKey);
		if (prev == null)
		{
			return;
		}
		using RegistryKey k = hive.CreateSubKey(path);
		if (prev == "<ausente>")
		{
			k.DeleteValue(name, throwOnMissingValue: false);
		}
		else
		{
			k.SetValue(name, int.Parse(prev), RegistryValueKind.DWord);
		}
	}

	private static Dictionary<string, string> DxSettings()
	{
		Dictionary<string, string> d = new Dictionary<string, string>();
		string[] array = ((Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\DirectX\\UserGpuPreferences")?.GetValue("DirectXUserGlobalSettings") as string) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string[] kv = array[i].Split('=', 2);
			if (kv.Length == 2)
			{
				d[kv[0].Trim()] = kv[1].Trim();
			}
		}
		return d;
	}

	private static void ApplyMouse(string[] v)
	{
		using (RegistryKey k = Registry.CurrentUser.CreateSubKey("Control Panel\\Mouse"))
		{
			k.SetValue("MouseSpeed", v[0]);
			k.SetValue("MouseThreshold1", v[1]);
			k.SetValue("MouseThreshold2", v[2]);
		}
		int[] p = v.Select(int.Parse).ToArray();
		SystemParametersInfo(4u, 0u, p, 3u);
	}

	private static string? ActiveScheme()
	{
		Match m = Regex.Match(PowerCfg("/getactivescheme"), "[0-9a-fA-F\\-]{36}");
		if (!m.Success)
		{
			return null;
		}
		return m.Value.ToLowerInvariant();
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

	[DllImport("user32.dll")]
	private static extern bool SystemParametersInfo(uint action, uint param, int[] data, uint winIni);
}
