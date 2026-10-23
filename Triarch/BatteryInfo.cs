using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using LibreHardwareMonitor.Hardware;

namespace Triarch;

public static class BatteryInfo
{
	private static List<Dictionary<string, object?>> Q(string scope, string cls)
	{
		List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
		try
		{
			using ManagementObjectSearcher s = new ManagementObjectSearcher(scope, "SELECT * FROM " + cls);
			foreach (ManagementObject o in s.Get())
			{
				Dictionary<string, object> d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
				foreach (PropertyData p in o.Properties)
				{
					d[p.Name] = p.Value;
				}
				list.Add(d);
				o.Dispose();
			}
		}
		catch
		{
		}
		return list;
	}

	private static Dictionary<string, object?>? At(List<Dictionary<string, object?>> l, int i)
	{
		if (i >= l.Count)
		{
			return null;
		}
		return l[i];
	}

	private static long? L(Dictionary<string, object?>? d, string k)
	{
		if (d != null && d.TryGetValue(k, out object v) && v != null)
		{
			try
			{
				return Convert.ToInt64(v);
			}
			catch
			{
			}
		}
		return null;
	}

	private static bool? B(Dictionary<string, object?>? d, string k)
	{
		if (d != null && d.TryGetValue(k, out object v) && v is bool)
		{
			return (bool)v;
		}
		return null;
	}

	private static string? S(Dictionary<string, object?>? d, string k)
	{
		if (d == null || !d.TryGetValue(k, out object v) || !(v is string s) || s.Trim().Length <= 0)
		{
			return null;
		}
		return s.Trim();
	}

	private static string Chemistry(long? c)
	{
		if (c.HasValue)
		{
			long valueOrDefault = c.GetValueOrDefault();
			long num = valueOrDefault - 3;
			if ((ulong)num <= 5uL)
			{
				switch ((int)num)
				{
				case 0:
					return "Chumbo-ácido";
				case 1:
					return "NiCd";
				case 2:
					return "NiMH";
				case 3:
					return "Íon-lítio";
				case 4:
					return "Zinco-ar";
				case 5:
					return "Polímero de lítio";
				}
			}
		}
		return "—";
	}

	private static string Status(long? s)
	{
		if (s.HasValue)
		{
			long valueOrDefault = s.GetValueOrDefault();
			long num = valueOrDefault - 1;
			if ((ulong)num <= 10uL)
			{
				switch ((int)num)
				{
				case 0:
					return "Descarregando";
				case 1:
					return "Na tomada";
				case 2:
					return "Totalmente carregada";
				case 3:
					return "Carga baixa";
				case 4:
					return "Carga crítica";
				case 5:
				case 6:
				case 7:
				case 8:
					return "Carregando";
				case 10:
					return "Parcialmente carregada";
				}
			}
		}
		return "—";
	}

