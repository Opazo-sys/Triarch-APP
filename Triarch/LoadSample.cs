namespace Triarch;

public sealed record LoadSample(float? GpuLoad, float? CpuTotal, float? CpuMaxCore, string? BusiestCore, double? VramPct, float? RamPct, float? GpuTemp, float? CpuTemp, bool GpuThermalCap, double? Fps)
{
	public Limiter Classify()
	{
		if (VramPct >= 97.0)
		{
			return Limiter.Vram;
		}
		if (GpuThermalCap || CpuTemp >= 95f)
		{
			return Limiter.Thermal;
		}
		if (GpuLoad >= 90f)
		{
			return Limiter.Gpu;
		}
		if (CpuMaxCore >= 85f || CpuTotal >= 85f)
		{
			return Limiter.Cpu;
		}
		if (RamPct >= 92f)
		{
			return Limiter.Ram;
		}
		return Limiter.Capped;
	}
}
