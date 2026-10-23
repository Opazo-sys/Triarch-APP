using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Triarch;

public sealed class GameSimBench
{
	public const int Entities = 30000;

	public const float World = 1000f;

	private const int GridN = 64;

	private const float Cell = 15.625f;

	private const float Radius = 15.625f;

	private const float Dt = 1f / 60f;

	private float[] _x = new float[30000];

	private float[] _y = new float[30000];

	private float[] _vx = new float[30000];

	private float[] _vy = new float[30000];

	private float[] _nx = new float[30000];

	private float[] _ny = new float[30000];

	private float[] _nvx = new float[30000];

	private float[] _nvy = new float[30000];

	private readonly float[] _hp = new float[30000];

	private readonly float[] _dmg = new float[30000];

	private readonly byte[] _team = new byte[30000];

	private readonly int[] _cellStart = new int[4097];

	private readonly int[] _sorted = new int[30000];

	private readonly int[] _cellOf = new int[30000];

	private readonly float[] _drawKey = new float[30000];

	private readonly int[] _drawList = new int[30000];

	private uint _rng;

	private int _frames;

	public float[] ViewX => _x;

	public float[] ViewY => _y;

	public byte[] ViewTeam => _team;

	public int FramesDone => Volatile.Read(in _frames);

	public double Progress { get; private set; }

	public double CurrentFps { get; private set; }

	public GameSimResult Run(TimeSpan duration, CancellationToken ct)
	{
		Reset();
		Stopwatch warm = Stopwatch.StartNew();
		while (warm.Elapsed.TotalSeconds < 2.0 && !ct.IsCancellationRequested)
		{
			Step(out var _, out var _);
		}
		List<double> frameTimes = new List<double>(20000);
		double mainMs2 = 0.0;
		double parMs2 = 0.0;
		_frames = 0;
		Stopwatch sw = Stopwatch.StartNew();
		Stopwatch fpsWindow = Stopwatch.StartNew();
		int fpsFrames = 0;
		while (sw.Elapsed < duration && !ct.IsCancellationRequested)
		{
			long t0 = Stopwatch.GetTimestamp();
			Step(out var m, out var p);
			frameTimes.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
			mainMs2 += m;
			parMs2 += p;
			Interlocked.Increment(ref _frames);
			fpsFrames++;
			if (fpsWindow.ElapsedMilliseconds >= 500)
			{
				CurrentFps = (double)fpsFrames / fpsWindow.Elapsed.TotalSeconds;
				fpsFrames = 0;
				fpsWindow.Restart();
			}
			Progress = sw.Elapsed / duration;
		}
		int n = Math.Max(1, frameTimes.Count);
		double[] sorted = frameTimes.OrderBy((double x) => x).ToArray();
		double p99 = ((sorted.Length != 0) ? sorted[Math.Min(sorted.Length - 1, (int)((double)sorted.Length * 0.99))] : 0.0);
		return new GameSimResult((double)frameTimes.Count / sw.Elapsed.TotalSeconds, (p99 > 0.0) ? (1000.0 / p99) : 0.0, mainMs2 / (double)n, parMs2 / (double)n, frameTimes.Count, sw.Elapsed.TotalSeconds);
	}

	private void Reset()
	{
		_rng = 12345u;
		for (int i = 0; i < 30000; i++)
		{
			_team[i] = (byte)(i & 1);
			_x[i] = ((_team[i] == 0) ? 0.05f : 0.55f) * 1000f + Rnd() * 1000f * 0.4f;
			_y[i] = Rnd() * 1000f;
			_vx[i] = (float)((_team[i] == 0) ? 1 : (-1)) * (20f + Rnd() * 10f);
			_vy[i] = (Rnd() - 0.5f) * 10f;
			_hp[i] = 100f;
		}
	}

	private void Step(out double mainMs, out double parMs)
	{
		long t0 = Stopwatch.GetTimestamp();
		BuildGrid();
		long t1 = Stopwatch.GetTimestamp();
		Parallel.For(0, Environment.ProcessorCount * 4, (int chunk) =>
		{
			int num = (30000 + Environment.ProcessorCount * 4 - 1) / (Environment.ProcessorCount * 4);
			int num2 = Math.Min(30000, (chunk + 1) * num);
			for (int i = chunk * num; i < num2; i++)
			{
				Simulate(i);
			}
		});
		long t2 = Stopwatch.GetTimestamp();
		float[] nx = _nx;
		float[] x = _x;
		_x = nx;
		_nx = x;
		x = _ny;
		nx = _y;
		_y = x;
		_ny = nx;
		nx = _nvx;
		x = _vx;
		_vx = nx;
		_nvx = x;
		x = _nvy;
		nx = _vy;
		_vy = x;
		_nvy = nx;
		GameLogic();
		PrepareDraw();
		long t3 = Stopwatch.GetTimestamp();
		mainMs = (double)(t1 - t0 + t3 - t2) * 1000.0 / (double)Stopwatch.Frequency;
		parMs = (double)(t2 - t1) * 1000.0 / (double)Stopwatch.Frequency;
	}

