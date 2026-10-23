using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Dxc;
using Vortice.DXGI;

namespace Triarch;

public sealed class GpuRtBench
{
	private struct Vtx
	{
		public Vector3 Pos;

		public Vector3 Nrm;
	}

	private struct MeshInfo
	{
		public uint IndexOffset;

		public uint VertexOffset;

		public uint Pad0;

		public uint Pad1;
	}

	private struct InstMat
	{
		public Vector3 Albedo;

		public float Metal;

		public float Rough;

		public float Emissive;

		public uint Mesh;

		public float Pad;
	}

	private struct InstanceDesc
	{
		public float M00;

		public float M01;

		public float M02;

		public float M03;

		public float M10;

		public float M11;

		public float M12;

		public float M13;

		public float M20;

		public float M21;

		public float M22;

		public float M23;

		public uint IdMask;

		public uint ContribFlags;

		public ulong Blas;
	}

	private struct FrameCB
	{
		public Matrix4x4 InvViewProj;

		public Vector3 CamPos;

		public float Time;

		public Vector3 SunDir;

		public uint FrameIndex;

		public uint Width;

		public uint Height;

		public uint Spp;

		public uint Bounces;
	}

	private sealed class MeshBuilder
	{
		public readonly List<Vtx> V = new List<Vtx>();

		public readonly List<uint> I = new List<uint>();

		public readonly List<(uint IndexOffset, uint IndexCount, uint VertexOffset, uint VertexCount)> Meshes = new List<(uint, uint, uint, uint)>();

		private int _vStart;

		private int _iStart;

		public void Begin()
		{
			_vStart = V.Count;
			_iStart = I.Count;
		}

		public void End()
		{
			Meshes.Add(((uint)_iStart, (uint)(I.Count - _iStart), (uint)_vStart, (uint)(V.Count - _vStart)));
		}

		public uint Add(Vector3 p, Vector3 n)
		{
			V.Add(new Vtx
			{
				Pos = p,
				Nrm = Vector3.Normalize(n)
			});
			return (uint)(V.Count - 1 - _vStart);
		}

		public void Tri(uint a, uint b, uint c)
		{
			I.Add(a);
			I.Add(b);
			I.Add(c);
		}
	}

	private sealed record Obj(int Mesh, Vector3 Pos, float Scale, Vector3 Scale3, float Spin, Vector3 Axis, InstMat Mat);

	private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct WNDCLASSEX
	{
		public uint cbSize;

		public uint style;

		public nint lpfnWndProc;

		public int cbClsExtra;

		public int cbWndExtra;

		public nint hInstance;

		public nint hIcon;

		public nint hCursor;

		public nint hbrBackground;

		public string? lpszMenuName;

		public string lpszClassName;

		public nint hIconSm;
	}

	private struct MSG
	{
		public nint hwnd;

		public uint message;

		public nint wParam;

		public nint lParam;

		public uint time;

		public int x;

		public int y;
	}

	private struct RECT
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private volatile bool _stop;

	private WndProc? _wndProc;

	private const int MeshGround = 0;

	private const int MeshSphere = 1;

	private const int MeshHeroSphere = 2;

	private const int MeshTorus = 3;

	private const int MeshColumn = 4;

	private const int MeshBox = 5;

	private const int ObjectCount = 4000;

	private const float World = 220f;

