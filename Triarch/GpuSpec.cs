using System;
using System.Globalization;

namespace Triarch;

public record GpuSpec(string Chip, string Arch, string Process, int DieMm2, double TransistorsBi, int Shaders, int Rops, int Tmus, string MemType, int BaseMHz, int BoostMHz, int TdpW, string Release, string? NameMatch = null, int BusBits = 0, double MemGbps = 0.0)
{
	public string FormattedTransistors => (TransistorsBi * 1000.0).ToString("N0", PtBr) + " milhões";

	public string InferVendorId
	{
		get
		{
			bool flag;
			switch (Arch)
			{
			case "Blackwell":
			case "Ada Lovelace":
			case "Ampere":
			case "Turing":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (!flag)
			{
				if (!Arch.StartsWith("Xe", StringComparison.OrdinalIgnoreCase))
				{
					return "1002";
				}
				return "8086";
			}
			return "10DE";
		}
	}

	private static readonly CultureInfo PtBr = new CultureInfo("pt-BR");

	public string FormatUnitsSummary(string vendorId, int actualShaders = 0)
	{
		int sh = ((actualShaders > 0) ? actualShaders : Shaders);
		if (sh <= 0)
		{
			return "—";
		}
		bool flag = vendorId.Equals("10DE", StringComparison.OrdinalIgnoreCase);
		if (!flag)
		{
			bool flag2;
			switch (Arch)
			{
			case "Blackwell":
			case "Ada Lovelace":
			case "Ampere":
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
			int sms = sh / 128;
			return $"{sms} SMs ({sh} CUDA Cores, {Tmus} TMUs, {Rops} ROPs, {sms} RT Cores, {sms * 4} Tensor Cores)";
		}
		if (vendorId.Equals("8086", StringComparison.OrdinalIgnoreCase) || Arch.StartsWith("Xe", StringComparison.OrdinalIgnoreCase))
		{
			int xeCores = sh / 128;
			return $"{xeCores} Xe-Cores ({sh} Shading Units, {Tmus} TMUs, {Rops} ROPs, {xeCores} RT Cores)";
		}
		int cus = sh / 64;
		return $"{cus} CUs ({sh} Stream Processors, {Tmus} TMUs, {Rops} ROPs, {cus} RT Cores)";
	}
}
