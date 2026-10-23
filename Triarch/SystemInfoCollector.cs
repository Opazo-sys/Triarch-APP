using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace Triarch;

public static class SystemInfoCollector
{
	private static readonly Regex SpeedInPart = new Regex("(?<!\\d)(2133|2400|2666|2933|3000|3200|3466|3600|3733|3800|4000|4133|4266|4400|4800|5200|5600|5800|6000|6200|6400|6600|6800|7000|7200|7400|7600|7800|8000|8200|8400|8800)(?!\\d)", RegexOptions.Compiled);

	private static readonly Regex ChannelInLocator = new Regex("(?i)(?:channel|chan|ch)[\\s_-]*([A-H0-9])|dimm[\\s_-]*([A-H])\\d|(?:^|[^A-Za-z])([A-H])[12](?:$|[^0-9])", RegexOptions.Compiled);

	public static List<InfoNode> Collect()
	{
		List<InfoNode> list = new List<InfoNode>();
		Safe(list, "Sistema operacional", CollectOs);
		Safe(list, "Placa-mãe e BIOS", CollectBoard);
		Safe(list, "Processador", CollectCpu);
		Safe(list, "Memória RAM", CollectRam);
		Safe(list, "Placa de vídeo", CollectGpu);
		Safe(list, "Armazenamento", CollectDisks);
		return list;
	}

	private static void Safe(List<InfoNode> roots, string title, Action<InfoNode> fill)
	{
		InfoNode node = new InfoNode
		{
			Header = title
		};
		try
		{
			fill(node);
		}
		catch (Exception ex)
		{
			node.Add("Erro ao ler", ex.Message);
		}
		roots.Add(node);
	}

	private static IEnumerable<ManagementObject> Query(string wql)
	{
		using ManagementObjectSearcher searcher = new ManagementObjectSearcher(wql);
		foreach (ManagementObject item in searcher.Get())
		{
			yield return item;
		}
	}

	private static string S(ManagementObject mo, string prop)
	{
		return mo[prop]?.ToString()?.Trim() ?? "—";
	}

	private static string Bytes(object? v)
	{
		if (v == null)
		{
			return "—";
		}
		double b = Convert.ToDouble(v);
		string[] u = new string[5] { "B", "KB", "MB", "GB", "TB" };
		int i = 0;
		while (b >= 1024.0 && i < u.Length - 1)
		{
			b /= 1024.0;
			i++;
		}
		return $"{b:0.##} {u[i]}";
	}

	private static void CollectOs(InfoNode n)
	{
		foreach (ManagementObject os in Query("SELECT * FROM Win32_OperatingSystem"))
		{
			n.Add("Nome", S(os, "Caption"));
			n.Add("Versão", S(os, "Version"));
			n.Add("Build", S(os, "BuildNumber"));
			n.Add("Arquitetura", S(os, "OSArchitecture"));
			n.Add("Nome do computador (Hostname)", Environment.MachineName);
			n.Add("Usuário atual", Environment.UserDomainName + "\\" + Environment.UserName);
			try
			{
				TimeSpan up = TimeSpan.FromMilliseconds(Environment.TickCount64);
				n.Add("Tempo ligado (Uptime)", $"{(int)up.TotalDays}d {up.Hours:00}h {up.Minutes:00}m");
			}
			catch
			{
			}
			try
			{
				if (os["InstallDate"] != null)
				{
					n.Add("Instalado em", ManagementDateTimeConverter.ToDateTime(os["InstallDate"].ToString()).ToString("dd/MM/yyyy HH:mm"));
				}
			}
			catch
			{
			}
		}
		try
		{
			foreach (ManagementObject cs in Query("SELECT Domain, PartOfDomain FROM Win32_ComputerSystem"))
			{
				bool inDomain = cs["PartOfDomain"] is bool b && b;
				n.Add(inDomain ? "Domínio corporativo (AD)" : "Grupo de trabalho (Workgroup)", S(cs, "Domain"));
			}
		}
		catch
		{
		}
		try
		{
			using ManagementObjectSearcher avSearcher = new ManagementObjectSearcher("root\\SecurityCenter2", "SELECT displayName FROM AntiVirusProduct");
			List<string> avs = new List<string>();
			foreach (ManagementObject av in avSearcher.Get())
			{
				string avName = av["displayName"]?.ToString()?.Trim() ?? "";
				if (avName.Length > 0 && !avs.Contains(avName))
				{
					avs.Add(avName);
				}
			}
			if (avs.Count > 0)
			{
				n.Add("Antivírus / EDR registrado", string.Join(" · ", avs));
			}
		}
		catch
		{
		}
		try
		{
			using ManagementObjectSearcher tpmSearcher = new ManagementObjectSearcher("root\\CIMV2\\Security\\MicrosoftTpm", "SELECT IsEnabled_InitialValue, SpecVersion FROM Win32_Tpm");
			foreach (ManagementObject tpm in tpmSearcher.Get())
			{
				bool enabled = tpm["IsEnabled_InitialValue"] is bool e && e;
				string spec = tpm["SpecVersion"]?.ToString()?.Split(',')[0]?.Trim() ?? "Presente";
				n.Add("Módulo TPM", enabled ? $"Ativo (Especificação {spec})" : "Desativado");
			}
		}
		catch
		{
		}
		if (false)
		{
		}
	}