	private const string Hlsl = "#define RS \"CBV(b0), SRV(t0), SRV(t1), SRV(t2), SRV(t3), SRV(t4), DescriptorTable(UAV(u0))\"\r\nstruct Frame { row_major float4x4 InvViewProj; float3 CamPos; float Time; float3 SunDir; uint FrameIndex; uint Width; uint Height; uint Spp; uint Bounces; };\r\nConstantBuffer<Frame> F : register(b0);\r\nRaytracingAccelerationStructure Scene : register(t0);\r\nstruct Vtx { float3 pos; float3 nrm; };\r\nStructuredBuffer<Vtx> Verts : register(t1);\r\nStructuredBuffer<uint> Indices : register(t2);\r\nstruct Mesh { uint indexOffset; uint vertexOffset; uint p0; uint p1; };\r\nStructuredBuffer<Mesh> Meshes : register(t3);\r\nstruct Inst { float3 albedo; float metal; float rough; float emissive; uint mesh; float pad; };\r\nStructuredBuffer<Inst> Insts : register(t4);\r\nRWTexture2D<float4> Output : register(u0);\r\n\r\nuint Pcg(inout uint s) { s = s * 747796405u + 2891336453u; uint w = ((s >> ((s >> 28u) + 4u)) ^ s) * 277803737u; return (w >> 22u) ^ w; }\r\nfloat Rnd(inout uint s) { return (Pcg(s) & 0xFFFFFF) / 16777216.0; }\r\n\r\nfloat3 CosineDir(float3 n, inout uint s)\r\n{\r\n    float u = Rnd(s), v = Rnd(s), r = sqrt(u), phi = 6.2831853 * v;\r\n    float3 t = abs(n.y) < 0.99 ? normalize(cross(n, float3(0, 1, 0))) : float3(1, 0, 0);\r\n    float3 b = cross(n, t);\r\n    return normalize(t * (r * cos(phi)) + b * (r * sin(phi)) + n * sqrt(max(0, 1 - u)));\r\n}\r\n\r\nfloat3 Sky(float3 d)\r\n{\r\n    float3 sky = lerp(float3(0.85, 0.9, 1.0), float3(0.25, 0.45, 0.85), saturate(d.y));\r\n    float sun = pow(saturate(dot(d, -F.SunDir)), 900) * 60;\r\n    return sky * 0.8 + sun * float3(1.0, 0.92, 0.8);\r\n}\r\n\r\nstruct Hit { float3 pos; float3 nrm; Inst mat; };\r\n\r\nbool Closest(float3 o, float3 d, out Hit h)\r\n{\r\n    RayQuery<RAY_FLAG_FORCE_OPAQUE> q;\r\n    RayDesc r; r.Origin = o; r.TMin = 0.002; r.Direction = d; r.TMax = 2000;\r\n    q.TraceRayInline(Scene, RAY_FLAG_NONE, 0xFF, r);\r\n    q.Proceed();\r\n    h = (Hit)0;\r\n    if (q.CommittedStatus() != COMMITTED_TRIANGLE_HIT) return false;\r\n    Inst m = Insts[q.CommittedInstanceID()];\r\n    Mesh me = Meshes[m.mesh];\r\n    uint i0 = me.indexOffset + q.CommittedPrimitiveIndex() * 3;\r\n    float2 bc = q.CommittedTriangleBarycentrics();\r\n    float3 n0 = Verts[me.vertexOffset + Indices[i0]].nrm, n1 = Verts[me.vertexOffset + Indices[i0 + 1]].nrm, n2 = Verts[me.vertexOffset + Indices[i0 + 2]].nrm;\r\n    float3 n = n0 * (1 - bc.x - bc.y) + n1 * bc.x + n2 * bc.y;\r\n    float3x4 o2w = q.CommittedObjectToWorld3x4();\r\n    n = normalize(mul((float3x3)o2w, n));\r\n    if (dot(n, d) > 0) n = -n;\r\n    h.pos = o + d * q.CommittedRayT() + n * 0.003;\r\n    h.nrm = n; h.mat = m;\r\n    return true;\r\n}\r\n\r\nbool Occluded(float3 o, float3 d, float tmax)\r\n{\r\n    RayQuery<RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_CLOSEST_HIT_SHADER> q;\r\n    RayDesc r; r.Origin = o; r.TMin = 0.002; r.Direction = d; r.TMax = tmax;\r\n    q.TraceRayInline(Scene, RAY_FLAG_NONE, 0xFF, r);\r\n    q.Proceed();\r\n    return q.CommittedStatus() != COMMITTED_NOTHING;\r\n}\r\n\r\n[RootSignature(RS)]\r\n[numthreads(8, 8, 1)]\r\nvoid Main(uint3 id : SV_DispatchThreadID)\r\n{\r\n    if (id.x >= F.Width || id.y >= F.Height) return;\r\n    uint seed = (id.y * F.Width + id.x) * 9781u + F.FrameIndex * 6271u;\r\n    float3 color = 0;\r\n    for (uint s = 0; s < F.Spp; s++)\r\n    {\r\n        float2 uv = (float2(id.xy) + float2(Rnd(seed), Rnd(seed))) / float2(F.Width, F.Height);\r\n        float4 p = mul(float4(uv.x * 2 - 1, 1 - uv.y * 2, 1, 1), F.InvViewProj);\r\n        float3 o = F.CamPos, d = normalize(p.xyz / p.w - F.CamPos);\r\n        float3 through = 1, acc = 0;\r\n        for (uint b = 0; b < F.Bounces; b++)\r\n        {\r\n            Hit h;\r\n            if (!Closest(o, d, h)) { acc += through * Sky(d); break; }\r\n            acc += through * h.mat.albedo * h.mat.emissive;\r\n            // Sombra suave do sol (direção sorteada num cone) + oclusão de ambiente (um raio no hemisfério).\r\n            float3 sd = normalize(-F.SunDir + (float3(Rnd(seed), Rnd(seed), Rnd(seed)) - 0.5) * 0.06);\r\n            float ndl = saturate(dot(h.nrm, sd));\r\n            float sunVis = ndl > 0 && !Occluded(h.pos, sd, 1000) ? 1 : 0;\r\n            float3 ad = CosineDir(h.nrm, seed);\r\n            float ao = Occluded(h.pos, ad, 6) ? 0.15 : 1;\r\n            float3 diffuse = h.mat.albedo * (1 - h.mat.metal);\r\n            acc += through * diffuse * (ndl * sunVis * float3(3.6, 3.3, 2.9) + ao * float3(0.12, 0.15, 0.2));\r\n            // Próxima rebatida: reflexo (metal) ou iluminação indireta (difuso).\r\n            if (h.mat.metal > 0.5)\r\n            {\r\n                float3 rd = reflect(d, h.nrm);\r\n                d = normalize(rd + (CosineDir(h.nrm, seed) - h.nrm) * h.mat.rough);\r\n                through *= h.mat.albedo;\r\n            }\r\n            else\r\n            {\r\n                d = CosineDir(h.nrm, seed);\r\n                through *= h.mat.albedo * 0.8;\r\n            }\r\n            o = h.pos;\r\n            if (max(through.x, max(through.y, through.z)) < 0.02) break;\r\n        }\r\n        color += acc;\r\n    }\r\n    color /= F.Spp;\r\n    color = color / (1 + color);               // tone mapping (Reinhard)\r\n    color = pow(saturate(color), 1 / 2.2);     // gama\r\n    Output[id.xy] = float4(color, 1);\r\n}";

	public int Width { get; init; } = 1920;

	public int Height { get; init; } = 1080;

	public string Preset { get; init; } = "alta";

	public TimeSpan PassDuration { get; init; } = TimeSpan.FromSeconds(30.0);

	public int Passes { get; init; } = 3;

	public TimeSpan Warmup { get; init; } = TimeSpan.FromSeconds(10.0);

	public string AdapterName { get; init; } = "";

	public double Progress { get; private set; }

	public double CurrentFps { get; private set; }

	public string Phase { get; private set; } = "";

	public string? Error { get; private set; }

	public bool Cancelled { get; private set; }

	private (uint Spp, uint Bounces) Quality
	{
		get
		{
			string preset = Preset;
			if (!(preset == "normal"))
			{
				if (preset == "extrema")
				{
					return (Spp: 8u, Bounces: 4u);
				}
				return (Spp: 4u, Bounces: 3u);
			}
			return (Spp: 2u, Bounces: 2u);
		}
	}

	private double RaysPerPixel => (double)(Quality.Spp * Quality.Bounces) * 3.0;

	public void Cancel()
	{
		_stop = true;
	}

	public static string? CheckSupport(string adapterName)
	{
		try
		{
			using IDXGIFactory4 factory = DXGI.CreateDXGIFactory1<IDXGIFactory4>();
			string desc;
			using IDXGIAdapter1 adapter = PickAdapter(factory, adapterName, out desc);
			using ID3D12Device5 device = D3D12.D3D12CreateDevice<ID3D12Device5>(adapter, FeatureLevel.Level_12_0);
			return (device.Options5.RaytracingTier >= RaytracingTier.Tier1_1) ? null : "Esta placa não tem ray tracing por hardware (DXR 1.1). Precisa de RTX 20 ou mais nova, Radeon RX 6000 ou mais nova, ou Intel Arc.";
		}
		catch (Exception ex)
		{
			return "Direct3D 12 indisponível: " + ex.Message;
		}
	}

	private static IDXGIAdapter1 PickAdapter(IDXGIFactory4 factory, string name, out string desc)
	{
		List<IDXGIAdapter1> list = new List<IDXGIAdapter1>();
		IDXGIAdapter1 a;
		for (uint i = 0u; factory.EnumAdapters1(i, out a).Success; i++)
		{
			if ((a.Description1.Flags & AdapterFlags.Software) == 0)
			{
				list.Add(a);
			}
			else
			{
				a.Dispose();
			}
		}
		if (list.Count == 0)
		{
			throw new InvalidOperationException("Nenhuma placa de vídeo encontrada.");
		}
		IDXGIAdapter1 pick = list.FirstOrDefault((IDXGIAdapter1 iDXGIAdapter) => name.Length > 0 && iDXGIAdapter.Description1.Description.Contains(name, StringComparison.OrdinalIgnoreCase)) ?? list.OrderByDescending((IDXGIAdapter1 iDXGIAdapter) => iDXGIAdapter.Description1.DedicatedVideoMemory).First();
		foreach (IDXGIAdapter1 a2 in list)
		{
			if (a2 != pick)
			{
				a2.Dispose();
			}
		}
		desc = pick.Description1.Description;
		return pick;
	}

