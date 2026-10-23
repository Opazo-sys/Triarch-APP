namespace Triarch;

public sealed record GameSimResult(double AvgFps, double Low1Fps, double MainThreadMs, double ParallelMs, int Frames, double Seconds)
{
	public double Score => AvgFps;
}
