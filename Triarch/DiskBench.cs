using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Triarch;

public static class DiskBench
{
	private const int FILE_FLAG_NO_BUFFERING = 536870912;

	private const int Block = 1048576;

	private const int Small = 4096;

	private const double RandomSeconds = 4.0;

	public static List<(string Root, string Text)> Drives()
	{
		List<(string, string)> list = new List<(string, string)>();
		DriveInfo[] drives = DriveInfo.GetDrives();
		foreach (DriveInfo d in drives)
		{
			try
			{
				if (d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable) && d.TotalFreeSpace >= 1610612736)
				{
					string label = (string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : (" " + d.VolumeLabel));
					list.Add((d.Name, d.Name.TrimEnd('\\') + label));
				}
			}
			catch
			{
			}
		}
		return list;
	}

	public unsafe static DiskBenchResult Run(string driveRoot, long fileMb, Action<string, double> progress, CancellationToken ct)
	{
		long fileBytes = fileMb << 20;
		if (new DriveInfo(driveRoot).TotalFreeSpace < fileBytes + 536870912)
		{
			throw new IOException($"Pouco espaço livre em {driveRoot} (precisa de {fileMb + 512} MB).");
		}
		string dir = Path.Combine(driveRoot, "TriarchBench");
		string path = Path.Combine(dir, "bench.tmp");
		Directory.CreateDirectory(dir);
		void* seqMem = NativeMemory.AlignedAlloc(1048576u, 4096u);
		try
		{
			Span<byte> seq = new Span<byte>(seqMem, 1048576);
			new Random(42).NextBytes(seq);
			using SafeFileHandle h = File.OpenHandle(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, (FileOptions)(-1610612736), fileBytes);
			Stopwatch sw = Stopwatch.StartNew();
			for (long off = 0L; off < fileBytes; off += 1048576)
			{
				ct.ThrowIfCancellationRequested();
				RandomAccess.Write(h, seq, off);
				progress("Escrita sequencial (1 MB)", 0.0 + 0.25 * (double)off / (double)fileBytes);
			}
			double seqWrite = (double)fileMb / sw.Elapsed.TotalSeconds;
			sw.Restart();
			for (long off2 = 0L; off2 < fileBytes; off2 += 1048576)
			{
				ct.ThrowIfCancellationRequested();
				RandomAccess.Read(h, seq, off2);
				progress("Leitura sequencial (1 MB)", 0.25 + 0.25 * (double)off2 / (double)fileBytes);
			}
			double seqRead = (double)fileMb / sw.Elapsed.TotalSeconds;
			long slots = fileBytes / 4096;
			Span<byte> small = new Span<byte>(seqMem, 4096);
			double readIops = RandomLoop(h, small, slots, write: false, progress, "Leitura 4 KB aleatória (1 por vez)", 0.5, 0.15, ct, out var readLat);
			double writeIops = RandomLoop(h, small, slots, write: true, progress, "Escrita 4 KB aleatória (1 por vez)", 0.65, 0.15, ct, out var writeLat);
			progress("Leitura 4 KB aleatória (8 em paralelo)", 0.8);
			double q8 = ParallelRead(h, slots, 8, ct, progress);
			progress("Concluído", 1.0);
			return new DiskBenchResult(driveRoot.TrimEnd('\\'), fileMb, seqRead, seqWrite, readIops, writeIops, readLat, writeLat, q8);
		}
		finally
		{
			NativeMemory.AlignedFree(seqMem);
			try
			{
				File.Delete(path);
			}
			catch
			{
			}
			try
			{
				Directory.Delete(dir);
			}
			catch
			{
			}
		}
	}

	private static double RandomLoop(SafeFileHandle h, Span<byte> buf, long slots, bool write, Action<string, double> progress, string phase, double start, double span, CancellationToken ct, out double latencyUs)
	{
		Random rng = new Random(write ? 7 : 3);
		long ops = 0L;
		Stopwatch sw = Stopwatch.StartNew();
		while (sw.Elapsed.TotalSeconds < 4.0)
		{
			for (int i = 0; i < 64; i++)
			{
				long off = rng.NextInt64(slots) * 4096;
				if (write)
				{
					RandomAccess.Write(h, buf, off);
				}
				else
				{
					RandomAccess.Read(h, buf, off);
				}
				ops++;
			}
			ct.ThrowIfCancellationRequested();
			progress(phase, start + span * Math.Min(1.0, sw.Elapsed.TotalSeconds / 4.0));
		}
		double secs = sw.Elapsed.TotalSeconds;
		latencyUs = secs / (double)ops * 1000000.0;
		return (double)ops / secs;
	}

	private unsafe static double ParallelRead(SafeFileHandle h, long slots, int threads, CancellationToken ct, Action<string, double> progress)
	{
		long total = 0L;
		Stopwatch sw = Stopwatch.StartNew();
		Task[] tasks = new Task[threads];
		for (int t = 0; t < threads; t++)
		{
			int seed = 100 + t;
			tasks[t] = Task.Factory.StartNew(() =>
			{
				void* ptr = NativeMemory.AlignedAlloc(4096u, 4096u);
				try
				{
					Span<byte> buffer = new Span<byte>(ptr, 4096);
					Random random = new Random(seed);
					long num = 0L;
					while (sw.Elapsed.TotalSeconds < 4.0 && !ct.IsCancellationRequested)
					{
						for (int i = 0; i < 32; i++)
						{
							RandomAccess.Read(h, buffer, random.NextInt64(slots) * 4096);
							num++;
						}
					}
					Interlocked.Add(ref total, num);
				}
				finally
				{
					NativeMemory.AlignedFree(ptr);
				}
			}, TaskCreationOptions.LongRunning);
		}
		while (!Task.WaitAll(tasks, 150))
		{
			progress("Leitura 4 KB aleatória (8 em paralelo)", 0.8 + 0.19 * Math.Min(1.0, sw.Elapsed.TotalSeconds / 4.0));
		}
		ct.ThrowIfCancellationRequested();
		return (double)total / sw.Elapsed.TotalSeconds;
	}
}
