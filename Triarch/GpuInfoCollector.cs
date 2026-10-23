using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Triarch;

public static class GpuInfoCollector
{
	private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

	public static List<GpuInfo> Collect()
	{
		List<GpuInfo> result = new List<GpuInfo>();
		List<Dxgi.Adapter> adapters = Dxgi.Enumerate();
		List<(nint, Nvml.PciInfo)> nvDevices = Nvml.Devices();
		using ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
		foreach (ManagementObject vc in searcher.Get())
		{
			try
			{
				result.Add(Build(vc, adapters, nvDevices));
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Exception ex3 = ex2;
				GpuInfo g = new GpuInfo
				{
					Name = (vc["Name"]?.ToString() ?? "GPU")
				};
				g.Sections.Add(new InfoNode
				{
					Header = "ERRO"
				}.Also((InfoNode n) =>
				{
					n.Add("Falha ao ler", ex3.Message);
				}));
				result.Add(g);
			}
		}
		return result.OrderByDescending((GpuInfo gpuInfo) => gpuInfo.VramBytes).ToList();
	}

	private static InfoNode Also(this InfoNode n, Action<InfoNode> a)
	{
		a(n);
		return n;
	}

	private static GpuInfo Build(ManagementObject vc, List<Dxgi.Adapter> adapters, List<(nint Handle, Nvml.PciInfo Pci)> nvDevices)
	{
		string name = vc["Name"]?.ToString()?.Trim() ?? "GPU";
		string pnp = vc["PNPDeviceID"]?.ToString() ?? "";
		Match m = Regex.Match(pnp, "VEN_([0-9A-F]{4})&DEV_([0-9A-F]{4})(?:&SUBSYS_([0-9A-F]{4})([0-9A-F]{4}))?(?:&REV_([0-9A-F]{2}))?", RegexOptions.IgnoreCase);
		string ven = m.Groups[1].Value.ToUpperInvariant();
		string dev = m.Groups[2].Value.ToUpperInvariant();
		string subDev = m.Groups[3].Value.ToUpperInvariant();
		string subVen = m.Groups[4].Value.ToUpperInvariant();
		string rev = m.Groups[5].Value.ToUpperInvariant();
		GpuSpec spec = GpuSpecDb.Find(ven, dev, name);
		if (string.IsNullOrEmpty(ven) && spec != null)
		{
			ven = spec.InferVendorId;
		}
		string vendor = ven switch
		{
			"10DE" => "NVIDIA", 
			"1002" => "AMD", 
			"8086" => "Intel", 
			_ => ven, 
		};
		Dxgi.Adapter dx = adapters.FirstOrDefault((Dxgi.Adapter a) => a.VendorId.ToString("X4") == ven && a.DeviceId.ToString("X4") == dev) ?? adapters.FirstOrDefault((Dxgi.Adapter a) => string.Equals(a.Description?.Trim(), name, StringComparison.OrdinalIgnoreCase));
		nint h = IntPtr.Zero;
		if (ven == "10DE" && dev.Length == 4)
		{
			uint pciId = (Convert.ToUInt32(dev, 16) << 16) | 0x10DE;
			h = nvDevices.FirstOrDefault(((nint Handle, Nvml.PciInfo Pci) d) => d.Pci.PciDeviceId == pciId).Handle;
		}
		Dictionary<string, object> pnpProps = PnpProperties(pnp);
		Dictionary<string, object> reg = DriverRegistry(pnpProps.GetValueOrDefault("DEVPKEY_Device_Driver") as string);
		GpuInfo gpu = new GpuInfo
		{
			Name = name,
			Vendor = vendor,
			NvmlHandle = h
		};
		InfoNode id = Section(gpu, "IDENTIFICAÇÃO");
		id.Add("Nome", name);
		id.Add("Chip", spec?.Chip ?? "—");
		id.Add("Arquitetura", spec?.Arch ?? "—");
		id.Add("Revisão", (rev.Length > 0) ? rev : "—");
		id.Add("Fabricante da placa", (subVen.Length > 0) ? GpuSpecDb.SubvendorName(subVen) : "—");
		id.Add("Device ID", $"{ven} {dev} · {subVen} {subDev}".Trim(' ', '·'));
		id.Add("Processo", spec?.Process ?? "—");
		id.Add("Área do die", ((object)spec != null && spec.DieMm2 > 0) ? $"{spec.DieMm2} mm²" : "—");
		id.Add("Transistores", spec?.FormattedTransistors ?? "—");
		id.Add("Lançamento", spec?.Release ?? "—");
		int shaders = ((int?)((h != IntPtr.Zero) ? Nvml.Cores(h) : ((uint?)null))) ?? spec?.Shaders ?? 0;
		InfoNode units = Section(gpu, "UNIDADES DE PROCESSAMENTO");
		if (spec != null)
		{
			units.Add("Unidades de Processamento", spec.FormatUnitsSummary(ven, shaders));
		}
		if (shaders > 0)
		{
			bool flag = ven == "10DE";
			bool intel = ven == "8086";
			int perUnit = ((flag | intel) ? 128 : 64);
			string shaderLabel;
			if (flag)
			{
				shaderLabel = "CUDA cores";
			}
			else
			{
				shaderLabel = (intel ? "Shading units" : "Stream processors");
			}
			string unitLabel;
			if (flag)
			{
				unitLabel = "SMs";
			}
			else
			{
				unitLabel = (intel ? "Xe-Cores" : "Compute units (CUs)");
			}
			units.Add(shaderLabel, shaders.ToString("N0", PtBr));
			units.Add(unitLabel, $"{shaders / perUnit}");
			bool flag2 = flag && spec != null;
			if (flag2)
			{
				bool flag3;
				switch (spec.Arch)
				{
				case "Blackwell":
				case "Ada Lovelace":
				case "Ampere":
				case "Turing":
					flag3 = true;
					break;
				default:
					flag3 = false;
					break;
				}
				flag2 = flag3;
			}
			if (flag2)
			{
				units.Add("Tensor cores", $"{shaders / perUnit * 4}");
				units.Add("RT cores", $"{shaders / perUnit}");
			}
			else if (ven == "1002" && spec != null && spec.Arch.StartsWith("RDNA", StringComparison.OrdinalIgnoreCase) && !spec.Arch.StartsWith("RDNA 1", StringComparison.OrdinalIgnoreCase))
			{
				units.Add("RT Cores (Ray Accelerators)", $"{shaders / perUnit}");
			}
			else if (intel && spec != null && spec.Arch.StartsWith("Xe", StringComparison.OrdinalIgnoreCase))
			{
				units.Add("RT Units / XMX Engines", $"{shaders / perUnit} / {shaders / perUnit * 16}");
			}
		}
		if (spec != null)
		{
			units.Add("TMUs / ROPs", $"{spec.Tmus} TMUs / {spec.Rops} ROPs");
			units.Add("Pixel fillrate", $"{(double)(spec.Rops * spec.BoostMHz) / 1000.0:0.0} GPixel/s");
			units.Add("Texture fillrate", $"{(double)(spec.Tmus * spec.BoostMHz) / 1000.0:0.0} GTexel/s");
			units.Add("FP32 (teórico)", $"{(double)((shaders > 0) ? shaders : spec.Shaders) * 2.0 * (double)spec.BoostMHz / 1000000.0:0.00} TFLOPS");
		}
		if (units.Children.Count == 0)
		{
			units.Add("Sem dados", "GPU fora do banco de especificações");
		}
		InfoNode mem = Section(gpu, "MEMÓRIA");
		ulong vram = 0uL;
		if (h != IntPtr.Zero)
		{
			Nvml.Memory? memory = Nvml.MemoryInfo(h);
			if (memory.HasValue)
			{
				vram = memory.GetValueOrDefault().Total;
			}
		}
		if (vram == 0L && dx != null)
		{
			vram = dx.DedicatedVideo;
		}
		if (vram == 0L && reg.TryGetValue("HardwareInformation.qwMemorySize", out var q))
		{
			vram = Convert.ToUInt64(q);
		}
		gpu.VramBytes = vram;
		bool igpu = spec?.MemType.StartsWith("Memória do sistema") ?? false;
		if (igpu || vram < 1073741824)
		{
			mem.Add(igpu ? "Reservada (dedicada)" : "Tamanho", Bytes(vram));
		}
		else
		{
			double gib = (double)vram / 1073741824.0;
			mem.Add("Tamanho", $"{Math.Ceiling(gib - 0.15):0} GB ({vram >> 20:N0} MB utilizáveis)");
		}
		if (dx != null && dx.SharedSystem != 0)
		{
			mem.Add("Compartilhada do sistema", Bytes(dx.SharedSystem));
		}
		mem.Add("Tipo de Memória", spec?.MemType ?? "—");
		uint? bus = ((h != IntPtr.Zero) ? Nvml.BusWidth(h) : ((uint?)null));
		if (!bus.HasValue && (object)spec != null && spec.BusBits > 0)
		{
			bus = (uint)spec.BusBits;
		}
		uint? memClock = ((h != IntPtr.Zero) ? Nvml.MaxClock(h, 2) : ((uint?)null));
		if (bus.HasValue)
		{
			uint b = bus.GetValueOrDefault();
			if (b != 0)
			{
				mem.Add("Barramento", $"{b} bits");
			}
		}
		if (memClock.HasValue)
		{
			uint mc = memClock.GetValueOrDefault();
			double gbps = (double)(mc * 2) / 1000.0;
			mem.Add("Clock da memória", $"{mc} MHz ({gbps:0.0} Gbps efetivos)");
			if (bus.HasValue)
			{
				uint bw = bus.GetValueOrDefault();
				if (bw != 0)
				{
					mem.Add("Largura de banda", $"{gbps * (double)bw / 8.0:0.0} GB/s");
				}
			}
		}
		else if ((object)spec != null && spec.MemGbps > 0.0 && !igpu)
		{
			mem.Add("Velocidade (referência)", $"{spec.MemGbps:0.#} Gbps");
			if (bus.HasValue)
			{
				uint bw2 = bus.GetValueOrDefault();
				if (bw2 != 0)
				{
					mem.Add("Largura de banda", $"{spec.MemGbps * (double)bw2 / 8.0:0.0} GB/s");
				}
			}
		}
		if (h != IntPtr.Zero)
		{
			Nvml.Memory? memory = Nvml.Bar1(h);
			if (memory.HasValue)
			{
				Nvml.Memory bar = memory.GetValueOrDefault();
				mem.Add("Resizable BAR", (bar.Total > 268435456) ? ("Ativado (BAR1 " + Bytes(bar.Total) + ")") : "Desativado");
			}
		}
		InfoNode clk = Section(gpu, "CLOCKS");
		if (spec != null)
		{
			clk.Add("Base (referência)", $"{spec.BaseMHz} MHz");
			clk.Add("Boost (referência)", $"{spec.BoostMHz} MHz");
		}
		uint? num;
		if (h != IntPtr.Zero)
		{
			num = Nvml.MaxClock(h, 0);
			if (num.HasValue)
			{
				uint g = num.GetValueOrDefault();
				clk.Add("Máximo (curva V/F)", $"{g} MHz");
			}
			num = Nvml.MaxClock(h, 3);
			if (num.HasValue)
			{
				uint v = num.GetValueOrDefault();
				clk.Add("Video engine máx.", $"{v} MHz");
			}
		}
		if (clk.Children.Count == 0)
		{
			clk.Add("Sem dados", "—");
		}
		InfoNode pcie = Section(gpu, "BARRAMENTO");
		uint? maxGen = ((h != IntPtr.Zero) ? Nvml.MaxPcieGen(h) : ((uint?)null));
		uint? maxW = ((h != IntPtr.Zero) ? Nvml.MaxPcieWidth(h) : ((uint?)null));
		num = maxGen;
		if (!num.HasValue)
		{
			maxGen = AsUInt(pnpProps.GetValueOrDefault("DEVPKEY_PciDevice_MaxLinkSpeed"));
		}
		num = maxW;
		if (!num.HasValue)
		{
			maxW = AsUInt(pnpProps.GetValueOrDefault("DEVPKEY_PciDevice_MaxLinkWidth"));
		}
		uint? curGen = ((h != IntPtr.Zero) ? Nvml.CurPcieGen(h) : AsUInt(pnpProps.GetValueOrDefault("DEVPKEY_PciDevice_CurrentLinkSpeed")));
		uint? curW = ((h != IntPtr.Zero) ? Nvml.CurPcieWidth(h) : AsUInt(pnpProps.GetValueOrDefault("DEVPKEY_PciDevice_CurrentLinkWidth")));
		if (maxGen.HasValue)
		{
			uint mg = maxGen.GetValueOrDefault();
			if (maxW.HasValue)
			{
				uint mw = maxW.GetValueOrDefault();
				pcie.Add("Interface", $"{GpuInfo.PcieName(mg)} x{mw}");
			}
		}
		if (curGen.HasValue)
		{
			uint cg = curGen.GetValueOrDefault();
			if (curW.HasValue)
			{
				uint cw = curW.GetValueOrDefault();
				pcie.Add("Em uso agora", $"{GpuInfo.PcieName(cg)} x{cw}" + ((maxGen > curGen) ? " (economia de energia em repouso)" : ""));
			}
		}
		if (pnpProps.GetValueOrDefault("DEVPKEY_Device_LocationInfo") is string loc)
		{
			pcie.Add("Localização", loc);
		}
		if (h != IntPtr.Zero || ((object)spec != null && spec.TdpW > 0))
		{
			InfoNode pwr = Section(gpu, "ENERGIA");
			if ((object)spec != null && spec.TdpW > 0)
			{
				pwr.Add("TDP (referência)", $"{spec.TdpW} W");
			}
			if (h != IntPtr.Zero)
			{
				uint? num2 = Nvml.PowerLimitMw(h);
				if (num2.HasValue)
				{
					uint lim = num2.GetValueOrDefault();
					pwr.Add("Limite atual", $"{(double)lim / 1000.0:0} W");
				}
				num2 = Nvml.PowerDefaultMw(h);
				if (num2.HasValue)
				{
					uint def = num2.GetValueOrDefault();
					pwr.Add("Limite padrão da placa", $"{(double)def / 1000.0:0} W");
				}
				(uint, uint)? tuple = Nvml.PowerConstraintsMw(h);
				if (tuple.HasValue)
				{
					(uint, uint) pc = tuple.GetValueOrDefault();
					pwr.Add("Faixa ajustável", $"{(double)pc.Item1 / 1000.0:0} – {(double)pc.Item2 / 1000.0:0} W");
				}
			}
		}
		InfoNode drv = Section(gpu, "DRIVER E BIOS");
		string vbios = ((h != IntPtr.Zero) ? Nvml.Vbios(h) : null);
		if (vbios == null)
		{
			vbios = RegString(reg, "HardwareInformation.BiosString");
		}
		drv.Add("BIOS (VBIOS)", vbios ?? "—");
		string wmiVer = vc["DriverVersion"]?.ToString() ?? "—";
		string nvVer = ((h != IntPtr.Zero) ? Nvml.DriverVersion() : null);
		drv.Add("Versão do driver", (nvVer != null) ? (nvVer + " (" + wmiVer + ")") : wmiVer);
		if (vc["DriverDate"] is string { Length: >=8 } dd)
		{
			drv.Add("Data do driver", ManagementDateTimeConverter.ToDateTime(dd).ToString("dd/MM/yyyy"));
		}
		drv.Add("Assinatura digital", DriverSigner(pnp));
		if (h != IntPtr.Zero)
		{
			string cuda = Nvml.CudaVersion();
			if (cuda != null)
			{
				drv.Add("Versão CUDA", cuda);
			}
			string cc = Nvml.ComputeCapability(h);
			if (cc != null)
			{
				drv.Add("Compute capability", cc);
			}
		}
		InfoNode tech = Section(gpu, "TECNOLOGIAS");
		string fl = dx?.FeatureLevel;
		string value;
		if (fl == "12_2")
		{
			value = "DirectX 12 Ultimate (12_2)";
		}
		else
		{
			value = ((fl != null) ? ("DirectX 12 (" + fl.Replace('_', '.') + ")") : "—");
		}
		tech.Add("DirectX", value);
		List<string> apis = new List<string>();
		if (ven == "10DE" && File.Exists(Path.Combine(Environment.SystemDirectory, "nvcuda.dll")))
		{
			apis.Add("CUDA");
		}
		if (File.Exists(Path.Combine(Environment.SystemDirectory, "OpenCL.dll")))
		{
			apis.Add("OpenCL");
		}
		if (File.Exists(Path.Combine(Environment.SystemDirectory, "vulkan-1.dll")))
		{
			apis.Add("Vulkan");
		}
		if (fl != null)
		{
			apis.Add("DirectCompute");
			apis.Add("DirectML");
		}
		if (ven == "10DE")
		{
			apis.Add("PhysX");
		}
		tech.Add("APIs", (apis.Count > 0) ? string.Join(" · ", apis) : "—");
		if (spec != null && GpuSpecDb.ArchFeatures.TryGetValue(spec.Arch, out string feats))
		{
			tech.Add("Recursos da arquitetura", feats);
		}
		string memLabel = (((object)spec != null && spec.MemType.StartsWith("GDDR")) ? spec.MemType : "");
		gpu.Subtitle = string.Join("  ·  ", new string[5]
		{
			spec?.Chip,
			spec?.Arch,
			spec?.Process,
			(vram >= 1073741824 && !igpu) ? $"{Math.Ceiling((double)vram / 1073741824.0 - 0.15):0} GB {memLabel}".Trim() : null,
			(subVen.Length > 0) ? GpuSpecDb.SubvendorName(subVen) : null
		}.Where((string s) => !string.IsNullOrEmpty(s)));
		return gpu;
	}