	private static void Plane(MeshBuilder b, float size, int n)
	{
		b.Begin();
		for (int z = 0; z <= n; z++)
		{
			for (int x = 0; x <= n; x++)
			{
				b.Add(new Vector3(((float)x / (float)n - 0.5f) * size, 0f, ((float)z / (float)n - 0.5f) * size), Vector3.UnitY);
			}
		}
		for (int i = 0; i < n; i++)
		{
			for (int j = 0; j < n; j++)
			{
				uint a = (uint)(i * (n + 1) + j);
				uint c = a + (uint)(n + 1);
				b.Tri(a, c, a + 1);
				b.Tri(a + 1, c, c + 1);
			}
		}
		b.End();
	}

	private static void Sphere(MeshBuilder b, int seg)
	{
		b.Begin();
		int rings = seg / 2;
		for (int r = 0; r <= rings; r++)
		{
			for (int s = 0; s <= seg; s++)
			{
				float th = (float)Math.PI * (float)r / (float)rings;
				float ph = (float)Math.PI * 2f * (float)s / (float)seg;
				Vector3 p = new Vector3(MathF.Sin(th) * MathF.Cos(ph), MathF.Cos(th), MathF.Sin(th) * MathF.Sin(ph));
				b.Add(p, p);
			}
		}
		for (int i = 0; i < rings; i++)
		{
			for (int j = 0; j < seg; j++)
			{
				uint a = (uint)(i * (seg + 1) + j);
				uint c = a + (uint)(seg + 1);
				b.Tri(a, a + 1, c);
				b.Tri(a + 1, c + 1, c);
			}
		}
		b.End();
	}

	private static void Torus(MeshBuilder b, int seg, int side, float R, float r)
	{
		b.Begin();
		for (int i = 0; i <= seg; i++)
		{
			for (int j = 0; j <= side; j++)
			{
				float u = (float)Math.PI * 2f * (float)i / (float)seg;
				float v = (float)Math.PI * 2f * (float)j / (float)side;
				Vector3 c = new Vector3(MathF.Cos(u) * R, 0f, MathF.Sin(u) * R);
				Vector3 n = new Vector3(MathF.Cos(u) * MathF.Cos(v), MathF.Sin(v), MathF.Sin(u) * MathF.Cos(v));
				b.Add(c + n * r, n);
			}
		}
		for (int k = 0; k < seg; k++)
		{
			for (int l = 0; l < side; l++)
			{
				uint a = (uint)(k * (side + 1) + l);
				uint c2 = a + (uint)(side + 1);
				b.Tri(a, c2, a + 1);
				b.Tri(a + 1, c2, c2 + 1);
			}
		}
		b.End();
	}

