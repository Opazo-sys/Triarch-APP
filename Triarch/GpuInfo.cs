using System;
using System.Collections.Generic;

namespace Triarch;

public sealed class GpuInfo
{
	public string Name { get; init; } = "";

	public string Subtitle { get; set; } = "";

	public string Vendor { get; init; } = "";

	public List<InfoNode> Sections { get; } = new List<InfoNode>();

	internal nint NvmlHandle { get; set; }

	internal ulong VramBytes { get; set; }

	public List<KeyValuePair<string, string>> LiveExtras()
	{
		List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
		nint h = NvmlHandle;
		if (h == IntPtr.Zero)
		{
			return list;
		}
		int? num = Nvml.PState(h);
		if (num.HasValue)
		{
			int p = num.GetValueOrDefault();
			list.Add(new KeyValuePair<string, string>("Estado de desempenho", $"P{p}"));
		}
		ulong? num2 = Nvml.ThrottleReasons(h);
		if (num2.HasValue)
		{
			ulong r = num2.GetValueOrDefault();
			list.Add(new KeyValuePair<string, string>("PerfCap (limitador)", PerfCap(r)));
		}
		uint? num3 = Nvml.PowerUsageMw(h);
		if (num3.HasValue)
		{
			uint pw = num3.GetValueOrDefault();
			num3 = Nvml.PowerLimitMw(h);
			if (num3.HasValue)
			{
				uint lim = num3.GetValueOrDefault();
				if (lim != 0)
				{
					list.Add(new KeyValuePair<string, string>("Consumo / limite", $"{(double)pw / 1000.0:0.0} W · {100.0 * (double)pw / (double)lim:0} %"));
				}
			}
		}
		Nvml.Utilization? utilization = Nvml.Util(h);
		if (utilization.HasValue)
		{
			Nvml.Utilization u = utilization.GetValueOrDefault();
			list.Add(new KeyValuePair<string, string>("Controlador de memória", $"{u.Memory} %"));
		}
		num3 = Nvml.EncoderUtil(h);
		if (num3.HasValue)
		{
			uint enc = num3.GetValueOrDefault();
			list.Add(new KeyValuePair<string, string>("Encoder (NVENC)", $"{enc} %"));
		}
		num3 = Nvml.DecoderUtil(h);
		if (num3.HasValue)
		{
			uint dec = num3.GetValueOrDefault();
			list.Add(new KeyValuePair<string, string>("Decoder (NVDEC)", $"{dec} %"));
		}
		num3 = Nvml.CurPcieGen(h);
		if (num3.HasValue)
		{
			uint g = num3.GetValueOrDefault();
			num3 = Nvml.CurPcieWidth(h);
			if (num3.HasValue)
			{
				uint w = num3.GetValueOrDefault();
				list.Add(new KeyValuePair<string, string>("PCIe em uso", $"{PcieName(g)} x{w}"));
			}
		}
		return list;
	}

	private static string PerfCap(ulong r)
	{
		List<string> parts = new List<string>();
		if ((r & 4) != 0L)
		{
			parts.Add("Energia");
		}
		if ((r & 0x60) != 0L)
		{
			parts.Add("Térmico");
		}
		if ((r & 8) != 0L)
		{
			parts.Add("HW slowdown");
		}
		if ((r & 0x80) != 0L)
		{
			parts.Add("Power brake");
		}
		if ((r & 1) != 0L && parts.Count == 0)
		{
			return "Ociosa";
		}
		if (parts.Count != 0)
		{
			return string.Join(" + ", parts);
		}
		return "Nenhum";
	}

	internal static string PcieName(uint gen)
	{
		return gen switch
		{
			1u => "PCIe 1.1", 
			2u => "PCIe 2.0", 
			3u => "PCIe 3.0", 
			4u => "PCIe 4.0", 
			5u => "PCIe 5.0", 
			6u => "PCIe 6.0", 
			_ => $"PCIe gen {gen}", 
		};
	}
}