	private static InfoNode Section(GpuInfo g, string title)
	{
		InfoNode n = new InfoNode
		{
			Header = title
		};
		g.Sections.Add(n);
		return n;
	}

	private static Dictionary<string, object?> PnpProperties(string pnpId)
	{
		Dictionary<string, object> dict = new Dictionary<string, object>();
		if (pnpId.Length == 0)
		{
			return dict;
		}
		try
		{
			using ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE DeviceID='" + pnpId.Replace("\\", "\\\\") + "'");
			ManagementObject mo = s.Get().Cast<ManagementObject>().FirstOrDefault();
			if (mo == null)
			{
				return dict;
			}
			ManagementBaseObject input = mo.GetMethodParameters("GetDeviceProperties");
			input["devicePropertyKeys"] = new string[6] { "DEVPKEY_PciDevice_CurrentLinkSpeed", "DEVPKEY_PciDevice_CurrentLinkWidth", "DEVPKEY_PciDevice_MaxLinkSpeed", "DEVPKEY_PciDevice_MaxLinkWidth", "DEVPKEY_Device_Driver", "DEVPKEY_Device_LocationInfo" };
			if (mo.InvokeMethod("GetDeviceProperties", input, null)?["deviceProperties"] is ManagementBaseObject[] props)
			{
				ManagementBaseObject[] array = props;
				foreach (ManagementBaseObject p in array)
				{
					if (p["KeyName"] is string k)
					{
						dict[k] = p["Data"];
					}
				}
			}
		}
		catch
		{
		}
		return dict;
	}

