namespace Triarch;

public sealed class GpuBurnSettings
{
	public int Width { get; init; } = 1280;

	public int Height { get; init; } = 720;

	public bool Fullscreen { get; init; }

	public int Msaa { get; init; } = 1;

	public int Shells { get; init; } = 96;

	public int Iterations { get; init; }

	public string AdapterName { get; init; } = "";
}
