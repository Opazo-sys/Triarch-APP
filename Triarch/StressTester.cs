using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;

namespace Triarch;

public sealed class StressTester
{
	private CancellationTokenSource? _cts;

	private readonly List<Thread> _threads = new List<Thread>();

	private long _memErrors;

	private long _memPasses;

	public bool Running => _cts != null;

	public long MemoryErrors => Interlocked.Read(in _memErrors);

	public long MemoryPasses => Interlocked.Read(in _memPasses);

	public long MemoryAllocatedBytes { get; private set; }

	public void Start(bool cpu, bool ram, double ramFraction = 0.6)
	{
		if (Running)
		{
			return;
		}
		_cts = new CancellationTokenSource();
		CancellationToken token = _cts.Token;
		Interlocked.Exchange(ref _memErrors, 0L);
		Interlocked.Exchange(ref _memPasses, 0L);
		MemoryAllocatedBytes = 0L;
		int cpuThreads = Environment.ProcessorCount;
		if ((cpu & ram) && cpuThreads > 1)
		{
			cpuThreads--;
		}
		if (cpu)
		{
			for (int i = 0; i < cpuThreads; i++)
			{
				int seed = i;
				StartThread(() =>
				{
					CpuWorker(token, seed);
				}, $"Stress CPU {seed}");
			}
		}
		if (ram)
		{
			long bytes = (long)((double)FreePhysicalMemory() * Math.Clamp(ramFraction, 0.1, 0.9));
			MemoryAllocatedBytes = bytes;
			StartThread(() =>
			{
				RamWorker(token, bytes);
			}, "Stress RAM");
		}
	}

	public void Stop()
	{
		if (_cts == null)
		{
			return;
		}
		_cts.Cancel();
		foreach (Thread thread in _threads)
		{
			thread.Join(2000);
		}
		_threads.Clear();
		_cts.Dispose();
		_cts = null;
		GC.Collect();
	}

	private void StartThread(ThreadStart body, string name)
	{
		Thread t = new Thread(body)
		{
			IsBackground = true,
			Name = name,
			Priority = ThreadPriority.BelowNormal
		};
		_threads.Add(t);
		t.Start();
	}

	private static void CpuWorker(CancellationToken token, int seed)
	{
		double[] buf = new double[4096];
		for (int i = 0; i < buf.Length; i++)
		{
			buf[i] = i + seed + 1;
		}
		ulong x = (ulong)((seed + 1) * -7046029254386353131L);
		while (!token.IsCancellationRequested)
		{
			for (int rep = 0; rep < 64; rep++)
			{
				for (int j = 0; j < buf.Length; j++)
				{
					double v = buf[j];
					v = Math.FusedMultiplyAdd(v, 1.0000001, 0.5);
					v = Math.Sqrt(v * v + 1.0) + Math.Sin(v) * 0.001;
					buf[j] = ((v > 1000000000000.0) ? ((double)(j + 1)) : v);
					x ^= x << 13;
					x ^= x >> 7;
					x ^= x << 17;
				}
			}
			if (x == 0L)
			{
				buf[0] = 0.0;
			}
		}
	}

	private void RamWorker(CancellationToken token, long totalBytes)
	{
		List<long[]> chunks = new List<long[]>();
		try
		{
			long allocated = 0L;
			try
			{
				for (; allocated + 134217728 <= totalBytes; allocated += 134217728)
				{
					if (token.IsCancellationRequested)
					{
						break;
					}
					chunks.Add(new long[16777216]);
				}
			}
			catch (OutOfMemoryException)
			{
			}
			MemoryAllocatedBytes = allocated;
			if (chunks.Count == 0)
			{
				return;
			}
			ulong pass = 0uL;
			while (!token.IsCancellationRequested)
			{
				pass++;
				long pattern = (long)pass * -7046029254386353131L;
				foreach (long[] c in chunks)
				{
					for (int i = 0; i < c.Length; i++)
					{
						c[i] = pattern ^ i;
					}
					if (token.IsCancellationRequested)
					{
						return;
					}
				}
				foreach (long[] c2 in chunks)
				{
					long errs = 0L;
					for (int j = 0; j < c2.Length; j++)
					{
						if (c2[j] != (pattern ^ j))
						{
							errs++;
						}
					}
					if (errs > 0)
					{
						Interlocked.Add(ref _memErrors, errs);
					}
					if (token.IsCancellationRequested)
					{
						return;
					}
				}
				Interlocked.Increment(ref _memPasses);
			}
		}
		finally
		{
			chunks.Clear();
		}
	}

	private static long FreePhysicalMemory()
	{
		try
		{
			using ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
			using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = s.Get().GetEnumerator();
			if (managementObjectEnumerator.MoveNext())
			{
				return Convert.ToInt64(((ManagementObject)managementObjectEnumerator.Current)["FreePhysicalMemory"]) * 1024;
			}
		}
		catch
		{
		}
		return 1073741824L;
	}
}
