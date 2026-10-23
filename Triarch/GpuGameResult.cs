namespace Triarch;

public sealed record GpuGameResult(double AvgFps, double Low1Fps, int Frames, double Seconds, int Width, int Height, string Adapter)
{
	public double Score => AvgFps * 10.0;
}
