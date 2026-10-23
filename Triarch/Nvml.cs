using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Triarch;

internal static class Nvml
{
	public struct PciInfo
	{
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
		public string BusIdLegacy;

		public uint Domain;

		public uint Bus;

		public uint Device;

		public uint PciDeviceId;

		public uint PciSubSystemId;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string BusId;
	}

	public struct Memory
	{
		public ulong Total;

		public ulong Free;

		public ulong Used;
	}

	public struct Utilization
	{
		public uint Gpu;

		public uint Memory;
	}

	private delegate int UFunc(nint h, out uint v);

	private const string Dll = "nvml.dll";

	public const int ClockGraphics = 0;

	public const int ClockSm = 1;

	public const int ClockMem = 2;

	public const int ClockVideo = 3;

	private static bool? _ok;

	public static bool Available
	{
		get
		{
			bool valueOrDefault = _ok == true;
			if (!_ok.HasValue)
			{
				valueOrDefault = Call(() => nvmlInit());
				_ok = valueOrDefault;
			}
			return _ok.Value;
		}
	}

	[DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
	private static extern int nvmlInit();

	[DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount_v2")]
	private static extern int nvmlDeviceGetCount(out uint count);

	[DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
	private static extern int nvmlDeviceGetHandleByIndex(uint index, out nint device);

	[DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPciInfo_v3")]
	private static extern int nvmlDeviceGetPciInfo(nint device, out PciInfo pci);

	[DllImport("nvml.dll", CharSet = CharSet.Ansi)]
	private static extern int nvmlDeviceGetVbiosVersion(nint device, StringBuilder version, uint length);

	[DllImport("nvml.dll", CharSet = CharSet.Ansi)]
	private static extern int nvmlSystemGetDriverVersion(StringBuilder version, uint length);

	[DllImport("nvml.dll", EntryPoint = "nvmlSystemGetCudaDriverVersion_v2")]
	private static extern int nvmlSystemGetCudaDriverVersion(out int version);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetMemoryInfo(nint device, out Memory memory);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetBAR1MemoryInfo(nint device, out Memory bar1);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetMemoryBusWidth(nint device, out uint width);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetNumGpuCores(nint device, out uint cores);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetCudaComputeCapability(nint device, out int major, out int minor);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetMaxPcieLinkGeneration(nint device, out uint gen);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetMaxPcieLinkWidth(nint device, out uint width);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetCurrPcieLinkGeneration(nint device, out uint gen);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetCurrPcieLinkWidth(nint device, out uint width);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetMaxClockInfo(nint device, int type, out uint mhz);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetClockInfo(nint device, int type, out uint mhz);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetPowerManagementDefaultLimit(nint device, out uint mw);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetEnforcedPowerLimit(nint device, out uint mw);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetPowerManagementLimitConstraints(nint device, out uint minMw, out uint maxMw);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetPowerUsage(nint device, out uint mw);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetPerformanceState(nint device, out int pstate);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetCurrentClocksThrottleReasons(nint device, out ulong reasons);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetEncoderUtilization(nint device, out uint util, out uint periodUs);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetDecoderUtilization(nint device, out uint util, out uint periodUs);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceGetUtilizationRates(nint device, out Utilization util);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceSetPowerManagementLimit(nint device, uint mw);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceSetGpuLockedClocks(nint device, uint minMhz, uint maxMhz);

	[DllImport("nvml.dll")]
	private static extern int nvmlDeviceResetGpuLockedClocks(nint device);

	private static bool Call(Func<int> f)
	{
		try
		{
			return f() == 0;
		}
		catch (DllNotFoundException)
		{
			return false;
		}
		catch (EntryPointNotFoundException)
		{
			return false;
		}
	}

	public static string? Explain(int code)
	{
		return code switch
		{
			0 => null, 
			3 => "o driver não permite este ajuste nesta placa", 
			4 => "sem permissão (abra o TRIARCH como administrador)", 
			2 => "valor fora da faixa aceita", 
			_ => $"erro NVML {code}", 
		};
	}

	public static int SetPowerLimitMw(nint h, uint mw)
	{
		return Raw(() => nvmlDeviceSetPowerManagementLimit(h, mw));
	}

	public static int LockGpuClocks(nint h, uint min, uint max)
	{
		return Raw(() => nvmlDeviceSetGpuLockedClocks(h, min, max));
	}

	public static int ResetGpuClocks(nint h)
	{
		return Raw(() => nvmlDeviceResetGpuLockedClocks(h));
	}

	private static int Raw(Func<int> f)
	{
		try
		{
			return f();
		}
		catch (DllNotFoundException)
		{
			return 999;
		}
		catch (EntryPointNotFoundException)
		{
			return 3;
		}
	}

	public static List<(nint Handle, PciInfo Pci)> Devices()
	{
		List<(nint, PciInfo)> list = new List<(nint, PciInfo)>();
		uint n = 0u;
		if (!Available || !Call(() => nvmlDeviceGetCount(out n)))
		{
			return list;
		}
		for (uint i = 0u; i < n; i++)
		{
			nint h = IntPtr.Zero;
			PciInfo pci = default;
			if (Call(() => nvmlDeviceGetHandleByIndex(i, out h)) && Call(() => nvmlDeviceGetPciInfo(h, out pci)))
			{
				list.Add((h, pci));
			}
		}
		return list;
	}

	public static string? DriverVersion()
	{
		StringBuilder sb = new StringBuilder(96);
		if (!Call(() => nvmlSystemGetDriverVersion(sb, (uint)sb.Capacity)))
		{
			return null;
		}
		return sb.ToString();
	}

	public static string? CudaVersion()
	{
		int v = 0;
		if (!Call(() => nvmlSystemGetCudaDriverVersion(out v)))
		{
			return null;
		}
		return $"{v / 1000}.{v % 1000 / 10}";
	}

	public static string? Vbios(nint h)
	{
		StringBuilder sb = new StringBuilder(64);
		if (!Call(() => nvmlDeviceGetVbiosVersion(h, sb, (uint)sb.Capacity)))
		{
			return null;
		}
		return sb.ToString();
	}

	public static Memory? MemoryInfo(nint h)
	{
		Memory m = default;
		if (!Call(() => nvmlDeviceGetMemoryInfo(h, out m)))
		{
			return null;
		}
		return m;
	}

	public static Memory? Bar1(nint h)
	{
		Memory m = default;
		if (!Call(() => nvmlDeviceGetBAR1MemoryInfo(h, out m)))
		{
			return null;
		}
		return m;
	}

	public static uint? BusWidth(nint h)
	{
		return U(h, nvmlDeviceGetMemoryBusWidth);
	}

	public static uint? Cores(nint h)
	{
		return U(h, nvmlDeviceGetNumGpuCores);
	}

	public static uint? MaxPcieGen(nint h)
	{
		return U(h, nvmlDeviceGetMaxPcieLinkGeneration);
	}

	public static uint? MaxPcieWidth(nint h)
	{
		return U(h, nvmlDeviceGetMaxPcieLinkWidth);
	}

	public static uint? CurPcieGen(nint h)
	{
		return U(h, nvmlDeviceGetCurrPcieLinkGeneration);
	}

	public static uint? CurPcieWidth(nint h)
	{
		return U(h, nvmlDeviceGetCurrPcieLinkWidth);
	}

	public static uint? PowerDefaultMw(nint h)
	{
		return U(h, nvmlDeviceGetPowerManagementDefaultLimit);
	}

	public static uint? PowerLimitMw(nint h)
	{
		return U(h, nvmlDeviceGetEnforcedPowerLimit);
	}

	public static uint? PowerUsageMw(nint h)
	{
		return U(h, nvmlDeviceGetPowerUsage);
	}

	public static uint? MaxClock(nint h, int type)
	{
		uint v = 0u;
		if (!Call(() => nvmlDeviceGetMaxClockInfo(h, type, out v)))
		{
			return null;
		}
		return v;
	}

	public static uint? Clock(nint h, int type)
	{
		uint v = 0u;
		if (!Call(() => nvmlDeviceGetClockInfo(h, type, out v)))
		{
			return null;
		}
		return v;
	}

	public static (uint Min, uint Max)? PowerConstraintsMw(nint h)
	{
		uint a = 0u;
		uint b = 0u;
		if (!Call(() => nvmlDeviceGetPowerManagementLimitConstraints(h, out a, out b)))
		{
			return null;
		}
		return (a, b);
	}

	public static string? ComputeCapability(nint h)
	{
		int a = 0;
		int b = 0;
		if (!Call(() => nvmlDeviceGetCudaComputeCapability(h, out a, out b)))
		{
			return null;
		}
		return $"{a}.{b}";
	}

	public static int? PState(nint h)
	{
		int v = 0;
		if (!Call(() => nvmlDeviceGetPerformanceState(h, out v)))
		{
			return null;
		}
		return v;
	}

	public static ulong? ThrottleReasons(nint h)
	{
		ulong v = 0uL;
		if (!Call(() => nvmlDeviceGetCurrentClocksThrottleReasons(h, out v)))
		{
			return null;
		}
		return v;
	}

	public static uint? EncoderUtil(nint h)
	{
		uint v = 0u;
		if (!Call(() => nvmlDeviceGetEncoderUtilization(h, out v, out var _)))
		{
			return null;
		}
		return v;
	}

	public static uint? DecoderUtil(nint h)
	{
		uint v = 0u;
		if (!Call(() => nvmlDeviceGetDecoderUtilization(h, out v, out var _)))
		{
			return null;
		}
		return v;
	}

	public static Utilization? Util(nint h)
	{
		Utilization u = default;
		if (!Call(() => nvmlDeviceGetUtilizationRates(h, out u)))
		{
			return null;
		}
		return u;
	}

	private static uint? U(nint h, UFunc f)
	{
		uint v = 0u;
		if (!Call(() => f(h, out v)))
		{
			return null;
		}
		return v;
	}
}
