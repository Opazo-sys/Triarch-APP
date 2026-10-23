using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Triarch;

public static class GameCatalog
{
	public static readonly GameInfo[] Games = new GameInfo[32]
	{
		new GameInfo("Counter-Strike 2", "FPS competitivo", 340.0, 420.0),
		new GameInfo("Valorant", "FPS competitivo", 600.0, 650.0),
		new GameInfo("Fortnite", "Battle royale", 160.0, 250.0),
		new GameInfo("League of Legends", "MOBA", 420.0, 480.0),
		new GameInfo("Dota 2", "MOBA", 260.0, 280.0),
		new GameInfo("Apex Legends", "Battle royale", 220.0, 300.0),
		new GameInfo("PUBG: Battlegrounds", "Battle royale", 180.0, 220.0),
		new GameInfo("Call of Duty: Black Ops 6", "FPS", 140.0, 220.0),
		new GameInfo("Call of Duty: Warzone", "Battle royale", 130.0, 200.0),
		new GameInfo("Battlefield 6", "FPS", 120.0, 170.0),
		new GameInfo("Rainbow Six Siege", "FPS tático", 350.0, 430.0),
		new GameInfo("Overwatch 2", "FPS heroico", 280.0, 380.0),
		new GameInfo("Marvel Rivals", "FPS heroico", 110.0, 160.0),
		new GameInfo("Rocket League", "Esporte", 400.0, 450.0),
		new GameInfo("EA Sports FC 25", "Esporte", 220.0, 260.0),
		new GameInfo("Minecraft", "Sandbox", 300.0, 340.0),
		new GameInfo("GTA V", "Mundo aberto", 160.0, 190.0),
		new GameInfo("Red Dead Redemption 2", "Mundo aberto", 95.0, 160.0),
		new GameInfo("Cyberpunk 2077", "RPG mundo aberto", 95.0, 150.0),
		new GameInfo("The Witcher 3: Wild Hunt", "RPG", 120.0, 160.0),
		new GameInfo("Elden Ring", "RPG de ação", 110.0, 140.0, 60.0),
		new GameInfo("Baldur's Gate 3", "RPG", 110.0, 120.0),
		new GameInfo("Hogwarts Legacy", "RPG de ação", 90.0, 120.0),
		new GameInfo("Starfield", "RPG espacial", 65.0, 90.0),
		new GameInfo("Black Myth: Wukong", "Ação", 70.0, 140.0),
		new GameInfo("Alan Wake 2", "Terror / ação", 60.0, 140.0),
		new GameInfo("God of War Ragnarök", "Ação", 110.0, 170.0),
		new GameInfo("Forza Horizon 5", "Corrida", 140.0, 200.0),
		new GameInfo("Helldivers 2", "Cooperativo", 90.0, 110.0),
		new GameInfo("Monster Hunter Wilds", "Ação", 60.0, 85.0),
		new GameInfo("Assassin's Creed Shadows", "Ação mundo aberto", 65.0, 110.0),
		new GameInfo("Borderlands 4", "Looter shooter", 55.0, 110.0)
	};

	private static readonly (string Key, double Index)[] GpuIndex = new (string, double)[43]
	{
		("RTX 5090", 265.0),
		("RTX 5080", 175.0),
		("RTX 5070 Ti", 150.0),
		("RTX 5070", 118.0),
		("RTX 5060 Ti", 95.0),
		("RTX 5060", 85.0),
		("RTX 4090", 210.0),
		("RTX 4080 SUPER", 160.0),
		("RTX 4080", 157.0),
		("RTX 4070 Ti SUPER", 136.0),
		("RTX 4070 Ti", 124.0),
		("RTX 4070 SUPER", 116.0),
		("RTX 4070", 100.0),
		("RTX 4060 Ti", 82.0),
		("RTX 4060", 70.0),
		("RTX 3090 Ti", 135.0),
		("RTX 3090", 125.0),
		("RTX 3080 Ti", 122.0),
		("RTX 3080", 112.0),
		("RTX 3070 Ti", 92.0),
		("RTX 3070", 86.0),
		("RTX 3060 Ti", 78.0),
		("RTX 3060", 60.0),
		("RTX 3050", 40.0),
		("GTX 1660", 38.0),
		("GTX 1650", 28.0),
		("RX 9070 XT", 160.0),
		("RX 9070", 140.0),
		("RX 7900 XTX", 170.0),
		("RX 7900 XT", 150.0),
		("RX 7900 GRE", 128.0),
		("RX 7800 XT", 115.0),
		("RX 7700 XT", 98.0),
		("RX 7600 XT", 72.0),
		("RX 7600", 70.0),
		("RX 6950 XT", 125.0),
		("RX 6800 XT", 112.0),
		("RX 6700 XT", 82.0),
		("RX 6600", 58.0),
		("Arc B580", 80.0),
		("Arc A770", 68.0),
		("Radeon(TM) Graphics", 6.0),
		("Radeon Graphics", 6.0)
	};

