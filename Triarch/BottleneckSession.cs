using System;
using System.Collections.Generic;
using System.Linq;

namespace Triarch;

public sealed class BottleneckSession
{
	private readonly List<(LoadSample Sample, Limiter Limiter)> _samples = new List<(LoadSample, Limiter)>();

	public DateTime Start { get; } = DateTime.Now;

	public TimeSpan Duration { get; init; }

	public int Count => _samples.Count;

	public bool Done => DateTime.Now - Start >= Duration;

	public Limiter? Main
	{
		get
		{
			if (_samples.Count != 0)
			{
				return Distribution()[0].Limiter;
			}
			return null;
		}
	}

	public double? AvgFps
	{
		get
		{
			List<double> f = (from x in _samples
				where x.Sample.Fps > 0.0
				select x.Sample.Fps.Value).ToList();
			if (f.Count <= 0)
			{
				return null;
			}
			return f.Average();
		}
	}

	public void Add(LoadSample s)
	{
		_samples.Add((s, s.Classify()));
	}

	public IReadOnlyList<(Limiter Limiter, double Pct)> Distribution()
	{
		return (from x in _samples
			group x by x.Limiter into g
			select (Key: g.Key, 100.0 * (double)g.Count() / (double)Math.Max(1, _samples.Count)) into x
			orderby x.Item2 descending
			select x).ToList();
	}

	public double? Avg(Func<LoadSample, float?> sel)
	{
		List<double> v = (from x in _samples
			select sel(x.Sample) into x
			where x.HasValue
			select x).Select((Func<float?, double>)((float? x) => x.Value)).ToList();
		if (v.Count <= 0)
		{
			return null;
		}
		return v.Average();
	}

	public double? Max(Func<LoadSample, double?> sel)
	{
		List<double> v = (from x in _samples
			select sel(x.Sample) into x
			where x.HasValue
			select x.Value).ToList();
		if (v.Count <= 0)
		{
			return null;
		}
		return v.Max();
	}
}