	private static void Column(MeshBuilder b, int seg)
	{
		b.Begin();
		for (int i = 0; i <= seg; i++)
		{
			float a = (float)Math.PI * 2f * (float)i / (float)seg;
			float rr = 1f + 0.06f * MathF.Cos(a * 12f);
			Vector3 n = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a));
			b.Add(new Vector3(MathF.Cos(a) * rr, 0f, MathF.Sin(a) * rr), n);
			b.Add(new Vector3(MathF.Cos(a) * rr, 1f, MathF.Sin(a) * rr), n);
		}
		for (int j = 0; j < seg; j++)
		{
			uint a2 = (uint)(j * 2);
			b.Tri(a2, a2 + 1, a2 + 2);
			b.Tri(a2 + 1, a2 + 3, a2 + 2);
		}
		uint top = b.Add(new Vector3(0f, 1f, 0f), Vector3.UnitY);
		for (int k = 0; k < seg; k++)
		{
			float a3 = (float)Math.PI * 2f * (float)k / (float)seg;
			float a4 = (float)Math.PI * 2f * (float)(k + 1) / (float)seg;
			uint p0 = b.Add(new Vector3(MathF.Cos(a3), 1f, MathF.Sin(a3)), Vector3.UnitY);
			uint p1 = b.Add(new Vector3(MathF.Cos(a4), 1f, MathF.Sin(a4)), Vector3.UnitY);
			b.Tri(top, p1, p0);
		}
		b.End();
	}

	private static void Box(MeshBuilder b)
	{
		b.Begin();
		Vector3[] array = new Vector3[6]
		{
			Vector3.UnitX,
			-Vector3.UnitX,
			Vector3.UnitY,
			-Vector3.UnitY,
			Vector3.UnitZ,
			-Vector3.UnitZ
		};
		for (int i = 0; i < array.Length; i++)
		{
			Vector3 f = array[i];
			Vector3 u = ((MathF.Abs(f.Y) > 0.5f) ? Vector3.UnitX : Vector3.UnitY);
			Vector3 v = Vector3.Cross(f, u);
			uint a = b.Add(f - u - v, f);
			uint c = b.Add(f + u - v, f);
			uint d = b.Add(f + u + v, f);
			uint e = b.Add(f - u + v, f);
			b.Tri(a, c, d);
			b.Tri(a, d, e);
		}
		b.End();
	}

	private static List<Obj> BuildObjects()
	{
		Random rnd = new Random(20261015);
		List<Obj> list = new List<Obj>
		{
			new Obj(0, Vector3.Zero, 1f, Vector3.One, 0f, Vector3.UnitY, new InstMat
			{
				Albedo = new Vector3(0.55f, 0.55f, 0.58f),
				Metal = 0.15f,
				Rough = 0.35f,
				Mesh = 0u
			})
		};
		for (int i = 0; i < 7; i++)
		{
			float a = (float)i / 7f * ((float)Math.PI * 2f);
			Vector3 pos = new Vector3(MathF.Cos(a) * 16f, 6f, MathF.Sin(a) * 16f);
			bool gold = i % 2 == 1;
			list.Add(new Obj(2, pos, 6f, new Vector3(6f), 0f, Vector3.UnitY, new InstMat
			{
				Albedo = (gold ? new Vector3(1f, 0.78f, 0.34f) : new Vector3(0.95f, 0.95f, 0.97f)),
				Metal = 1f,
				Rough = (gold ? 0.12f : 0f),
				Mesh = 2u
			}));
		}
		while (list.Count < 4000)
		{
			Vector3 pos2 = new Vector3((R() - 0.5f) * 220f, 0f, (R() - 0.5f) * 220f);
			if (!(new Vector2(pos2.X, pos2.Z).Length() < 26f))
			{
				float num = R();
				int num2 = ((num < 0.35f) ? 1 : ((num < 0.55f) ? 3 : ((!(num < 0.75f)) ? 5 : 4)));
				int mesh = num2;
				float s = 0.6f + R() * 1.8f;
				Vector3 s3 = ((mesh == 4) ? new Vector3(s * 0.6f, s * (2f + R() * 3f), s * 0.6f) : new Vector3(s));
				pos2.Y = mesh switch
				{
					1 => s, 
					3 => s * 0.35f + 0.6f, 
					5 => s, 
					_ => 0f, 
				};
				float roll = R();
				InstMat mat = new InstMat
				{
					Albedo = Palette(R(), R()),
					Metal = ((roll < 0.3f) ? 1 : 0),
					Rough = ((roll < 0.3f) ? (R() * 0.35f) : (0.6f + R() * 0.4f)),
					Emissive = ((roll > 0.97f) ? (6f + R() * 10f) : 0f),
					Mesh = (uint)mesh
				};
				bool flag = ((mesh == 3 || mesh == 5) ? true : false);
				float spin = ((flag && R() < 0.5f) ? (0.3f + R() * 1.2f) : 0f);
				Vector3 axis = Vector3.Normalize(new Vector3(R() - 0.5f, 1f, R() - 0.5f));
				list.Add(new Obj(mesh, pos2, s, s3, spin, axis, mat));
			}
		}
		return list;
		float R()
		{
			return (float)rnd.NextDouble();
		}
	}

	private static Vector3 Palette(float pick, float shade)
	{
		Vector3[] p = new Vector3[8]
		{
			new Vector3(0.85f, 0.18f, 0.12f),
			new Vector3(0.95f, 0.55f, 0.08f),
			new Vector3(0.12f, 0.45f, 0.85f),
			new Vector3(0.1f, 0.65f, 0.4f),
			new Vector3(0.9f, 0.85f, 0.75f),
			new Vector3(0.45f, 0.2f, 0.7f),
			new Vector3(0.08f, 0.08f, 0.1f),
			new Vector3(0.75f, 0.75f, 0.78f)
		};
		return p[(int)(pick * (float)p.Length) % p.Length] * (0.55f + shade * 0.45f);
	}

	private static Matrix4x4 ObjMatrix(Obj o, float t)
	{
		return Matrix4x4.CreateScale(o.Scale3) * ((o.Spin != 0f) ? Matrix4x4.CreateFromAxisAngle(o.Axis, t * o.Spin) : Matrix4x4.Identity) * Matrix4x4.CreateTranslation(o.Pos);
	}

	private static (Vector3 Eye, Vector3 Target) CameraAt(float t)
	{
		float a = t * ((float)Math.PI * 2f) * 0.75f + 0.4f;
		float r = 40f + 26f * MathF.Sin(t * (float)Math.PI);
		Vector3 item = new Vector3(MathF.Cos(a) * r, 12f + 10f * (0.5f + 0.5f * MathF.Sin(t * ((float)Math.PI * 2f) * 1.5f)), MathF.Sin(a) * r);
		Vector3 target = new Vector3(MathF.Cos(a + 0.6f) * 6f, 3f, MathF.Sin(a + 0.6f) * 6f);
		return (Eye: item, Target: target);
	}

	public unsafe GpuRtResult? Run()
	{
		ThreadPriority oldPriority = Thread.CurrentThread.Priority;
		Thread.CurrentThread.Priority = ThreadPriority.Highest;
		nint hwnd = IntPtr.Zero;
		string cls = "TriarchRtBench" + Environment.TickCount64;
		nint hInst = GetModuleHandleW(null);
		List<IDisposable> keep = new List<IDisposable>();
		ID3D12CommandQueue queue = null;
		ID3D12Fence fence = null;
		ulong fenceValue = 0uL;
		AutoResetEvent fenceEvent = new AutoResetEvent(initialState: false);
		try
		{
			_wndProc = WindowProc;
			WNDCLASSEX wc = new WNDCLASSEX
			{
				cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
				lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
				hInstance = hInst,
				hCursor = LoadCursorW(IntPtr.Zero, 32512),
				lpszClassName = cls
			};
			RegisterClassExW(ref wc);
			int sw = GetSystemMetrics(0);
			int sh = GetSystemMetrics(1);
			double scale = Math.Min(1.0, Math.Min((double)sw * 0.85 / (double)Width, (double)sh * 0.85 / (double)Height));
			RECT rect = new RECT
			{
				Right = (int)((double)Width * scale),
				Bottom = (int)((double)Height * scale)
			};
			AdjustWindowRect(ref rect, 282001408u, menu: false);
			int ww = rect.Right - rect.Left;
			int wh = rect.Bottom - rect.Top;
			hwnd = CreateWindowExW(0u, cls, "TRIARCH BENCH · Ray tracing", 282001408u, (sw - ww) / 2, (sh - wh) / 2, ww, wh, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
			IDXGIFactory4 iDXGIFactory = K<IDXGIFactory4>(DXGI.CreateDXGIFactory1<IDXGIFactory4>());
			IDXGIAdapter1 adapter = K<IDXGIAdapter1>(PickAdapter(iDXGIFactory, AdapterName, out string adapterName));
			ID3D12Device5 device = K<ID3D12Device5>(D3D12.D3D12CreateDevice<ID3D12Device5>(adapter, FeatureLevel.Level_12_0));
			if (device.Options5.RaytracingTier < RaytracingTier.Tier1_1)
			{
				throw new NotSupportedException("Esta placa não tem ray tracing por hardware (DXR 1.1).");
			}
			queue = K<ID3D12CommandQueue>(device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct)));
			fence = K<ID3D12Fence>(device.CreateFence(0uL));
			queue.GetTimestampFrequency(out var tsFreq);
			IDXGISwapChain3 swap = K<IDXGISwapChain3>(iDXGIFactory.CreateSwapChainForHwnd(queue, hwnd, new SwapChainDescription1((uint)Width, (uint)Height, Format.R8G8B8A8_UNorm)).QueryInterface<IDXGISwapChain3>());
			iDXGIFactory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);
			ID3D12Resource[] backBuffers = new ID3D12Resource[2]
			{
				K<ID3D12Resource>(swap.GetBuffer<ID3D12Resource>(0u)),
				K<ID3D12Resource>(swap.GetBuffer<ID3D12Resource>(1u))
			};
			ID3D12GraphicsCommandList4 cl;
			using (IDxcResult compiled = DxcCompiler.Compile(DxcShaderStage.Compute, "#define RS \"CBV(b0), SRV(t0), SRV(t1), SRV(t2), SRV(t3), SRV(t4), DescriptorTable(UAV(u0))\"\r\nstruct Frame { row_major float4x4 InvViewProj; float3 CamPos; float Time; float3 SunDir; uint FrameIndex; uint Width; uint Height; uint Spp; uint Bounces; };\r\nConstantBuffer<Frame> F : register(b0);\r\nRaytracingAccelerationStructure Scene : register(t0);\r\nstruct Vtx { float3 pos; float3 nrm; };\r\nStructuredBuffer<Vtx> Verts : register(t1);\r\nStructuredBuffer<uint> Indices : register(t2);\r\nstruct Mesh { uint indexOffset; uint vertexOffset; uint p0; uint p1; };\r\nStructuredBuffer<Mesh> Meshes : register(t3);\r\nstruct Inst { float3 albedo; float metal; float rough; float emissive; uint mesh; float pad; };\r\nStructuredBuffer<Inst> Insts : register(t4);\r\nRWTexture2D<float4> Output : register(u0);\r\n\r\nuint Pcg(inout uint s) { s = s * 747796405u + 2891336453u; uint w = ((s >> ((s >> 28u) + 4u)) ^ s) * 277803737u; return (w >> 22u) ^ w; }\r\nfloat Rnd(inout uint s) { return (Pcg(s) & 0xFFFFFF) / 16777216.0; }\r\n\r\nfloat3 CosineDir(float3 n, inout uint s)\r\n{\r\n    float u = Rnd(s), v = Rnd(s), r = sqrt(u), phi = 6.2831853 * v;\r\n    float3 t = abs(n.y) < 0.99 ? normalize(cross(n, float3(0, 1, 0))) : float3(1, 0, 0);\r\n    float3 b = cross(n, t);\r\n    return normalize(t * (r * cos(phi)) + b * (r * sin(phi)) + n * sqrt(max(0, 1 - u)));\r\n}\r\n\r\nfloat3 Sky(float3 d)\r\n{\r\n    float3 sky = lerp(float3(0.85, 0.9, 1.0), float3(0.25, 0.45, 0.85), saturate(d.y));\r\n    float sun = pow(saturate(dot(d, -F.SunDir)), 900) * 60;\r\n    return sky * 0.8 + sun * float3(1.0, 0.92, 0.8);\r\n}\r\n\r\nstruct Hit { float3 pos; float3 nrm; Inst mat; };\r\n\r\nbool Closest(float3 o, float3 d, out Hit h)\r\n{\r\n    RayQuery<RAY_FLAG_FORCE_OPAQUE> q;\r\n    RayDesc r; r.Origin = o; r.TMin = 0.002; r.Direction = d; r.TMax = 2000;\r\n    q.TraceRayInline(Scene, RAY_FLAG_NONE, 0xFF, r);\r\n    q.Proceed();\r\n    h = (Hit)0;\r\n    if (q.CommittedStatus() != COMMITTED_TRIANGLE_HIT) return false;\r\n    Inst m = Insts[q.CommittedInstanceID()];\r\n    Mesh me = Meshes[m.mesh];\r\n    uint i0 = me.indexOffset + q.CommittedPrimitiveIndex() * 3;\r\n    float2 bc = q.CommittedTriangleBarycentrics();\r\n    float3 n0 = Verts[me.vertexOffset + Indices[i0]].nrm, n1 = Verts[me.vertexOffset + Indices[i0 + 1]].nrm, n2 = Verts[me.vertexOffset + Indices[i0 + 2]].nrm;\r\n    float3 n = n0 * (1 - bc.x - bc.y) + n1 * bc.x + n2 * bc.y;\r\n    float3x4 o2w = q.CommittedObjectToWorld3x4();\r\n    n = normalize(mul((float3x3)o2w, n));\r\n    if (dot(n, d) > 0) n = -n;\r\n    h.pos = o + d * q.CommittedRayT() + n * 0.003;\r\n    h.nrm = n; h.mat = m;\r\n    return true;\r\n}\r\n\r\nbool Occluded(float3 o, float3 d, float tmax)\r\n{\r\n    RayQuery<RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_CLOSEST_HIT_SHADER> q;\r\n    RayDesc r; r.Origin = o; r.TMin = 0.002; r.Direction = d; r.TMax = tmax;\r\n    q.TraceRayInline(Scene, RAY_FLAG_NONE, 0xFF, r);\r\n    q.Proceed();\r\n    return q.CommittedStatus() != COMMITTED_NOTHING;\r\n}\r\n\r\n[RootSignature(RS)]\r\n[numthreads(8, 8, 1)]\r\nvoid Main(uint3 id : SV_DispatchThreadID)\r\n{\r\n    if (id.x >= F.Width || id.y >= F.Height) return;\r\n    uint seed = (id.y * F.Width + id.x) * 9781u + F.FrameIndex * 6271u;\r\n    float3 color = 0;\r\n    for (uint s = 0; s < F.Spp; s++)\r\n    {\r\n        float2 uv = (float2(id.xy) + float2(Rnd(seed), Rnd(seed))) / float2(F.Width, F.Height);\r\n        float4 p = mul(float4(uv.x * 2 - 1, 1 - uv.y * 2, 1, 1), F.InvViewProj);\r\n        float3 o = F.CamPos, d = normalize(p.xyz / p.w - F.CamPos);\r\n        float3 through = 1, acc = 0;\r\n        for (uint b = 0; b < F.Bounces; b++)\r\n        {\r\n            Hit h;\r\n            if (!Closest(o, d, h)) { acc += through * Sky(d); break; }\r\n            acc += through * h.mat.albedo * h.mat.emissive;\r\n            // Sombra suave do sol (direção sorteada num cone) + oclusão de ambiente (um raio no hemisfério).\r\n            float3 sd = normalize(-F.SunDir + (float3(Rnd(seed), Rnd(seed), Rnd(seed)) - 0.5) * 0.06);\r\n            float ndl = saturate(dot(h.nrm, sd));\r\n            float sunVis = ndl > 0 && !Occluded(h.pos, sd, 1000) ? 1 : 0;\r\n            float3 ad = CosineDir(h.nrm, seed);\r\n            float ao = Occluded(h.pos, ad, 6) ? 0.15 : 1;\r\n            float3 diffuse = h.mat.albedo * (1 - h.mat.metal);\r\n            acc += through * diffuse * (ndl * sunVis * float3(3.6, 3.3, 2.9) + ao * float3(0.12, 0.15, 0.2));\r\n            // Próxima rebatida: reflexo (metal) ou iluminação indireta (difuso).\r\n            if (h.mat.metal > 0.5)\r\n            {\r\n                float3 rd = reflect(d, h.nrm);\r\n                d = normalize(rd + (CosineDir(h.nrm, seed) - h.nrm) * h.mat.rough);\r\n                through *= h.mat.albedo;\r\n            }\r\n            else\r\n            {\r\n                d = CosineDir(h.nrm, seed);\r\n                through *= h.mat.albedo * 0.8;\r\n            }\r\n            o = h.pos;\r\n            if (max(through.x, max(through.y, through.z)) < 0.02) break;\r\n        }\r\n        color += acc;\r\n    }\r\n    color /= F.Spp;\r\n    color = color / (1 + color);               // tone mapping (Reinhard)\r\n    color = pow(saturate(color), 1 / 2.2);     // gama\r\n    Output[id.xy] = float4(color, 1);\r\n}", "Main", new DxcCompilerOptions
			{
				ShaderModel = new DxcShaderModel(6, 5),
				OptimizationLevel = 3
			}))
			{
				byte[] bytecode = compiled.GetObjectBytecodeMemory().ToArray();
				if (bytecode.Length == 0)
				{
					throw new InvalidOperationException("Falha ao compilar o shader: " + compiled.GetErrors());
				}
				ID3D12RootSignature rootSig = K<ID3D12RootSignature>(device.CreateRootSignature(bytecode));
				ID3D12PipelineState pso = K<ID3D12PipelineState>(device.CreateComputePipelineState<ID3D12PipelineState>(new ComputePipelineStateDescription
				{
					RootSignature = rootSig,
					ComputeShader = bytecode
				}));
				ID3D12CommandAllocator[] allocs = new ID3D12CommandAllocator[4];
				for (int i = 0; i < 4; i++)
				{
					allocs[i] = K<ID3D12CommandAllocator>(device.CreateCommandAllocator(CommandListType.Direct));
				}
				cl = K<ID3D12GraphicsCommandList4>(device.CreateCommandList<ID3D12GraphicsCommandList4>(0u, CommandListType.Direct, allocs[0]));
				MeshBuilder mb = new MeshBuilder();
				Plane(mb, 280f, 96);
				Sphere(mb, 32);
				Sphere(mb, 160);
				Torus(mb, 48, 24, 1f, 0.35f);
				Column(mb, 48);
				Box(mb);
				ID3D12Resource vb = ToGpu<Vtx>(mb.V.ToArray());
				ID3D12Resource ib = ToGpu<uint>(mb.I.ToArray());
				ID3D12Resource meshInfo = ToGpu<MeshInfo>(mb.Meshes.Select(((uint IndexOffset, uint IndexCount, uint VertexOffset, uint VertexCount) tuple4) =>
				{
					MeshInfo result = default;
					(result.IndexOffset, _, result.VertexOffset, _) = tuple4;
					return result;
				}).ToArray());
				List<Obj> objects = BuildObjects();
				ID3D12Resource mats = ToGpu<InstMat>(objects.Select((Obj obj3) => obj3.Mat).ToArray());
				ID3D12Resource[] blas = new ID3D12Resource[mb.Meshes.Count];
				for (int m = 0; m < mb.Meshes.Count; m++)
				{
					(uint IndexOffset, uint IndexCount, uint VertexOffset, uint VertexCount) tuple = mb.Meshes[m];
					uint io = tuple.IndexOffset;
					uint ic = tuple.IndexCount;
					uint vo = tuple.VertexOffset;
					uint vc = tuple.VertexCount;
					RaytracingGeometryDescription geo = new RaytracingGeometryDescription(new RaytracingGeometryTrianglesDescription(new GpuVirtualAddressAndStride(vb.GPUVirtualAddress + (ulong)(vo * Marshal.SizeOf<Vtx>()), (ulong)Marshal.SizeOf<Vtx>()), Format.R32G32B32_Float, vc, 0uL, ib.GPUVirtualAddress + (ulong)((long)io * 4L), Format.R32_UInt, ic), RaytracingGeometryFlags.Opaque);
					BuildRaytracingAccelerationStructureInputs buildRaytracingAccelerationStructureInputs = new BuildRaytracingAccelerationStructureInputs();
					buildRaytracingAccelerationStructureInputs.Type = RaytracingAccelerationStructureType.BottomLevel;
					buildRaytracingAccelerationStructureInputs.Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace;
					buildRaytracingAccelerationStructureInputs.Layout = ElementsLayout.Array;
					buildRaytracingAccelerationStructureInputs.DescriptorsCount = 1u;
					buildRaytracingAccelerationStructureInputs.GeometryDescriptions = new RaytracingGeometryDescription[1] { geo };
					BuildRaytracingAccelerationStructureInputs inputs = buildRaytracingAccelerationStructureInputs;
					RaytracingAccelerationStructurePrebuildInfo info = device.GetRaytracingAccelerationStructurePrebuildInfo(inputs);
					ID3D12Resource scratch = Buffer(info.ScratchDataSizeInBytes, HeapType.Default, ResourceStates.Common, ResourceFlags.AllowUnorderedAccess);
					blas[m] = Buffer(info.ResultDataMaxSizeInBytes, HeapType.Default, ResourceStates.RaytracingAccelerationStructure, ResourceFlags.AllowUnorderedAccess);
					cl.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription(blas[m].GPUVirtualAddress, inputs, 0uL, scratch.GPUVirtualAddress));
					cl.ResourceBarrierUnorderedAccessView(blas[m]);
				}
				BuildRaytracingAccelerationStructureInputs tlasInputs = new BuildRaytracingAccelerationStructureInputs
				{
					Type = RaytracingAccelerationStructureType.TopLevel,
					Flags = RaytracingAccelerationStructureBuildFlags.PreferFastBuild,
					Layout = ElementsLayout.Array,
					DescriptorsCount = (uint)objects.Count
				};
				RaytracingAccelerationStructurePrebuildInfo raytracingAccelerationStructurePrebuildInfo = device.GetRaytracingAccelerationStructurePrebuildInfo(tlasInputs);
				ID3D12Resource tlas = Buffer(raytracingAccelerationStructurePrebuildInfo.ResultDataMaxSizeInBytes, HeapType.Default, ResourceStates.RaytracingAccelerationStructure, ResourceFlags.AllowUnorderedAccess);
				ID3D12Resource tlasScratch = Buffer(raytracingAccelerationStructurePrebuildInfo.ScratchDataSizeInBytes, HeapType.Default, ResourceStates.Common, ResourceFlags.AllowUnorderedAccess);
				int instSize = Marshal.SizeOf<InstanceDesc>();
				ID3D12Resource[] instUpload = new ID3D12Resource[4];
				nint[] instPtr = new nint[4];
				void* p = default;
				for (int i2 = 0; i2 < 4; i2++)
				{
					instUpload[i2] = Buffer((ulong)(objects.Count * instSize), HeapType.Upload, ResourceStates.GenericRead);
					instUpload[i2].Map(0u, null, &p);
					instPtr[i2] = (nint)p;
				}
				ID3D12Resource[] cbUpload = new ID3D12Resource[4];
				nint[] cbPtr = new nint[4];
				void* p2 = default;
				for (int i3 = 0; i3 < 4; i3++)
				{
					cbUpload[i3] = Buffer(256uL, HeapType.Upload, ResourceStates.GenericRead);
					cbUpload[i3].Map(0u, null, &p2);
					cbPtr[i3] = (nint)p2;
				}
				ID3D12Resource output = K<ID3D12Resource>(device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, (uint)Width, (uint)Height, 1, 1, 1u, 0u, ResourceFlags.AllowUnorderedAccess, TextureLayout.Unknown, 0uL), ResourceStates.UnorderedAccess));
				ID3D12DescriptorHeap heap = K<ID3D12DescriptorHeap>(device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 1u, DescriptorHeapFlags.ShaderVisible)));
				device.CreateUnorderedAccessView(output, null, null, heap.GetCPUDescriptorHandleForHeapStart());
				ID3D12QueryHeap tsHeap = K<ID3D12QueryHeap>(device.CreateQueryHeap<ID3D12QueryHeap>(new QueryHeapDescription(QueryHeapType.Timestamp, 4u)));
				ID3D12Resource tsReadback = Buffer(32uL, HeapType.Readback, ResourceStates.CopyDest);
				cl.Close();
				queue.ExecuteCommandList(cl);
				queue.Signal(fence, ++fenceValue);
				WaitGpu(fenceValue);
				(uint Spp, uint Bounces) quality = Quality;
				uint spp = quality.Spp;
				uint bounces = quality.Bounces;
				Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView((float)Math.PI / 3f, (float)Width / (float)Height, 0.1f, 2000f);
				Vector3 sunDir = Vector3.Normalize(new Vector3(-0.4f, -0.75f, -0.35f));
				ulong[] frameFence = new ulong[4];
				List<RtPass> passes = new List<RtPass>();
				InstanceDesc[] inst = new InstanceDesc[objects.Count];
				uint frameIndex = 0u;
				int totalPhases = Passes + ((Warmup > TimeSpan.Zero) ? 1 : 0);
				void* p3 = default;
				for (int phase = 0; phase < totalPhases; phase++)
				{
					if (_stop)
					{
						break;
					}
					bool warm = Warmup > TimeSpan.Zero && phase == 0;
					TimeSpan duration = (warm ? Warmup : PassDuration);
					Phase = (warm ? "Aquecendo" : $"Passada {((!warm) ? (phase - ((Warmup > TimeSpan.Zero) ? 1 : 0)) : 0) + 1} de {Passes}");
					List<double> frameTimes = new List<double>(20000);
					Stopwatch clock = Stopwatch.StartNew();
					double lastPresent = 0.0;
					double lastTitle = 0.0;
					int titleFrames = 0;
					ulong lastTs = 0uL;
					long frame = 0L;
					while (!_stop && clock.Elapsed < duration)
					{
						MSG msg;
						while (PeekMessageW(out msg, IntPtr.Zero, 0u, 0u, 1u))
						{
							TranslateMessage(ref msg);
							DispatchMessageW(ref msg);
						}
						if (_stop)
						{
							break;
						}
						int slot = (int)(frame % 4);
						if (frame >= 4)
						{
							WaitGpu(frameFence[slot]);
							Vortice.Direct3D12.Range range = new Vortice.Direct3D12.Range((nuint)(slot * 8), (nuint)(slot * 8 + 8));
							tsReadback.Map(0u, range, &p3);
							ulong ts = *(ulong*)((byte*)p3 + slot * 8);
							tsReadback.Unmap(0u, new Vortice.Direct3D12.Range(0u, 0u));
							if (lastTs != 0L && ts > lastTs)
							{
								frameTimes.Add((double)(ts - lastTs) * 1000.0 / (double)tsFreq);
							}
							lastTs = ts;
							titleFrames++;
						}
						float t = (float)(clock.Elapsed.TotalSeconds / duration.TotalSeconds);
						float secs = (float)clock.Elapsed.TotalSeconds;
						var (eye, target) = CameraAt(t);
						Matrix4x4.Invert(Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY) * proj, out var inv);
						Marshal.StructureToPtr(new FrameCB
						{
							InvViewProj = inv,
							CamPos = eye,
							Time = secs,
							SunDir = sunDir,
							FrameIndex = frameIndex++,
							Width = (uint)Width,
							Height = (uint)Height,
							Spp = spp,
							Bounces = bounces
						}, cbPtr[slot], fDeleteOld: false);
						for (int i4 = 0; i4 < objects.Count; i4++)
						{
							Obj o = objects[i4];
							Matrix4x4 m2 = ObjMatrix(o, secs);
							inst[i4] = new InstanceDesc
							{
								M00 = m2.M11,
								M01 = m2.M21,
								M02 = m2.M31,
								M03 = m2.M41,
								M10 = m2.M12,
								M11 = m2.M22,
								M12 = m2.M32,
								M13 = m2.M42,
								M20 = m2.M13,
								M21 = m2.M23,
								M22 = m2.M33,
								M23 = m2.M43,
								IdMask = (uint)(i4 | -16777216),
								ContribFlags = 83886080u,
								Blas = blas[o.Mesh].GPUVirtualAddress
							};
						}
						fixed (InstanceDesc* src = inst)
						{
							System.Buffer.MemoryCopy(src, (void*)instPtr[slot], (long)inst.Length * (long)instSize, (long)inst.Length * (long)instSize);
						}
						allocs[slot].Reset();
						cl.Reset(allocs[slot], pso);
						tlasInputs.InstanceDescriptions = instUpload[slot].GPUVirtualAddress;
						cl.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription(tlas.GPUVirtualAddress, tlasInputs, 0uL, tlasScratch.GPUVirtualAddress));
						cl.ResourceBarrierUnorderedAccessView(tlas);
						cl.SetComputeRootSignature(rootSig);
						cl.SetDescriptorHeaps(heap);
						cl.SetComputeRootConstantBufferView(0u, cbUpload[slot].GPUVirtualAddress);
						cl.SetComputeRootShaderResourceView(1u, tlas.GPUVirtualAddress);
						cl.SetComputeRootShaderResourceView(2u, vb.GPUVirtualAddress);
						cl.SetComputeRootShaderResourceView(3u, ib.GPUVirtualAddress);
						cl.SetComputeRootShaderResourceView(4u, meshInfo.GPUVirtualAddress);
						cl.SetComputeRootShaderResourceView(5u, mats.GPUVirtualAddress);
						cl.SetComputeRootDescriptorTable(6u, heap.GetGPUDescriptorHandleForHeapStart());
						cl.Dispatch((uint)((Width + 7) / 8), (uint)((Height + 7) / 8), 1u);
						cl.ResourceBarrierUnorderedAccessView(output);
						double now = clock.Elapsed.TotalSeconds;
						bool flag = now - lastPresent >= 1.0 / 30.0;
						ID3D12Resource bb = null;
						if (flag)
						{
							bb = backBuffers[swap.CurrentBackBufferIndex];
							cl.ResourceBarrierTransition(output, ResourceStates.UnorderedAccess, ResourceStates.CopySource);
							cl.ResourceBarrierTransition(bb, ResourceStates.Common, ResourceStates.CopyDest);
							cl.CopyResource(bb, output);
							cl.ResourceBarrierTransition(bb, ResourceStates.CopyDest, ResourceStates.Common);
							cl.ResourceBarrierTransition(output, ResourceStates.CopySource, ResourceStates.UnorderedAccess);
						}
						cl.EndQuery(tsHeap, QueryType.Timestamp, (uint)slot);
						cl.ResolveQueryData(tsHeap, QueryType.Timestamp, (uint)slot, 1u, tsReadback, (ulong)(slot * 8));
						cl.Close();
						queue.ExecuteCommandList(cl);
						if (flag)
						{
							swap.Present(0u, PresentFlags.None);
							lastPresent = now;
						}
						queue.Signal(fence, ++fenceValue);
						frameFence[slot] = fenceValue;
						frame++;
						if (now - lastTitle >= 0.5)
						{
							CurrentFps = (double)titleFrames / (now - lastTitle);
							titleFrames = 0;
							lastTitle = now;
							double done = (double)phase + now / duration.TotalSeconds;
							Progress = done / (double)totalPhases;
							SetWindowTextW(hwnd, $"TRIARCH BENCH · Ray tracing · {Width}x{Height} · {Preset} · {Phase} · {CurrentFps:0} FPS");
						}
					}
					WaitGpu(fenceValue);
					if (_stop)
					{
						break;
					}
					if (!warm)
					{
						double[] sorted = frameTimes.OrderBy((double x) => x).ToArray();
						double p99 = ((sorted.Length != 0) ? sorted[Math.Min(sorted.Length - 1, (int)((double)sorted.Length * 0.99))] : 0.0);
						double sum = frameTimes.Sum() / 1000.0;
						passes.Add(new RtPass((sum > 0.0) ? ((double)frameTimes.Count / sum) : 0.0, (p99 > 0.0) ? (1000.0 / p99) : 0.0, frameTimes.Count));
					}
				}
				if (_stop)
				{
					Cancelled = true;
					return null;
				}
				GpuRtResult gpuRtResult = new GpuRtResult(passes, Width, Height, Preset, adapterName, 0.0);
				double grays = gpuRtResult.MedianFps * (double)Width * (double)Height * RaysPerPixel / 1000000000.0;
				return gpuRtResult with
				{
					GigaRaysPerSec = grays
				};
			}
			ID3D12Resource Buffer(ulong size, HeapType heapType, ResourceStates state, ResourceFlags flags = ResourceFlags.None)
			{
				return K<ID3D12Resource>(device.CreateCommittedResource(heapType, ResourceDescription.Buffer(Math.Max(size, 256uL), flags, 0uL), state));
			}
			ID3D12Resource ToGpu<T>(T[] data) where T : unmanaged
			{
				ID3D12Resource up = Upload<T>(data);
				ulong size = (ulong)(data.Length * Marshal.SizeOf<T>());
				ID3D12Resource gpu = Buffer(size, HeapType.Default, ResourceStates.Common);
				cl.CopyBufferRegion(gpu, 0uL, up, 0uL, size);
				cl.ResourceBarrierTransition(gpu, ResourceStates.CopyDest, ResourceStates.NonPixelShaderResource);
				return gpu;
			}
			unsafe ID3D12Resource Upload<T>(T[] data) where T : unmanaged
			{
				ulong size = (ulong)(data.Length * Marshal.SizeOf<T>());
				ID3D12Resource up = Buffer(size, HeapType.Upload, ResourceStates.GenericRead);
				void* p100 = default;
				up.Map(0u, null, &p100);
				fixed (T* src2 = data)
				{
					System.Buffer.MemoryCopy(src2, p100, (long)size, (long)size);
				}
				up.Unmap(0u);
				return up;
			}
		}
		catch (Exception ex)
		{
			Error = ex.Message;
			return null;
		}
		finally
		{
			try
			{
				if (queue != null && fence != null)
				{
					queue.Signal(fence, ++fenceValue);
					WaitGpu(fenceValue);
				}
			}
			catch
			{
			}
			for (int i5 = keep.Count - 1; i5 >= 0; i5--)
			{
				try
				{
					keep[i5].Dispose();
				}
				catch
				{
				}
			}
			fenceEvent.Dispose();
			if (hwnd != IntPtr.Zero)
			{
				DestroyWindow(hwnd);
			}
			UnregisterClassW(cls, hInst);
			Thread.CurrentThread.Priority = oldPriority;
		}
		T K<T>(T d) where T : notnull, IDisposable
		{
			keep.Add(d);
			return d;
		}
		void WaitGpu(ulong v)
		{
			if (fence.CompletedValue < v)
			{
				fence.SetEventOnCompletion(v, fenceEvent.SafeWaitHandle.DangerousGetHandle());
				fenceEvent.WaitOne();
			}
		}
	}

	private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
	{
		if (msg == 274 && ((ulong)wParam & 0xFFF0uL) == 61696)
		{
			return IntPtr.Zero;
		}
		if (msg == 16 || (msg == 256 && wParam == 27))
		{
			_stop = true;
			return IntPtr.Zero;
		}
		return DefWindowProcW(hWnd, msg, wParam, lParam);
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern ushort RegisterClassExW(ref WNDCLASSEX wc);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool UnregisterClassW(string cls, nint hInst);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern nint CreateWindowExW(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint hInst, nint param);

	[DllImport("user32.dll")]
	private static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

	[DllImport("user32.dll")]
	private static extern bool PeekMessageW(out MSG msg, nint hWnd, uint min, uint max, uint remove);

	[DllImport("user32.dll")]
	private static extern bool TranslateMessage(ref MSG msg);

	[DllImport("user32.dll")]
	private static extern nint DispatchMessageW(ref MSG msg);

	[DllImport("user32.dll")]
	private static extern bool DestroyWindow(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool AdjustWindowRect(ref RECT r, uint style, bool menu);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool SetWindowTextW(nint hWnd, string text);

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int index);

	[DllImport("user32.dll")]
	private static extern nint LoadCursorW(nint hInst, nint name);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern nint GetModuleHandleW(string? name);
}
