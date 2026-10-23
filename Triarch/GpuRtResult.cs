using System.Collections.Generic;
using System.Linq;

namespace Triarch;

public sealed record GpuRtResult(IReadOnlyList<RtPass> Passes, int Width, int Height, string Preset, string Adapter, double GigaRaysPerSec)
{
	public double MedianFps => Median(Passes.Select((RtPass p) => p.AvgFps));

	public double MedianLow1 => Median(Passes.Select((RtPass p) => p.Low1Fps));

	public double VariationPct
	{
		get
		{
			if (Passes.Count >= 2 && !(MedianFps <= 0.0))
			{
				return (Passes.Max((RtPass p) => p.AvgFps) - Passes.Min((RtPass p) => p.AvgFps)) / MedianFps * 100.0;
			}
			return 0.0;
		}
	}

	public bool Reliable
	{
		get
		{
			if (Passes.Count >= 3)
			{
				return VariationPct < 3.0;
			}
			return false;
		}
	}

	public double Score => MedianFps * 10.0;

	private static double Median(IEnumerable<double> v)
	{
		double[] s = v.OrderBy((double x) => x).ToArray();
		if (s.Length != 0)
		{
			if (s.Length % 2 != 1)
			{
				return (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2.0;
			}
			return s[s.Length / 2];
		}
		return 0.0;
	}
}