	private static void CollectBoard(InfoNode n)
	{
		InfoNode board = n.Add("Placa-mãe");
		foreach (ManagementObject b in Query("SELECT * FROM Win32_BaseBoard"))
		{
			board.Add("Fabricante", S(b, "Manufacturer"));
			board.Add("Modelo", S(b, "Product"));
			board.Add("Versão", S(b, "Version"));
			board.Add("Nº de série", S(b, "SerialNumber"));
		}
		InfoNode bios = n.Add("BIOS");
		foreach (ManagementObject b2 in Query("SELECT * FROM Win32_BIOS"))
		{
			bios.Add("Fabricante", S(b2, "Manufacturer"));
			bios.Add("Versão", S(b2, "SMBIOSBIOSVersion"));
			bios.Add("Data", ManagementDateTimeConverter.ToDateTime(b2["ReleaseDate"].ToString()).ToString("dd/MM/yyyy"));
		}
	}

	private static void CollectCpu(InfoNode n)
	{
		foreach (ManagementObject c in Query("SELECT * FROM Win32_Processor"))
		{
			InfoNode infoNode = n.Add(S(c, "Name"));
			infoNode.Add("Fabricante", S(c, "Manufacturer"));
			infoNode.Add("Soquete", S(c, "SocketDesignation"));
			infoNode.Add("Núcleos físicos", S(c, "NumberOfCores"));
			infoNode.Add("Threads (lógicos)", S(c, "NumberOfLogicalProcessors"));
			infoNode.Add("Clock base/máx. declarado", S(c, "MaxClockSpeed") + " MHz");
			infoNode.Add("Cache L2", S(c, "L2CacheSize") + " KB");
			infoNode.Add("Cache L3", S(c, "L3CacheSize") + " KB");
			infoNode.Add("Virtualização habilitada", S(c, "VirtualizationFirmwareEnabled"));
		}
	}

	private static string RamType(object? smbios, object? typeLegacy)
	{
		return Convert.ToInt32(smbios ?? ((object)0)) switch
		{
			20 => "DDR", 
			21 => "DDR2", 
			24 => "DDR3", 
			26 => "DDR4", 
			34 => "DDR5", 
			35 => "LPDDR5", 
			_ => "Desconhecido", 
		};
	}

	private static string RamForm(object? ff)
	{
		return Convert.ToInt32(ff ?? ((object)0)) switch
		{
			8 => "DIMM", 
			12 => "SO-DIMM", 
			13 => "Micro-DIMM", 
			15 => "FB-DIMM", 
			9 => "RIMM", 
			11 => "SRIMM", 
			6 => "Proprietário", 
			0 => "—", 
			_ => "Outro", 
		};
	}

