using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace Triarch;

public static class DiskHealth
{
	private sealed class Raw
	{
		public int Index;

		public string Model = "";

		public string Serial = "";

		public string Firmware = "";

		public string Bus = "";

		public string Media = "";

		public long Size;

		public int Health = 5;

		public string Pnp = "";

		public bool? PredictFail;

		public double? Temp;

		public double? TempMax;

		public double? Wear;

		public long? PowerOnHours;

		public long? ReadErr;

		public long? WriteErr;

		public long? ReadErrUncorrected;

		public long? WriteErrUncorrected;

		public long? StartStop;

		public double? WrittenGb;

		public double? ReadGb;

		public string Letters = "";

		public string? Error;
	}

	private const string StorageScope = "\\\\.\\root\\Microsoft\\Windows\\Storage";

	public static List<DiskHealthCard> Read(IEnumerable<SensorRow>? sensors = null)
	{
		Dictionary<int, Raw> raws = new Dictionary<int, Raw>();
		try
		{
			using ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Index, Model, SerialNumber, FirmwareRevision, InterfaceType, Size, PNPDeviceID, MediaType FROM Win32_DiskDrive");
			foreach (ManagementObject d in s.Get())
			{
				int idx = Convert.ToInt32(d["Index"] ?? ((object)0));
				string pnp = d["PNPDeviceID"]?.ToString() ?? "";
				raws[idx] = new Raw
				{
					Index = idx,
					Model = (d["Model"]?.ToString()?.Trim() ?? $"Disco {idx}"),
					Serial = (d["SerialNumber"]?.ToString()?.Trim() ?? ""),
					Firmware = (d["FirmwareRevision"]?.ToString()?.Trim() ?? ""),
					Bus = (pnp.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase) ? "USB" : (d["InterfaceType"]?.ToString() ?? "")),
					Size = Convert.ToInt64(d["Size"] ?? ((object)0L)),
					Pnp = pnp
				};
			}
		}
		catch (Exception ex)
		{
			return new List<DiskHealthCard> { ErrorCard(ex.Message) };
		}
		try
		{
			ManagementScope scope = new ManagementScope("\\\\.\\root\\Microsoft\\Windows\\Storage");
			scope.Connect();
			Dictionary<string, int> idByDeviceId = new Dictionary<string, int>();
			using (ManagementObjectSearcher s2 = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_PhysicalDisk")))
			{
				foreach (ManagementObject d2 in s2.Get())
				{
					string devId = d2["DeviceId"]?.ToString() ?? "";
					if (int.TryParse(devId, out var idx2) && raws.TryGetValue(idx2, out var r))
					{
						idByDeviceId[devId] = idx2;
						r.Health = Convert.ToInt32(d2["HealthStatus"] ?? ((object)5));
						Raw raw = r;
						raw.Media = Convert.ToInt32(d2["MediaType"] ?? ((object)0)) switch
						{
							3 => "HDD", 
							4 => "SSD", 
							5 => "SCM", 
							_ => "", 
						};
						int bus = Convert.ToInt32(d2["BusType"] ?? ((object)0));
						raw = r;
						raw.Bus = bus switch
						{
							17 => "NVMe", 
							11 => "SATA", 
							7 => "USB", 
							10 => "SAS", 
							8 => "RAID", 
							_ => r.Bus, 
						};
					}
				}
			}
			using ManagementObjectSearcher s3 = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_StorageReliabilityCounter"));
			foreach (ManagementObject c in s3.Get())
			{
				string devId2 = c["DeviceId"]?.ToString() ?? "";
				if (idByDeviceId.TryGetValue(devId2, out var idx3))
				{
					Raw raw2 = raws[idx3];
					raw2.Temp = Num(c["Temperature"]);
					raw2.TempMax = Num(c["TemperatureMax"]);
					raw2.Wear = Num(c["Wear"]);
					raw2.PowerOnHours = (long?)Num(c["PowerOnHours"]);
					raw2.ReadErr = (long?)Num(c["ReadErrorsTotal"]);
					raw2.WriteErr = (long?)Num(c["WriteErrorsTotal"]);
					raw2.ReadErrUncorrected = (long?)Num(c["ReadErrorsUncorrected"]);
					raw2.WriteErrUncorrected = (long?)Num(c["WriteErrorsUncorrected"]);
					raw2.StartStop = (long?)Num(c["StartStopCycleCount"]);
				}
			}
		}
		catch
		{
		}
		try
		{
			using ManagementObjectSearcher s4 = new ManagementObjectSearcher("root\\wmi", "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus");
			foreach (ManagementObject item in s4.Get())
			{
				string inst = item["InstanceName"]?.ToString() ?? "";
				bool fail = item["PredictFailure"] is bool b && b;
				foreach (Raw r2 in raws.Values)
				{
					if (r2.Pnp.Length > 0 && inst.StartsWith(r2.Pnp, StringComparison.OrdinalIgnoreCase))
					{
						r2.PredictFail = (r2.PredictFail == true) || fail;
					}
				}
			}
		}
		catch
		{
		}
		try
		{
			FillLetters(raws);
		}
		catch
		{
		}
		if (sensors != null)
		{
			List<SensorRow> list = sensors.Where((SensorRow x) => x.HardwareType == HardwareType.Storage).ToList();
			foreach (Raw r3 in raws.Values)
			{
				List<SensorRow> mine = list.Where((SensorRow x) => x.Hardware.Contains(r3.Model, StringComparison.OrdinalIgnoreCase) || r3.Model.Contains(x.Hardware, StringComparison.OrdinalIgnoreCase)).ToList();
				if (mine.Count == 0)
				{
					continue;
				}
				if (!r3.Temp.HasValue)
				{
					r3.Temp = mine.FirstOrDefault((SensorRow x) => x.Type == SensorType.Temperature && x.RawValue > 0f)?.RawValue;
				}
				if (!r3.Wear.HasValue)
				{
					float? used = mine.FirstOrDefault((SensorRow x) => x.Name.Contains("Percentage Used", StringComparison.OrdinalIgnoreCase))?.RawValue;
					float? left = mine.FirstOrDefault((SensorRow x) => x.Name.Contains("Remaining Life", StringComparison.OrdinalIgnoreCase))?.RawValue;
					if (used.HasValue)
					{
						r3.Wear = used;
					}
					else if (left.HasValue)
					{
						r3.Wear = 100f - left;
					}
				}
				r3.WrittenGb = mine.FirstOrDefault((SensorRow x) => x.Name.Contains("Data Written", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("Host Writes", StringComparison.OrdinalIgnoreCase))?.RawValue;
				r3.ReadGb = mine.FirstOrDefault((SensorRow x) => x.Name.Contains("Data Read", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("Host Reads", StringComparison.OrdinalIgnoreCase))?.RawValue;
			}
		}
		return raws.Values.OrderBy((Raw raw3) => raw3.Index).Select(ToCard).ToList();
	}

	private static double? Num(object? v)
	{
		if (v == null)
		{
			return null;
		}
		try
		{
			double d = Convert.ToDouble(v);
			return (d < 0.0 || d > 1000000000000.0) ? ((double?)null) : new double?(d);
		}
		catch
		{
			return null;
		}
	}

	private static DiskHealthCard ErrorCard(string msg)
	{
		return new DiskHealthCard
		{
			Title = "Não foi possível ler os discos",
			Verdict = DiskVerdict.Unknown,
			Reasons = msg
		};
	}

	private static void FillLetters(Dictionary<int, Raw> raws)
	{
		Dictionary<string, string> diskOfPart = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Antecedent, Dependent FROM Win32_DiskDriveToDiskPartition"))
		{
			foreach (ManagementObject a in s.Get())
			{
				string? disk = RefId(a["Antecedent"]);
				if (disk != null)
				{
					string? part = RefId(a["Dependent"]);
					if (part != null)
					{
						diskOfPart[part] = disk;
					}
				}
			}
		}
		Dictionary<int, List<string>> letters = new Dictionary<int, List<string>>();
		using (ManagementObjectSearcher s2 = new ManagementObjectSearcher("SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition"))
		{
			foreach (ManagementObject a2 in s2.Get())
			{
				string? part2 = RefId(a2["Antecedent"]);
				if (part2 == null)
				{
					continue;
				}
				string? letter = RefId(a2["Dependent"]);
				if (letter == null || !diskOfPart.TryGetValue(part2, out var disk2))
				{
					continue;
				}
				Match m = Regex.Match(disk2, "PHYSICALDRIVE(\\d+)", RegexOptions.IgnoreCase);
				if (m.Success)
				{
					int idx = int.Parse(m.Groups[1].Value);
					if (!letters.TryGetValue(idx, out var l))
					{
						l = (letters[idx] = new List<string>());
					}
					l.Add(letter);
				}
			}
		}
		foreach (KeyValuePair<int, List<string>> kv in letters)
		{
			if (raws.TryGetValue(kv.Key, out Raw? r))
			{
				r.Letters = string.Join(" ", from x in kv.Value.Distinct()
					orderby x
					select x);
			}
		}
	}

	private static string? RefId(object? reference)
	{
		Match m = Regex.Match(reference?.ToString() ?? "", "DeviceID=\"(.*)\"");
		if (!m.Success)
		{
			return null;
		}
		return m.Groups[1].Value.Replace("\\\\", "\\");
	}

	private static DiskHealthCard ToCard(Raw r)
	{
		bool isSsd = r.Media == "SSD" || r.Bus == "NVMe";
		bool isUsb = r.Bus == "USB";
		List<string> crit = new List<string>();
		List<string> warn = new List<string>();
		bool anyData = r.Health != 5 || r.PredictFail.HasValue || r.Wear.HasValue || r.Temp.HasValue || r.PowerOnHours.HasValue;
		if (r.PredictFail == true)
		{
			crit.Add("O próprio disco avisa (S.M.A.R.T.) que deve falhar em breve. Faça backup agora.");
		}
		if (r.Health == 2)
		{
			crit.Add("O Windows classifica o disco como NÃO saudável.");
		}
		else if (r.Health == 1)
		{
			warn.Add("O Windows classifica a saúde do disco como \"atenção\".");
		}
		if (r.Wear.HasValue)
		{
			double w = r.Wear.Value;
			if (w >= 95.0)
			{
				crit.Add($"Desgaste de {w:0}%: o SSD está no fim da vida útil. Troque e faça backup.");
			}
			else if (w >= 80.0)
			{
				warn.Add($"Desgaste de {w:0}%: o SSD já gastou boa parte da vida útil. Planeje a troca.");
			}
		}
		if (r.Temp.HasValue)
		{
			double t = r.Temp.Value;
			double okMax = (isSsd ? 70 : 55);
			double critMax = (isSsd ? 85 : 65);
			if (t >= critMax)
			{
				crit.Add($"Temperatura de {t:0} °C é muito alta para este disco.");
			}
			else if (t >= okMax)
			{
				warn.Add($"Temperatura de {t:0} °C está alta. Melhore a ventilação.");
			}
		}
		if (r.ReadErrUncorrected > 0 || r.WriteErrUncorrected > 0)
		{
			warn.Add($"Erros não corrigidos: {r.ReadErrUncorrected.GetValueOrDefault()} de leitura e {r.WriteErrUncorrected.GetValueOrDefault()} de escrita.");
		}
		DiskVerdict v = ((crit.Count > 0) ? DiskVerdict.Critical : ((warn.Count > 0) ? DiskVerdict.Warning : ((!anyData) ? DiskVerdict.Unknown : DiskVerdict.Good)));
		List<string> reasons = crit.Concat(warn).ToList();
		if (reasons.Count == 0)
		{
			reasons.Add((v == DiskVerdict.Good) ? "Nenhum problema encontrado nas leituras disponíveis." : "Este disco não informa dados de saúde (comum em pendrives e alguns HDs externos).");
		}
		string kind = ((r.Media.Length > 0) ? r.Media : (isSsd ? "SSD" : ""));
		IEnumerable<string> meta = new string[4]
		{
			kind,
			r.Bus,
			FmtSize(r.Size),
			(r.Letters.Length > 0) ? ("Unidades: " + r.Letters) : ""
		}.Where((string x) => !string.IsNullOrEmpty(x));
		List<KeyValuePair<string, string>> metrics = new List<KeyValuePair<string, string>>();
		string? tempVal = null;
		if (r.Temp.HasValue)
		{
			string maxSuffix = (r.TempMax > 0.0) ? $" (máx. registrado {r.TempMax.Value:0})" : "";
			tempVal = $"{r.Temp.Value:0} °C" + maxSuffix;
		}
		M("Temperatura", tempVal);
		if (r.Wear.HasValue)
		{
			double wr = r.Wear.Value;
			M("Desgaste", $"{wr:0}%");
			M("Vida útil restante", $"{Math.Max(0.0, 100.0 - wr):0}%");
		}
		M("Horas ligado", r.PowerOnHours.HasValue ? $"{r.PowerOnHours.Value:N0} h ({(double)r.PowerOnHours.Value / 24.0:N0} dias)" : null);
		M("Ciclos liga/desliga", r.StartStop.HasValue ? $"{r.StartStop.Value:N0}" : null);
		string? readErrVal = null;
		if (r.ReadErr.HasValue)
		{
			string ruSuffix = (r.ReadErrUncorrected > 0) ? $" ({r.ReadErrUncorrected.Value:N0} não corrigidos)" : "";
			readErrVal = $"{r.ReadErr.Value:N0}" + ruSuffix;
		}
		M("Erros de leitura", readErrVal);
		string? writeErrVal = null;
		if (r.WriteErr.HasValue)
		{
			string wuSuffix = (r.WriteErrUncorrected > 0) ? $" ({r.WriteErrUncorrected.Value:N0} não corrigidos)" : "";
			writeErrVal = $"{r.WriteErr.Value:N0}" + wuSuffix;
		}
		M("Erros de escrita", writeErrVal);
		M("Total gravado", (r.WrittenGb > 0.0) ? $"{r.WrittenGb.Value:N0} GB" : null);
		M("Total lido", (r.ReadGb > 0.0) ? $"{r.ReadGb.Value:N0} GB" : null);
		M("Falha prevista (S.M.A.R.T.)", r.PredictFail.HasValue ? ((r.PredictFail == true) ? "SIM" : "Não") : null);
		M("Saúde segundo o Windows", r.Health switch
		{
			0 => "Saudável", 
			1 => "Atenção", 
			2 => "Não saudável", 
			_ => null, 
		});
		M("Firmware", (r.Firmware.Length > 0) ? r.Firmware : null);
		return new DiskHealthCard
		{
			Index = r.Index,
			Title = r.Model,
			Meta = string.Join(" · ", meta),
			Serial = r.Serial,
			Verdict = ((isUsb && !anyData) ? DiskVerdict.Unknown : v),
			Reasons = string.Join(" ", reasons),
			Metrics = metrics
		};
		void M(string k, string? val)
		{
			if (val != null)
			{
				metrics.Add(new KeyValuePair<string, string>(k, val));
			}
		}
	}

	private static string FmtSize(long b)
	{
		double v = b;
		string[] u = new string[5] { "B", "KB", "MB", "GB", "TB" };
		int i = 0;
		while (v >= 1000.0 && i < u.Length - 1)
		{
			v /= 1000.0;
			i++;
		}
		if (b <= 0)
		{
			return "";
		}
		return $"{v:0.#} {u[i]}";
	}
}
