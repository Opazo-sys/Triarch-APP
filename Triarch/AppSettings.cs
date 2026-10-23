using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Triarch;

public sealed class AppSettings
{
	public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Triarch");

	private static string FilePath => Path.Combine(Folder, "settings.json");

	public string? HowManyFpsKeyProtected { get; set; }

	public List<BenchRun> BenchRuns { get; set; } = new List<BenchRun>();

	public List<BenchRecord> BenchRecords { get; set; } = new List<BenchRecord>();

	public Dictionary<string, string?> TweakBackup { get; set; } = new Dictionary<string, string>();

	public List<StartupBackup> StartupRemoved { get; set; } = new List<StartupBackup>();

	public Dictionary<string, GameProfile> GameProfiles { get; set; } = new Dictionary<string, GameProfile>();

	public List<CustomGame> CustomGames { get; set; } = new List<CustomGame>();

	public bool LightTheme { get; set; }

	public bool SidebarCollapsed { get; set; }

	public string OverlayCorner { get; set; } = "TL";

	public double OverlayScale { get; set; } = 1.0;

	public List<string>? OverlayItems { get; set; }

	public List<string>? HiddenSensorHardware { get; set; }

	public bool SensorWindowTopmost { get; set; } = true;

	public int SensorWindowColumns { get; set; } = 2;

	[JsonIgnore]
	public string? HowManyFpsKey
	{
		get
		{
			if (string.IsNullOrEmpty(HowManyFpsKeyProtected))
			{
				return null;
			}
			try
			{
				byte[] bytes = ProtectedData.Unprotect(Convert.FromBase64String(HowManyFpsKeyProtected), null, DataProtectionScope.CurrentUser);
				return Encoding.UTF8.GetString(bytes);
			}
			catch
			{
				return null;
			}
		}
		set
		{
			HowManyFpsKeyProtected = (string.IsNullOrWhiteSpace(value) ? null : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser)));
		}
	}

	public static AppSettings Load()
	{
		try
		{
			if (File.Exists(FilePath))
			{
				return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
			}
		}
		catch
		{
		}
		return new AppSettings();
	}

	public void Save()
	{
		Directory.CreateDirectory(Folder);
		File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}
}