	private static string? ChannelOf(string locator)
	{
		Match m = ChannelInLocator.Match(locator);
		if (!m.Success)
		{
			return null;
		}
		for (int g = 1; g <= 3; g++)
		{
			if (m.Groups[g].Success)
			{
				return m.Groups[g].Value.ToUpperInvariant();
			}
		}
		return null;
	}

	private static string Volt(object? mv)
	{
		if (mv == null)
		{
			return "—";
		}
		double v = Convert.ToDouble(mv);
		if (!(v > 0.0))
		{
			return "—";
		}
		return $"{v / 1000.0:0.00#} V";
	}

	private static void CollectRam(InfoNode n)
	{
		List<(string, string, ulong, string, string, int, int, string, string, string, int, int, string, string, string)> mods = new List<(string, string, ulong, string, string, int, int, string, string, string, int, int, string, string, string)>();
		foreach (ManagementObject m in Query("SELECT * FROM Win32_PhysicalMemory"))
		{
			string rank = Convert.ToInt32(m["Attributes"] ?? ((object)0)) switch
			{
				1 => "1 (single)", 
				2 => "2 (dual)", 
				3 => "3", 
				4 => "4 (quad)", 
				_ => "—", 
			};
			string range = ((m["MinVoltage"] != null && m["MaxVoltage"] != null) ? (Volt(m["MinVoltage"]) + " – " + Volt(m["MaxVoltage"])) : "—");
			mods.Add((S(m, "BankLabel"), S(m, "DeviceLocator"), Convert.ToUInt64(m["Capacity"] ?? ((object)0uL)), RamType(m["SMBIOSMemoryType"], m["MemoryType"]), RamForm(m["FormFactor"]), Convert.ToInt32(m["Speed"] ?? ((object)0)), Convert.ToInt32(m["ConfiguredClockSpeed"] ?? ((object)0)), S(m, "Manufacturer"), S(m, "PartNumber"), S(m, "SerialNumber"), Convert.ToInt32(m["DataWidth"] ?? ((object)0)), Convert.ToInt32(m["TotalWidth"] ?? ((object)0)), rank, Volt(m["ConfiguredVoltage"]), range));
		}
		ulong total = 0uL;
		foreach (var item in mods)
		{
			total += item.Item3;
		}
		int slots = 0;
		ulong maxBytes = 0uL;
		try
		{
			foreach (ManagementObject a in Query("SELECT * FROM Win32_PhysicalMemoryArray"))
			{
				slots += Convert.ToInt32(a["MemoryDevices"] ?? ((object)0));
				ulong kb = ((a["MaxCapacityEx"] != null) ? Convert.ToUInt64(a["MaxCapacityEx"]) : Convert.ToUInt64(a["MaxCapacity"] ?? ((object)0uL)));
				maxBytes += kb * 1024;
			}
		}
		catch
		{
		}
		n.Add("Total instalado", Bytes(total));
		InfoNode sum = n.Add("Resumo");
		sum.Add("Módulos instalados", (slots > 0) ? $"{mods.Count} de {slots} slots" : mods.Count.ToString());
		if (maxBytes != 0)
		{
			sum.Add("Capacidade máxima suportada pela placa", Bytes(maxBytes));
		}
		if (mods.Count > 0)
		{
			List<string> types = mods.Select(((string Bank, string Loc, ulong Cap, string Type, string Form, int Rated, int Conf, string Maker, string Part, string Serial, int DataW, int TotalW, string Rank, string VoltCfg, string VoltRange) tuple) => tuple.Type).Distinct().ToList();
			sum.Add("Tipo", string.Join(" / ", types));
			int conf = mods.Where(((string Bank, string Loc, ulong Cap, string Type, string Form, int Rated, int Conf, string Maker, string Part, string Serial, int DataW, int TotalW, string Rank, string VoltCfg, string VoltRange) tuple) => tuple.Conf > 0).Select(((string Bank, string Loc, ulong Cap, string Type, string Form, int Rated, int Conf, string Maker, string Part, string Serial, int DataW, int TotalW, string Rank, string VoltCfg, string VoltRange) tuple) => tuple.Conf).DefaultIfEmpty(0)
				.Min();
			int rated = mods.Select(((string Bank, string Loc, ulong Cap, string Type, string Form, int Rated, int Conf, string Maker, string Part, string Serial, int DataW, int TotalW, string Rank, string VoltCfg, string VoltRange) tuple) => tuple.Rated).DefaultIfEmpty(0).Max();
			sum.Add("Velocidade em uso", (conf > 0) ? $"{conf} MT/s" : "—");
			sum.Add("Velocidade nominal reportada pelo módulo", (rated > 0) ? $"{rated} MT/s" : "—");
			int label = 0;
			foreach (var m2 in mods)
			{
				Match mt = SpeedInPart.Match(m2.Item9);
				if (mt.Success)
				{
					label = Math.Max(label, int.Parse(mt.Value));
				}
			}
			string profile;
			if (conf <= 0)
			{
				profile = "Não foi possível ler a velocidade em uso.";
			}
			else if (label > 0 && (double)conf < (double)label * 0.95)
			{
				profile = $"Provavelmente DESLIGADO: a RAM roda a {conf} MT/s, mas o part number indica {label} MT/s. Ative EXPO/XMP na BIOS.";
			}
			else if (label > 0)
			{
				profile = $"Provavelmente ATIVO: roda a {conf} MT/s (part number indica {label} MT/s).";
			}
			else
			{
				profile = ((rated <= 0 || conf >= rated) ? "Sem como estimar (o part number não traz a velocidade). Confira na BIOS." : $"Roda abaixo da velocidade nominal ({conf} de {rated} MT/s). Confira EXPO/XMP na BIOS.");
			}
			sum.Add("Perfil EXPO/XMP (estimado)", profile);
			List<string> chans = mods.Select(((string Bank, string Loc, ulong Cap, string Type, string Form, int Rated, int Conf, string Maker, string Part, string Serial, int DataW, int TotalW, string Rank, string VoltCfg, string VoltRange) tuple) => ChannelOf(tuple.Loc) ?? ChannelOf(tuple.Bank)).ToList();
			string chText;
			if (mods.Count == 1)
			{
				chText = "Canal único (1 módulo). Com 2 módulos iguais a RAM roda em dual channel e fica bem mais rápida.";
			}
			else if (chans.All((string c) => c != null))
			{
				int distinct = chans.Distinct().Count();
				chText = ((distinct >= 2) ? $"{distinct} canais (dual channel ou mais)" : "Canal único: os módulos estão no mesmo canal. Mude para os slots recomendados no manual da placa.");
			}
			else
			{
				chText = "Não deu para identificar pelos nomes dos slots (" + string.Join(", ", mods.Select(((string Bank, string Loc, ulong Cap, string Type, string Form, int Rated, int Conf, string Maker, string Part, string Serial, int DataW, int TotalW, string Rank, string VoltCfg, string VoltRange) tuple) => tuple.Loc)) + "). Veja o manual da placa.";
			}
			sum.Add("Canais (estimado)", chText);
			bool ecc = mods.Any(((string Bank, string Loc, ulong Cap, string Type, string Form, int Rated, int Conf, string Maker, string Part, string Serial, int DataW, int TotalW, string Rank, string VoltCfg, string VoltRange) tuple) => tuple.TotalW > tuple.DataW && tuple.DataW > 0);
			sum.Add("ECC", ecc ? "Sim (correção de erros)" : "Não");
			List<string> volts = (from v in mods.Select(((string Bank, string Loc, ulong Cap, string Type, string Form, int Rated, int Conf, string Maker, string Part, string Serial, int DataW, int TotalW, string Rank, string VoltCfg, string VoltRange) tuple) => tuple.VoltCfg)
				where v != "—"
				select v).Distinct().ToList();
			if (volts.Count > 0)
			{
				sum.Add("Tensão em uso", string.Join(" / ", volts));
			}
		}
		sum.Add("Timings (CL, tRCD, tRP...)", "O Windows não expõe. Veja na BIOS ou com o CPU-Z (aba Memory/SPD).");
		try
		{
			foreach (ManagementObject item2 in Query("SELECT TotalVisibleMemorySize, FreePhysicalMemory, TotalVirtualMemorySize, FreeVirtualMemory FROM Win32_OperatingSystem"))
			{
				double totKb = Convert.ToDouble(item2["TotalVisibleMemorySize"]);
				double freeKb = Convert.ToDouble(item2["FreePhysicalMemory"]);
				double virtTot = Convert.ToDouble(item2["TotalVirtualMemorySize"]);
				double virtFree = Convert.ToDouble(item2["FreeVirtualMemory"]);
				sum.Add("Em uso (no momento da leitura)", $"{Bytes((totKb - freeKb) * 1024.0)} de {Bytes(totKb * 1024.0)} ({(totKb - freeKb) / totKb:P0})");
				sum.Add("Memória virtual em uso (RAM + arquivo de paginação)", Bytes((virtTot - virtFree) * 1024.0) + " de " + Bytes(virtTot * 1024.0));
			}
		}
		catch
		{
		}
		foreach (var m3 in mods)
		{
			InfoNode infoNode = n.Add("Módulo — " + m3.Item1 + " / " + m3.Item2);
			infoNode.Add("Capacidade", Bytes(m3.Item3));
			infoNode.Add("Tipo", m3.Item4);
			infoNode.Add("Formato", m3.Item5);
			infoNode.Add("Ranks", m3.Item13);
			infoNode.Add("Velocidade em uso", (m3.Item7 > 0) ? $"{m3.Item7} MT/s" : "—");
			infoNode.Add("Velocidade nominal", (m3.Item6 > 0) ? $"{m3.Item6} MT/s" : "—");
			infoNode.Add("Tensão em uso", m3.Item14);
			infoNode.Add("Faixa de tensão do módulo", m3.Item15);
			infoNode.Add("Largura de dados / total", (m3.Item11 > 0) ? $"{m3.Item11} / {m3.Item12} bits" : "—");
			infoNode.Add("Fabricante", m3.Item8);
			infoNode.Add("Part number", m3.Item9);
			infoNode.Add("Nº de série", m3.Item10);
		}
	}