	private static Dictionary<string, object> DriverRegistry(string? driverKey)
	{
		Dictionary<string, object> dict = new Dictionary<string, object>();
		if (string.IsNullOrEmpty(driverKey))
		{
			return dict;
		}
		try
		{
			using RegistryKey key = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Class\\" + driverKey);
			if (key == null)
			{
				return dict;
			}
			string[] valueNames = key.GetValueNames();
			foreach (string n in valueNames)
			{
				if (n.StartsWith("HardwareInformation."))
				{
					object v = key.GetValue(n);
					if (v != null)
					{
						dict[n] = v;
					}
				}
			}
		}
		catch
		{
		}
		return dict;
	}

	private static string? RegString(Dictionary<string, object> reg, string name)
	{
		if (!reg.TryGetValue(name, out object v))
		{
			return null;
		}
		string s = ((v is byte[] bytes) ? Encoding.Unicode.GetString(bytes) : (v.ToString() ?? ""));
		s = s.TrimEnd('\0').Trim();
		if (s.Length <= 0)
		{
			return null;
		}
		return s;
	}

	private static string DriverSigner(string pnpId)
	{
		try
		{
			using ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT IsSigned, Signer FROM Win32_PnPSignedDriver WHERE DeviceID='" + pnpId.Replace("\\", "\\\\") + "'");
			using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = s.Get().GetEnumerator();
			if (managementObjectEnumerator.MoveNext())
			{
				ManagementObject d = (ManagementObject)managementObjectEnumerator.Current;
				object obj = d["IsSigned"];
				bool b = default;
				int num;
				if (obj is bool)
				{
					b = (bool)obj;
					num = 1;
				}
				else
				{
					num = 0;
				}
				int num2 = num & (b ? 1 : 0);
				string signer = d["Signer"]?.ToString() ?? "";
				if (num2 == 0)
				{
					return "Não assinado";
				}
				return signer.Contains("Windows Hardware Compatibility", StringComparison.OrdinalIgnoreCase) ? "WHQL (Microsoft)" : ("Assinado · " + signer);
			}
		}
		catch
		{
		}
		return "—";
	}

	private static uint? AsUInt(object? v)
	{
		if (v != null)
		{
			return Convert.ToUInt32(v);
		}
		return null;
	}

	private static string Bytes(ulong b)
	{
		if (b == 0L)
		{
			return "—";
		}
		double v = b;
		string[] u = new string[5] { "B", "KB", "MB", "GB", "TB" };
		int i = 0;
		while (v >= 1024.0 && i < u.Length - 1)
		{
			v /= 1024.0;
			i++;
		}
		return $"{v:0.#} {u[i]}";
	}
}
