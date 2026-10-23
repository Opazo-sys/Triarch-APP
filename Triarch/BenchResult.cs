namespace Triarch;

public sealed record BenchResult(int Threads, double Seconds, long Samples, int Passes)
{
	public double Score
	{
		get
		{
			if (!(Seconds > 0.0))
			{
				return 0.0;
			}
			return (double)Samples / Seconds / 10000.0;
		}
	}
}