	private static void CollectGpu(InfoNode n)
	{
		foreach (ManagementObject g in Query("SELECT * FROM Win32_VideoController"))
		{
			InfoNode infoNode = n.Add(S(g, "Name"));
			infoNode.Add("Versão do driver", S(g, "DriverVersion"));
			infoNode.Add("Memória (WMI, limitada a 4 GB)", Bytes(g["AdapterRAM"]));
			infoNode.Add("Resolução atual", $"{S(g, "CurrentHorizontalResolution")}x{S(g, "CurrentVerticalResolution")} @ {S(g, "CurrentRefreshRate")} Hz");
		}
	}

	public static void RefreshDisks(InfoNode n)
	{
		n.Children.Clear();
		try
		{
			CollectDisks(n);
		}
		catch (Exception ex)
		{
			n.Add("Erro ao ler", ex.Message);
		}
	}

	private static void CollectDisks(InfoNode n)
	{
		foreach (ManagementObject d in Query("SELECT * FROM Win32_DiskDrive"))
		{
			InfoNode infoNode = n.Add(S(d, "Model"));
			infoNode.Add("Capacidade", Bytes(d["Size"]));
			infoNode.Add("Interface", S(d, "InterfaceType"));
			infoNode.Add("Nº de série", S(d, "SerialNumber"));
			infoNode.Add("Firmware", S(d, "FirmwareRevision"));
		}
	}
}
