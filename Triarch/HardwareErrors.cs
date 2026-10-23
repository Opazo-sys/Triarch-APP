namespace Triarch;

public sealed record HardwareErrors(int Whea, int GpuResets, string? LastDetail, string? Error)
{
	public int Total => Whea + GpuResets;
}