	private static readonly (string Key, double Index)[] CpuIndex = new (string, double)[36]
	{
		("9950X3D", 138.0),
		("9800X3D", 140.0),
		("7950X3D", 126.0),
		("7800X3D", 128.0),
		("5800X3D", 95.0),
		("5700X3D", 90.0),
		("9950X", 112.0),
		("9900X", 110.0),
		("9700X", 108.0),
		("9600X", 104.0),
		("7950X", 104.0),
		("7900X", 102.0),
		("7700X", 100.0),
		("7700", 97.0),
		("7600X", 96.0),
		("7600", 93.0),
		("5950X", 82.0),
		("5900X", 81.0),
		("5800X", 80.0),
		("5700X", 77.0),
		("5600X", 76.0),
		("5600", 73.0),
		("285K", 112.0),
		("265K", 108.0),
		("245K", 100.0),
		("14900K", 116.0),
		("14700K", 112.0),
		("14600K", 104.0),
		("13900K", 113.0),
		("13700K", 108.0),
		("13600K", 101.0),
		("13400", 84.0),
		("12900K", 98.0),
		("12700K", 95.0),
		("12600K", 90.0),
		("12400", 78.0)
	};

	public static double? GpuPerf(string gpuName)
	{
		return Lookup(GpuIndex, gpuName) ?? Lookup(GpuIndex, ShortGpuName(gpuName));
	}

	public static double? CpuPerf(string cpuName)
	{
		return Lookup(CpuIndex, cpuName);
	}

	private static double? Lookup((string Key, double Index)[] table, string name)
	{
		(string, double) hit = (from t in table
			where name.Contains(t.Key, StringComparison.OrdinalIgnoreCase)
			orderby t.Key.Length descending
			select t).FirstOrDefault();
		if (hit.Item1 != null)
		{
			return hit.Item2;
		}
		return null;
	}

	public static double ResolutionFactor(string res)
	{
		if (!(res == "2560x1440"))
		{
			if (res == "3840x2160")
			{
				return 0.42;
			}
			return 1.0;
		}
		return 0.72;
	}

	public static double PresetFactor(string preset)
	{
		return preset switch
		{
			"Baixo" => 1.6, 
			"Médio" => 1.3, 
			"Ultra" => 0.82, 
			_ => 1.0, 
		};
	}

	public static double UpscalingFactor(string upscaling, string res)
	{
		if (!(upscaling == "Quality"))
		{
			if (upscaling == "Performance")
			{
				return (res == "1920x1080") ? 1.45 : 1.75;
			}
			return 1.0;
		}
		return (res == "1920x1080") ? 1.25 : 1.4;
	}

	public static FpsEstimate Estimate(GameInfo g, double gpuIdx, double cpuIdx, string res, string preset, string upscaling)
	{
		double num = g.GpuRef * gpuIdx / 100.0 * ResolutionFactor(res) * PresetFactor(preset) * UpscalingFactor(upscaling, res);
		double cpuFps = g.CpuCap * cpuIdx / 100.0 * ((preset == "Baixo") ? 1.1 : ((preset == "Ultra") ? 0.95 : 1.0));
		double fps = Math.Min(num, cpuFps);
		Bottleneck limiter = ((num <= cpuFps) ? Bottleneck.Gpu : Bottleneck.Cpu);
		if (g.EngineCap > 0.0 && g.EngineCap < fps)
		{
			fps = g.EngineCap;
			limiter = Bottleneck.Engine;
		}
		return new FpsEstimate(Math.Round(fps), Math.Round(fps * 0.75), limiter);
	}

	public static string ShortGpuName(string name)
	{
		string s = name.Replace("(TM)", "").Replace("(R)", "");
		string[] array = new string[4] { "NVIDIA", "GeForce", "AMD", "Intel" };
		foreach (string w in array)
		{
			s = s.Replace(w, "");
		}
		if (s.Contains("Radeon RX"))
		{
			s = s.Replace("Radeon", "");
		}
		s = string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
		if (s.StartsWith("Arc") && s.EndsWith(" Graphics"))
		{
			string text = s;
			s = text.Substring(0, text.Length - 9);
		}
		return s;
	}

	public static string ShortCpuName(string name)
	{
		string s = name.Replace("AMD", "").Replace("Intel(R)", "").Replace("(R)", "")
			.Replace("(TM)", "")
			.Replace("Core(TM)", "Core");
		int i = s.IndexOf("-Core", StringComparison.OrdinalIgnoreCase);
		if (i > 0)
		{
			s = s.Substring(0, s.LastIndexOf(' ', i));
		}
		s = s.Replace("Processor", "").Replace("CPU", "");
		s = Regex.Replace(s, "\\d+th Gen\\s*", "");
		int at = s.IndexOf('@');
		if (at > 0)
		{
			s = s.Substring(0, at);
		}
		return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
	}
}