	public static List<DiskHealthCard> Read(IEnumerable<SensorRow>? sensors = null)
	{
		List<DiskHealthCard> cards = new List<DiskHealthCard>();
		List<Dictionary<string, object>> win = Q("root\\cimv2", "Win32_Battery");
		List<Dictionary<string, object>> stat = Q("root\\WMI", "BatteryStaticData");
		List<Dictionary<string, object>> full = Q("root\\WMI", "BatteryFullChargedCapacity");
		List<Dictionary<string, object>> cyc = Q("root\\WMI", "BatteryCycleCount");
		List<Dictionary<string, object>> live = Q("root\\WMI", "BatteryStatus");
		int count = Math.Max(win.Count, stat.Count);
		bool onAc = false;
		for (int i = 0; i < count; i++)
		{
			Dictionary<string, object> w = At(win, i);
			Dictionary<string, object> st = At(stat, i);
			Dictionary<string, object?>? d = At(full, i);
			Dictionary<string, object> cy = At(cyc, i);
			Dictionary<string, object> lv = At(live, i);
			long? design = L(st, "DesignedCapacity");
			long? fullCap = L(d, "FullChargedCapacity");
			long? cycles = L(cy, "CycleCount");
			if (cycles == 0)
			{
				cycles = null;
			}
			long? pct = L(w, "EstimatedChargeRemaining");
			long? bstat = L(w, "BatteryStatus");
			bool? ac = B(lv, "PowerOnline");
			bool charging = B(lv, "Charging") == true;
			bool discharging = B(lv, "Discharging") == true;
			if (ac == true)
			{
				onAc = true;
			}
			else
			{
				bool flag = !ac.HasValue;
				if (flag)
				{
					bool flag2;
					switch (bstat)
					{
					case 2L:
					case 3L:
					case 6L:
					case 7L:
					case 8L:
					case 9L:
						flag2 = true;
						break;
					default:
						flag2 = false;
						break;
					}
					flag = flag2;
				}
				if (flag)
				{
					onAc = true;
				}
			}
			double? wear = ((design.HasValue && design.GetValueOrDefault() > 0 && fullCap.HasValue && fullCap.GetValueOrDefault() > 0) ? new double?(Math.Max(0.0, 100.0 * (1.0 - (double)fullCap.Value / (double)design.Value))) : ((double?)null));
			List<string> reasons = new List<string>();
			DiskVerdict verdict = DiskVerdict.Good;
			string label = "SAUDÁVEL";
			if (!wear.HasValue)
			{
				verdict = DiskVerdict.Unknown;
				label = "SEM DADOS";
				reasons.Add("O Windows não informou a capacidade de projeto ou a atual, então o desgaste não pôde ser calculado.");
			}
			else if (wear >= 50.0)
			{
				verdict = DiskVerdict.Critical;
				label = "DESGASTADA";
				reasons.Add($"Perdeu {wear:0}% da capacidade original: a autonomia é metade do que era. Vale trocar a bateria.");
			}
			else if (wear >= 30.0)
			{
				verdict = DiskVerdict.Warning;
				label = "ATENÇÃO";
				reasons.Add($"Perdeu {wear:0}% da capacidade original. Ainda funciona, mas a autonomia já caiu bastante.");
			}
			else if (wear >= 15.0)
			{
				reasons.Add($"Desgaste de {wear:0}%, normal para uma bateria com uso.");
			}
			else
			{
				reasons.Add($"Desgaste de apenas {wear:0}%: bateria em ótimo estado.");
			}
			if (cycles.HasValue && cycles.GetValueOrDefault() >= 1000 && verdict == DiskVerdict.Good)
			{
				verdict = DiskVerdict.Warning;
				label = "ATENÇÃO";
			}
			if (cycles.HasValue && cycles.GetValueOrDefault() >= 800)
			{
				reasons.Add($"{cycles} ciclos de carga: perto do fim da vida útil típica das baterias de íon-lítio (500 a 1000).");
			}
			if (pct.HasValue && pct.GetValueOrDefault() < 20 && !onAc)
			{
				reasons.Add("Carga baixa e fora da tomada.");
			}
			List<KeyValuePair<string, string>> m = new List<KeyValuePair<string, string>>();
			Add("Carga atual", pct.HasValue ? $"{pct}%" : null);
			string s1 = Status(bstat);
			string? v;
			if (s1 != "—")
			{
				v = s1;
			}
			else if (charging)
			{
				v = "Carregando";
			}
			else
			{
				v = (discharging ? "Descarregando" : null);
			}
			Add("Estado", v);
			long? run = L(w, "EstimatedRunTime");
			if (run.HasValue)
			{
				long valueOrDefault = run.GetValueOrDefault();
				if (valueOrDefault > 0 && valueOrDefault < 71582788 && !onAc)
				{
					Add("Autonomia estimada", $"{run / 60}h {run % 60:00}min");
				}
			}
			Add("Capacidade de projeto", design.HasValue ? $"{(double?)design / 1000.0:0.0} Wh" : null);
			Add("Capacidade máxima atual", fullCap.HasValue ? $"{(double?)fullCap / 1000.0:0.0} Wh" : null);
			Add("Desgaste", wear.HasValue ? $"{wear:0.0}%" : null);
			Add("Ciclos de carga", cycles?.ToString());
			long? volt = L(lv, "Voltage");
			if (volt.HasValue && volt.GetValueOrDefault() > 0)
			{
				Add("Tensão", $"{(double?)volt / 1000.0:0.00} V");
			}
			long? cr = L(lv, "ChargeRate");
			long? dr = L(lv, "DischargeRate");
			if (charging && cr.HasValue && cr.GetValueOrDefault() > 0)
			{
				Add("Potência de carga", $"{(double?)cr / 1000.0:0.0} W");
			}
			if (discharging && dr.HasValue && dr.GetValueOrDefault() > 0)
			{
				Add("Consumo atual", $"{(double?)dr / 1000.0:0.0} W");
			}
			Add("Fabricante", S(st, "ManufactureName"));
			Add("Modelo", S(st, "DeviceName") ?? S(w, "Name"));
			string ch = Chemistry(L(w, "Chemistry"));
			Add("Química", (ch != "—") ? ch : null);
			cards.Add(new DiskHealthCard
			{
				Title = ((count > 1) ? $"Bateria {i + 1}" : "Bateria"),
				Meta = S(st, "ManufactureName") + (pct.HasValue ? $" · {pct}% de carga" : ""),
				Verdict = verdict,
				Label = label,
				Reasons = string.Join(" ", reasons),
				Metrics = m
			});
			void Add(string k, string? value)
			{
				if (!string.IsNullOrEmpty(value))
				{
					m.Add(new KeyValuePair<string, string>(k, value));
				}
			}
		}
		string plan = null;
		foreach (Dictionary<string, object> p in Q("root\\cimv2\\power", "Win32_PowerPlan"))
		{
			if (B(p, "IsActive") == true)
			{
				plan = S(p, "ElementName");
				break;
			}
		}
		List<KeyValuePair<string, string>> em = new List<KeyValuePair<string, string>>
		{
			new KeyValuePair<string, string>("Fonte de energia", (count == 0) ? "Tomada (PC de mesa)" : (onAc ? "Tomada" : "Bateria"))
		};
		if (plan != null)
		{
			em.Add(new KeyValuePair<string, string>("Plano de energia", plan));
		}
		if (sensors != null)
		{
			foreach (SensorRow s2 in sensors.Where((SensorRow x) => x.Type == SensorType.Power && x.RawValue.GetValueOrDefault() > 0f).Take(6))
			{
				em.Add(new KeyValuePair<string, string>(s2.Hardware + " · " + s2.Name, s2.Value));
			}
		}
		string advice = ((plan != null && !plan.Contains("alto", StringComparison.OrdinalIgnoreCase) && !plan.Contains("high", StringComparison.OrdinalIgnoreCase) && !plan.Contains("máximo", StringComparison.OrdinalIgnoreCase) && !plan.Contains("ultimate", StringComparison.OrdinalIgnoreCase)) ? " Para jogos e benchmarks, o plano Alto desempenho (aba RESET OTIMIZA) evita que o PC diminua o clock." : "");
		cards.Add(new DiskHealthCard
		{
			Title = "Energia",
			Meta = ((count == 0) ? "Nenhuma bateria detectada" : "Fonte, plano e consumo"),
			Verdict = DiskVerdict.Good,
			Label = "EM USO",
			Reasons = ((plan != null) ? ("Plano de energia ativo: " + plan + ".") : "Plano de energia não identificado.") + advice + ((count == 0) ? " O consumo da fonte (PSU) não é medido pelo Windows; os valores abaixo são dos sensores de CPU e GPU." : ""),
			Metrics = em
		});
		return cards;
	}
}
