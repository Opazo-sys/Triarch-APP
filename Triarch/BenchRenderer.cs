using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Triarch;

public sealed class BenchRenderer
{
	private enum Mat : byte
	{
		Diffuse,
		Metal,
		Glass,
		Light
	}

	private readonly record struct Sphere(Vector3 C, float R, Mat M, float Albedo, float Rough = 0f);

	private static class Camera
	{
		private static readonly Vector3 Origin;

		private static readonly Vector3 Lower;

		private static readonly Vector3 Horizontal;

		private static readonly Vector3 Vertical;

		static Camera()
		{
			Origin = new Vector3(0f, 2.1f, -7.2f);
			Vector3 target = new Vector3(0f, 0.75f, 0f);
			float aspect = 1.7777778f;
			float hh = MathF.Tan((float)Math.PI * 19f / 90f / 2f);
			float hw = aspect * hh;
			Vector3 w = Vector3.Normalize(Origin - target);
			Vector3 u = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, w));
			Vector3 v = Vector3.Cross(w, u);
			Lower = Origin - hw * u + hh * v - w;
			Horizontal = 2f * hw * u;
			Vertical = -2f * hh * v;
		}

		public static (Vector3 O, Vector3 D) Ray(float s, float t)
		{
			return (O: Origin, D: Vector3.Normalize(Lower + s * Horizontal + t * Vertical - Origin));
		}
	}

	public const int Width = 960;

	public const int Height = 540;

	public const int BucketSize = 32;

	public const int Spp = 1024;

	private const int MaxDepth = 6;

	private long _samples;

	private int _bucketsDone;

	private static readonly Sphere[] Spheres = BuildScene();

	private static readonly Sphere Lamp = new Sphere(new Vector3(-4f, 7f, -3f), 2.2f, Mat.Light, 0f);

	private const float LampPower = 9f;

	public int[] Pixels { get; } = new int[518400];

	public IReadOnlyList<(int X, int Y, int W, int H)> Buckets { get; }

	public int[] Active { get; private set; } = Array.Empty<int>();

	public long SamplesDone => Interlocked.Read(in _samples);

	public int BucketsDone => Volatile.Read(in _bucketsDone);

	public int BucketsTotal { get; private set; }

	public BenchRenderer()
	{
		List<(int, int, int, int)> list = new List<(int, int, int, int)>();
		for (int y = 0; y < 540; y += 32)
		{
			for (int x = 0; x < 960; x += 32)
			{
				list.Add((x, y, Math.Min(32, 960 - x), Math.Min(32, 540 - y)));
			}
		}
		Buckets = list.OrderBy(((int, int, int, int) b) => Math.Pow((double)b.Item1 + (double)b.Item3 / 2.0 - 480.0, 2.0) + Math.Pow(((double)b.Item2 + (double)b.Item4 / 2.0 - 270.0) * 1.6, 2.0)).ToList();
	}

	public BenchResult Run(int threads, TimeSpan minDuration, CancellationToken ct, Action? onPass = null)
	{
		int count = Buckets.Count;
		BucketsTotal = count;
		_samples = 0L;
		int passes = 0;
		Stopwatch sw = Stopwatch.StartNew();
		do
		{
			Array.Fill(Pixels, -16777216);
			_bucketsDone = 0;
			int next = -1;
			Active = Enumerable.Repeat(-1, threads).ToArray();
			Thread[] workers = new Thread[threads];
			for (int t = 0; t < threads; t++)
			{
				int ti = t;
				workers[t] = new Thread(() =>
				{
					int num2;
					while (!ct.IsCancellationRequested && (num2 = Interlocked.Increment(ref next)) < count)
					{
						Active[ti] = num2;
						RenderBucket(Buckets[num2], ct);
						Interlocked.Increment(ref _bucketsDone);
					}
					Active[ti] = -1;
				})
				{
					IsBackground = true,
					Name = $"TRIARCH BENCH {ti}"
				};
				workers[t].Start();
			}
			Thread[] array = workers;
			for (int num = 0; num < array.Length; num++)
			{
				array[num].Join();
			}
			if (ct.IsCancellationRequested)
			{
				break;
			}
			passes++;
			onPass?.Invoke();
		}
		while (sw.Elapsed < minDuration);
		return new BenchResult(threads, sw.Elapsed.TotalSeconds, SamplesDone, passes);
	}

	private void RenderBucket((int X, int Y, int W, int H) b, CancellationToken ct)
	{
		for (int y = b.Y; y < b.Y + b.H; y++)
		{
			if (ct.IsCancellationRequested)
			{
				break;
			}
			var (x, _, _, _) = b;
			for (; x < b.X + b.W; x++)
			{
				uint seed = (uint)((y * 960 + x) * 9781 + -1640531527);
				float sum = 0f;
				for (int s = 0; s < 1024; s++)
				{
					float u = ((float)x + Rnd(ref seed)) / 960f;
					float v = ((float)y + Rnd(ref seed)) / 540f;
					sum += MathF.Min(Trace(Camera.Ray(u, v), ref seed), 4f);
				}
				Pixels[y * 960 + x] = ToPixel(sum / 1024f);
			}
			Interlocked.Add(ref _samples, (long)b.W * 1024L);
		}
	}

	private static Sphere[] BuildScene()
	{
		List<Sphere> s = new List<Sphere>
		{
			new Sphere(new Vector3(0f, 1.1f, 0f), 1.1f, Mat.Glass, 0.98f),
			new Sphere(new Vector3(-2.5f, 0.9f, 0.9f), 0.9f, Mat.Metal, 0.92f, 0.03f),
			new Sphere(new Vector3(2.4f, 0.8f, 0.7f), 0.8f, Mat.Diffuse, 0.85f),
			new Sphere(new Vector3(1f, 0.45f, -1.9f), 0.45f, Mat.Metal, 0.85f, 0.25f),
			new Sphere(new Vector3(-1.1f, 0.35f, -2f), 0.35f, Mat.Diffuse, 0.15f)
		};
		for (int i = 0; i < 14; i++)
		{
			float a = (float)i / 14f * ((float)Math.PI * 2f);
			int num = i % 3;
			s.Add(new Sphere(new Vector3(MathF.Cos(a) * 4.2f, 0.28f, MathF.Sin(a) * 4.2f + 1.5f), 0.28f, num switch
			{
				0 => Mat.Metal, 
				1 => Mat.Diffuse, 
				_ => Mat.Glass, 
			}, (i % 2 == 0) ? 0.9f : 0.3f, 0.1f));
		}
		return s.ToArray();
	}

	private static float Trace((Vector3 O, Vector3 D) ray, ref uint seed)
	{
		float radiance = 0f;
		float throughput = 1f;
		bool specularBounce = true;
		(Vector3 O, Vector3 D) tuple = ray;
		Vector3 o = tuple.O;
		Vector3 d = tuple.D;
		for (int depth = 0; depth < 6; depth++)
		{
			if (!Intersect(o, d, out var t, out var idx, out var lamp))
			{
				radiance += throughput * Sky(d);
				break;
			}
			if (lamp)
			{
				if (specularBounce)
				{
					radiance += throughput * 9f;
				}
				break;
			}
			Vector3 p = o + d * t;
			float rough = 0f;
			Vector3 n;
			Mat mat;
			float albedo;
			if (idx < 0)
			{
				n = Vector3.UnitY;
				mat = Mat.Diffuse;
				albedo = (((((int)MathF.Floor(p.X * 0.9f) + (int)MathF.Floor(p.Z * 0.9f)) & 1) == 0) ? 0.62f : 0.38f);
			}
			else
			{
				Sphere sp = Spheres[idx];
				n = (p - sp.C) / sp.R;
				mat = sp.M;
				albedo = sp.Albedo;
				rough = sp.Rough;
			}
			switch (mat)
			{
			case Mat.Diffuse:
				radiance += throughput * albedo * DirectLight(p, n, ref seed);
				throughput *= albedo;
				d = CosineHemisphere(n, ref seed);
				o = p + n * 0.001f;
				specularBounce = false;
				break;
			case Mat.Metal:
			{
				Vector3 r1 = Vector3.Reflect(d, n) + rough * RandomInSphere(ref seed);
				if (Vector3.Dot(r1, n) <= 0f)
				{
					return radiance;
				}
				d = Vector3.Normalize(r1);
				o = p + n * 0.001f;
				throughput *= albedo;
				specularBounce = true;
				break;
			}
			case Mat.Glass:
			{
				bool flag = Vector3.Dot(d, n) < 0f;
				Vector3 nn = (flag ? n : (-n));
				float eta = (flag ? (2f / 3f) : 1.5f);
				float cos = MathF.Min(0f - Vector3.Dot(d, nn), 1f);
				float sin2 = eta * eta * (1f - cos * cos);
				float r0 = -0.2f;
				r0 *= r0;
				float fresnel = r0 + (1f - r0) * MathF.Pow(1f - cos, 5f);
				if (sin2 > 1f || Rnd(ref seed) < fresnel)
				{
					d = Vector3.Reflect(d, nn);
					o = p + nn * 0.001f;
				}
				else
				{
					d = Vector3.Normalize(eta * d + (eta * cos - MathF.Sqrt(1f - sin2)) * nn);
					o = p - nn * 0.001f;
				}
				throughput *= albedo;
				specularBounce = true;
				break;
			}
			}
			if (depth >= 3)
			{
				float q = MathF.Max(0.05f, 1f - throughput);
				if (Rnd(ref seed) < q)
				{
					break;
				}
				throughput /= 1f - q;
			}
		}
		return radiance;
	}

	private static float DirectLight(Vector3 p, Vector3 n, ref uint seed)
	{
		Vector3 toC = Lamp.C - p;
		float dist2 = toC.LengthSquared();
		float cosMax = MathF.Sqrt(MathF.Max(0f, 1f - Lamp.R * Lamp.R / dist2));
		Vector3 w = toC / MathF.Sqrt(dist2);
		float r1 = Rnd(ref seed);
		float r2 = Rnd(ref seed);
		float cosT = 1f - r1 * (1f - cosMax);
		float sinT = MathF.Sqrt(1f - cosT * cosT);
		float phi = (float)Math.PI * 2f * r2;
		Basis(w, out var a, out var b);
		Vector3 dir = Vector3.Normalize(a * (MathF.Cos(phi) * sinT) + b * (MathF.Sin(phi) * sinT) + w * cosT);
		float cosN = Vector3.Dot(dir, n);
		if (cosN <= 0f)
		{
			return 0f;
		}
		if (!Intersect(p + n * 0.001f, dir, out var _, out var _, out var hitLamp) || !hitLamp)
		{
			return 0f;
		}
		return 9f * cosN * 2f * (1f - cosMax);
	}

	private static bool Intersect(Vector3 o, Vector3 d, out float tMin, out int idx, out bool lamp)
	{
		tMin = float.MaxValue;
		idx = -2;
		lamp = false;
		if (d.Y < -1E-06f)
		{
			float t = (0f - o.Y) / d.Y;
			if (t > 0.0001f && t < 60f)
			{
				tMin = t;
				idx = -1;
			}
		}
		for (int i = 0; i < Spheres.Length; i++)
		{
			if (HitSphere(in Spheres[i], o, d, tMin, out var t2))
			{
				tMin = t2;
				idx = i;
			}
		}
		if (HitSphere(in Lamp, o, d, tMin, out var tl))
		{
			tMin = tl;
			lamp = true;
		}
		return (idx != -2) | lamp;
	}

	private static bool HitSphere(in Sphere s, Vector3 o, Vector3 d, float tMax, out float t)
	{
		Vector3 oc = o - s.C;
		float b = Vector3.Dot(oc, d);
		float c = oc.LengthSquared() - s.R * s.R;
		float disc = b * b - c;
		t = 0f;
		if (disc < 0f)
		{
			return false;
		}
		float sq = MathF.Sqrt(disc);
		t = 0f - b - sq;
		if (t < 0.0001f)
		{
			t = 0f - b + sq;
		}
		if (t > 0.0001f)
		{
			return t < tMax;
		}
		return false;
	}

	private static float Sky(Vector3 d)
	{
		return 0.06f + 0.22f * MathF.Max(0f, d.Y);
	}

	private static float Rnd(ref uint s)
	{
		s ^= s << 13;
		s ^= s >> 17;
		s ^= s << 5;
		return (float)(s >> 8) * 5.9604645E-08f;
	}

	private static Vector3 RandomInSphere(ref uint s)
	{
		Vector3 v;
		do
		{
			v = new Vector3(Rnd(ref s) * 2f - 1f, Rnd(ref s) * 2f - 1f, Rnd(ref s) * 2f - 1f);
		}
		while (!(v.LengthSquared() < 1f));
		return v;
	}

	private static Vector3 CosineHemisphere(Vector3 n, ref uint s)
	{
		float r1 = Rnd(ref s);
		float r2 = Rnd(ref s);
		float r3 = MathF.Sqrt(r1);
		float phi = (float)Math.PI * 2f * r2;
		Basis(n, out var a, out var b);
		return Vector3.Normalize(a * (r3 * MathF.Cos(phi)) + b * (r3 * MathF.Sin(phi)) + n * MathF.Sqrt(1f - r1));
	}

	private static void Basis(Vector3 n, out Vector3 a, out Vector3 b)
	{
		a = Vector3.Normalize(Vector3.Cross((MathF.Abs(n.X) > 0.9f) ? Vector3.UnitY : Vector3.UnitX, n));
		b = Vector3.Cross(n, a);
	}

	private static int ToPixel(float v)
	{
		v *= 1.1f;
		v = v * (2.51f * v + 0.03f) / (v * (2.43f * v + 0.59f) + 0.14f);
		int g = (int)(MathF.Pow(Math.Clamp(v, 0f, 1f), 0.45454544f) * 255f + 0.5f);
		return -16777216 | (g << 16) | (g << 8) | g;
	}
}
