using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Triarch;

public static class MemoryBench
{
	private const long LatencyBytes = 268435456L;

	private const long BandwidthBytes = 1073741824L;

	private const long PackBytes = 536870912L;

	private const int AssetCount = 1000;

	private const int AssetBytes = 65536;

	private const int FILE_FLAG_NO_BUFFERING = 536870912;

	public static double MeasureLatencyNs(CancellationToken ct)
	{
		long[] next = new long[33554432];
		int lines = 33554432 / 8;
		int[] order = new int[lines];
		for (int i = 0; i < lines; i++)
		{
			order[i] = i;
		}
		Random rng = new Random(42);
		for (int i2 = lines - 1; i2 > 0; i2--)
		{
			int j = rng.Next(i2);
			ref int reference = ref order[i2];
			ref int reference2 = ref order[j];
			int num = order[j];
			int num2 = order[i2];
			reference = num;
			reference2 = num2;
		}
		for (int k = 0; k < lines; k++)
		{
			next[order[k] * 8] = order[(k + 1) % lines] * 8;
		}
		long p = 0L;
		for (int l = 0; l < 200000; l++)
		{
			p = next[p];
		}
		Stopwatch sw = Stopwatch.StartNew();
		for (int m = 0; m < 20000000; m++)
		{
			p = next[p];
			if ((m & 0xFFFFF) == 0 && ct.IsCancellationRequested)
			{
				break;
			}
		}
		double result = sw.Elapsed.TotalMilliseconds * 1000000.0 / 20000000.0;
		GC.KeepAlive(p);
		return result;
	}

	public static (double Read, double Copy) MeasureBandwidth(CancellationToken ct)
	{
		int threads = Environment.ProcessorCount;
		int n = 134217728;
		long[] src = new long[n];
		long[] dst = new long[n];
		Parallel.For(0, threads, (int t) =>
		{
			int num = n / threads;
			Array.Fill(src, t + 1, t * num, num);
		});
		double bestRead = 0.0;
		double bestCopy = 0.0;
		for (int rep = 0; rep < 3; rep++)
		{
			if (ct.IsCancellationRequested)
			{
				break;
			}
			Stopwatch sw = Stopwatch.StartNew();
			long total = 0L;
			Parallel.For(0, threads, () => 0L, (int t, ParallelLoopState _, long acc) =>
			{
				int num = n / threads;
				ReadOnlySpan<long> readOnlySpan = new ReadOnlySpan<long>(src, t * num, num);
				long num2 = 0L;
				long num3 = 0L;
				long num4 = 0L;
				long num5 = 0L;
				for (int i = 0; i + 3 < readOnlySpan.Length; i += 4)
				{
					num2 += readOnlySpan[i];
					num3 += readOnlySpan[i + 1];
					num4 += readOnlySpan[i + 2];
					num5 += readOnlySpan[i + 3];
				}
				return acc + num2 + num3 + num4 + num5;
			}, (long acc) =>
			{
				Interlocked.Add(ref total, acc);
			});
			bestRead = Math.Max(bestRead, 1073741824.0 / sw.Elapsed.TotalSeconds / 1000000000.0);
			sw.Restart();
			Parallel.For(0, threads, (int t) =>
			{
				int num = n / threads;
				Array.Copy(src, t * num, dst, t * num, num);
			});
			bestCopy = Math.Max(bestCopy, 2147483648.0 / sw.Elapsed.TotalSeconds / 1000000000.0);
		}
		return (Read: bestRead, Copy: bestCopy);
	}

	public unsafe static LoadResult MeasureLoading(Action<string, double> progress, CancellationToken ct)
	{
		string dir = Path.Combine(Path.GetTempPath(), "TriarchBench");
		string drive = Path.GetPathRoot(dir) ?? "";
		try
		{
			Directory.CreateDirectory(dir);
			string pack = Path.Combine(dir, "mapa.pak");
			byte[] block = new byte[4194304];
			new Random(7).NextBytes(block);
			using (FileStream fs = new FileStream(pack, FileMode.Create, FileAccess.Write, FileShare.None, 1048576, FileOptions.WriteThrough))
			{
				for (long w = 0L; w < 536870912; w += block.Length)
				{
					if (ct.IsCancellationRequested)
					{
						break;
					}
					fs.Write(block);
					progress("Preparando arquivos de teste", (double)w / 602406912.0);
				}
			}
			for (int i = 0; i < 1000; i++)
			{
				if (ct.IsCancellationRequested)
				{
					break;
				}
				File.WriteAllBytes(Path.Combine(dir, $"asset{i:D4}.dat"), block.AsSpan(0, 65536).ToArray());
				if (i % 50 == 0)
				{
					progress("Preparando arquivos de teste", (536870912.0 + (double)i * 65536.0) / 602406912.0);
				}
			}
			ct.ThrowIfCancellationRequested();
			void* mem = NativeMemory.AlignedAlloc(4194304u, 4096u);
			try
			{
				Span<byte> buf = new Span<byte>(mem, 4194304);
				Stopwatch sw = Stopwatch.StartNew();
				using (SafeFileHandle h = File.OpenHandle(pack, FileMode.Open, FileAccess.Read, FileShare.Read, (FileOptions)536870912, 0L))
				{
					for (long off = 0L; off < 536870912; off += 4194304)
					{
						if (ct.IsCancellationRequested)
						{
							break;
						}
						RandomAccess.Read(h, buf, off);
						progress("Lendo pacote do mapa", (double)off / 536870912.0);
					}
				}
				double seqMBs = 512.0 / sw.Elapsed.TotalSeconds;
				progress("Antivírus verificando os arquivos novos", 0.0);
				ReadAssets(dir, 8, ct);
				sw.Restart();
				progress("Lendo assets pequenos (8 threads)", 0.5);
				ReadAssets(dir, 8, ct);
				double secs = sw.Elapsed.TotalSeconds;
				double mapSeconds = 512.0 / seqMBs + secs;
				double smallMBs = 62.5 / secs;
				double filesPerSec = 1000.0 / secs;
				ct.ThrowIfCancellationRequested();
				return new LoadResult(seqMBs, smallMBs, filesPerSec, mapSeconds, drive);
			}
			finally
			{
				NativeMemory.AlignedFree(mem);
			}
		}
		finally
		{
			try
			{
				Directory.Delete(dir, recursive: true);
			}
			catch
			{
			}
		}
	}

	private unsafe static void ReadAssets(string dir, int threads, CancellationToken ct)
	{
		Parallel.For(0, threads, new ParallelOptions
		{
			MaxDegreeOfParallelism = threads
		}, (int t) =>
		{
			void* ptr = NativeMemory.AlignedAlloc(65536u, 4096u);
			try
			{
				Span<byte> buffer = new Span<byte>(ptr, 65536);
				for (int i = t; i < 1000; i += threads)
				{
					if (ct.IsCancellationRequested)
					{
						break;
					}
					using SafeFileHandle handle = File.OpenHandle(Path.Combine(dir, $"asset{i:D4}.dat"), FileMode.Open, FileAccess.Read, FileShare.Read, (FileOptions)536870912, 0L);
					RandomAccess.Read(handle, buffer, 0L);
				}
			}
			finally
			{
				NativeMemory.AlignedFree(ptr);
			}
		});
	}
}