	private void BuildGrid()
	{
		Array.Clear(_cellStart);
		for (int i = 0; i < 30000; i++)
		{
			int cx = Math.Clamp((int)(_x[i] / 15.625f), 0, 63);
			int c = Math.Clamp((int)(_y[i] / 15.625f), 0, 63) * 64 + cx;
			_cellOf[i] = c;
			_cellStart[c + 1]++;
		}
		for (int j = 0; j < 4096; j++)
		{
			_cellStart[j + 1] += _cellStart[j];
		}
		int[] fill = (int[])_cellStart.Clone();
		for (int k = 0; k < 30000; k++)
		{
			_sorted[fill[_cellOf[k]]++] = k;
		}
	}

	private void Simulate(int i)
	{
		float x = _x[i];
		float y = _y[i];
		float vx = _vx[i];
		float vy = _vy[i];
		byte team = _team[i];
		float sepX = 0f;
		float sepY = 0f;
		float aliX = 0f;
		float aliY = 0f;
		float cohX = 0f;
		float cohY = 0f;
		float dmg = 0f;
		float bestD2 = 244.14062f;
		float enemyX = 0f;
		float enemyY = 0f;
		int allies = 0;
		bool enemy = false;
		int num = _cellOf[i];
		int cx = num % 64;
		int cy = num / 64;
		for (int gy = Math.Max(0, cy - 1); gy <= Math.Min(63, cy + 1); gy++)
		{
			for (int gx = Math.Max(0, cx - 1); gx <= Math.Min(63, cx + 1); gx++)
			{
				int cell = gy * 64 + gx;
				for (int k = _cellStart[cell]; k < _cellStart[cell + 1]; k++)
				{
					int j = _sorted[k];
					if (j == i)
					{
						continue;
					}
					float dx = _x[j] - x;
					float dy = _y[j] - y;
					float d2 = dx * dx + dy * dy;
					if (d2 > 244.14062f || d2 < 1E-06f)
					{
						continue;
					}
					float inv = 1f / MathF.Sqrt(d2);
					if (d2 < 9f)
					{
						sepX -= dx * inv;
						sepY -= dy * inv;
					}
					if (_team[j] == team)
					{
						aliX += _vx[j];
						aliY += _vy[j];
						cohX += dx;
						cohY += dy;
						allies++;
						continue;
					}
					if (d2 < 16f)
					{
						dmg += 1f / 15f * (1f - MathF.Sqrt(d2) / 4f);
					}
					if (d2 < bestD2)
					{
						bestD2 = d2;
						enemyX = dx;
						enemyY = dy;
						enemy = true;
					}
				}
			}
		}
		float ax = sepX * 60f;
		float ay = sepY * 60f;
		if (allies > 0)
		{
			ax += (aliX / (float)allies - vx) * 0.5f + cohX / (float)allies * 0.3f;
			ay += (aliY / (float)allies - vy) * 0.5f + cohY / (float)allies * 0.3f;
		}
		if (enemy)
		{
			float inv2 = 1f / MathF.Sqrt(bestD2);
			float dir = ((!(_hp[i] < 30f)) ? 1 : (-1));
			ax += dir * enemyX * inv2 * 80f;
			ay += dir * enemyY * inv2 * 80f;
		}
		else
		{
			ax += (float)(((team == 0) ? 1 : (-1)) * 25);
		}
		vx += ax * (1f / 60f);
		vy += ay * (1f / 60f);
		float sp = MathF.Sqrt(vx * vx + vy * vy);
		if (sp > 60f)
		{
			vx *= 60f / sp;
			vy *= 60f / sp;
		}
		x += vx * (1f / 60f);
		y += vy * (1f / 60f);
		if (x < 0f || x > 1000f)
		{
			vx = 0f - vx;
			x = Math.Clamp(x, 0f, 1000f);
		}
		if (y < 0f || y > 1000f)
		{
			vy = 0f - vy;
			y = Math.Clamp(y, 0f, 1000f);
		}
		_nx[i] = x;
		_ny[i] = y;
		_nvx[i] = vx;
		_nvy[i] = vy;
		_dmg[i] = dmg;
	}

	private void GameLogic()
	{
		for (int i = 0; i < 30000; i++)
		{
			_hp[i] = MathF.Min(100f, _hp[i] - _dmg[i] + 1f / 120f);
			bool scored = ((_team[i] == 0) ? (_x[i] > 970f) : (_x[i] < 30f));
			if (!(_hp[i] > 0f) || scored)
			{
				_hp[i] = 100f;
				_x[i] = ((_team[i] == 0) ? 0.02f : 0.98f) * 1000f;
				_y[i] = Rnd() * 1000f;
				_vx[i] = ((_team[i] == 0) ? 1 : (-1)) * 25;
				_vy[i] = 0f;
			}
		}
	}

	private void PrepareDraw()
	{
		float camX = 500f + MathF.Sin((float)_frames * 0.01f) * 200f;
		float camY = -200f;
		int visible = 0;
		for (int i = 0; i < 30000; i++)
		{
			float dx = _x[i] - camX;
			float dy = _y[i] - camY;
			if (!(dy <= 0f) && !(MathF.Abs(dx) > dy * 1.2f))
			{
				_drawKey[visible] = 0f - dy;
				_drawList[visible++] = i;
			}
		}
		Array.Sort(_drawKey, _drawList, 0, visible);
	}

	private float Rnd()
	{
		_rng ^= _rng << 13;
		_rng ^= _rng >> 17;
		_rng ^= _rng << 5;
		return (float)(_rng >> 8) * 5.9604645E-08f;
	}
}
