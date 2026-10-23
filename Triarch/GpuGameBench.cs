using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Triarch;

public sealed class GpuGameBench
{
	private struct Vertex
	{
		public Vector3 Pos;

		public Vector3 Normal;

		public float Mat;
	}

	private struct FrameCB
	{
		public Matrix4x4 ViewProj;

		public Matrix4x4 LightViewProj;

		public Matrix4x4 InvViewProj;

		public Vector3 CamPos;

		public float Time;

		public Vector3 SunDir;

		public float Pad;
	}

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

	private struct RECT
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
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

	private volatile bool _stop;

	private WndProc? _wndProc;

	private const int TerrainN = 512;

	private const float TerrainSize = 800f;

	private const float WaterY = 6f;

	private const int TreeCount = 40000;

	private static readonly Vector2[] Path = BuildPath();

	private const string Hlsl = "\r\ncbuffer Frame : register(b0)\r\n{\r\n    row_major float4x4 ViewProj;\r\n    row_major float4x4 LightViewProj;\r\n    row_major float4x4 InvViewProj;\r\n    float3 CamPos; float Time;\r\n    float3 SunDir; float Pad;\r\n};\r\nStructuredBuffer<float4> Trees : register(t1);\r\nTexture2D<float> ShadowMap : register(t0);\r\nSamplerComparisonState ShadowSampler : register(s0);\r\nTexture2D<float4> Hdr : register(t2);\r\nSamplerState LinearSampler : register(s1);\r\n\r\nstruct VSIn { float3 pos : POSITION; float3 nrm : NORMAL; float mat : TEXCOORD0; uint iid : SV_InstanceID; };\r\nstruct PSIn { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 nrm : NORMAL; float mat : TEXCOORD1; };\r\n\r\nfloat3 TreeWorld(float3 p, uint iid, out float3x3 rot)\r\n{\r\n    float4 t = Trees[iid];\r\n    float a = frac(sin(iid * 12.9898) * 43758.5453) * 6.2831;\r\n    float c = cos(a), s = sin(a);\r\n    rot = float3x3(c, 0, -s, 0, 1, 0, s, 0, c);\r\n    float sway = sin(Time * 1.3 + iid) * 0.03 * p.y;   // vento\r\n    return mul(p, rot) * t.w + t.xyz + float3(sway, 0, sway * 0.5);\r\n}\r\n\r\nPSIn TerrainVS(VSIn i)\r\n{\r\n    PSIn o; o.wpos = i.pos; o.nrm = i.nrm; o.mat = i.mat;\r\n    o.pos = mul(float4(i.pos, 1), ViewProj);\r\n    return o;\r\n}\r\n\r\nPSIn TreeVS(VSIn i)\r\n{\r\n    PSIn o; float3x3 rot;\r\n    o.wpos = TreeWorld(i.pos, i.iid, rot);\r\n    o.nrm = mul(i.nrm, rot); o.mat = i.mat;\r\n    o.pos = mul(float4(o.wpos, 1), ViewProj);\r\n    return o;\r\n}\r\n\r\nfloat4 TerrainShadowVS(VSIn i) : SV_POSITION { return mul(float4(i.pos, 1), LightViewProj); }\r\nfloat4 TreeShadowVS(VSIn i) : SV_POSITION { float3x3 r; return mul(float4(TreeWorld(i.pos, i.iid, r), 1), LightViewProj); }\r\n\r\nfloat3 Sky(float3 dir)\r\n{\r\n    float up = saturate(dir.y);\r\n    float3 col = lerp(float3(0.42, 0.42, 0.42), float3(0.12, 0.12, 0.12), pow(up, 0.6));\r\n    float sun = saturate(dot(dir, -SunDir));\r\n    col += pow(sun, 600) * 12 + pow(sun, 12) * 0.25;\r\n    return col;\r\n}\r\n\r\nfloat Shadow(float3 wpos, float3 n)\r\n{\r\n    float4 lp = mul(float4(wpos + n * 0.15, 1), LightViewProj);\r\n    float2 uv = lp.xy * float2(0.5, -0.5) + 0.5;\r\n    if (any(uv < 0) || any(uv > 1)) return 1;\r\n    float sum = 0;\r\n    [unroll] for (int y = -2; y <= 2; y++)\r\n        [unroll] for (int x = -2; x <= 2; x++)\r\n            sum += ShadowMap.SampleCmpLevelZero(ShadowSampler, uv + float2(x, y) / 4096.0, lp.z - 0.0015);\r\n    return sum / 25;\r\n}\r\n\r\nfloat3 Shade(float3 wpos, float3 n, float albedo)\r\n{\r\n    float3 l = -SunDir;\r\n    float ndl = saturate(dot(n, l));\r\n    float3 direct = ndl * Shadow(wpos, n) * 1.5;\r\n    float3 ambient = lerp(0.06, 0.16, n.y * 0.5 + 0.5);\r\n    float3 col = albedo * (direct + ambient);\r\n    float dist = length(wpos - CamPos);\r\n    float fog = 1 - exp(-dist * 0.0022);\r\n    return lerp(col, Sky(normalize(wpos - CamPos)), fog);\r\n}\r\n\r\nfloat4 TerrainPS(PSIn i) : SV_TARGET\r\n{\r\n    float3 n = normalize(i.nrm);\r\n    float slope = 1 - n.y;\r\n    float h = i.wpos.y;\r\n    float albedo = lerp(0.22, 0.15, saturate((h - 8) / 25));               // grama\r\n    albedo = lerp(albedo, 0.28, saturate((slope - 0.25) * 4));             // rocha nas encostas\r\n    albedo = lerp(albedo, 0.70, saturate((h - 62) / 10) * saturate(1 - slope * 3)); // neve só nos picos\r\n    albedo *= 0.9 + 0.1 * frac(sin(dot(floor(i.wpos.xz * 2), float2(12.9898, 78.233))) * 43758.5453);\r\n    return float4(Shade(i.wpos, n, albedo), 1);\r\n}\r\n\r\nfloat4 TreePS(PSIn i) : SV_TARGET\r\n{\r\n    float albedo = i.mat < 1.5 ? 0.08 : 0.11;\r\n    return float4(Shade(i.wpos, normalize(i.nrm), albedo), 1);\r\n}\r\n\r\nPSIn WaterVS(VSIn i)\r\n{\r\n    PSIn o; o.wpos = i.pos; o.nrm = float3(0, 1, 0); o.mat = 0;\r\n    o.pos = mul(float4(i.pos, 1), ViewProj);\r\n    return o;\r\n}\r\n\r\nfloat4 WaterPS(PSIn i) : SV_TARGET\r\n{\r\n    float2 p = i.wpos.xz;\r\n    float3 n = float3(0, 1, 0);\r\n    [unroll] for (int k = 0; k < 6; k++)           // ondas somadas\r\n    {\r\n        float2 d = float2(cos(k * 1.7), sin(k * 1.7));\r\n        float f = 0.15 * (k + 1), ph = dot(d, p) * f + Time * (1 + k * 0.4);\r\n        n.xz += d * cos(ph) * 0.06 / (k + 1);\r\n    }\r\n    n = normalize(n);\r\n    float3 v = normalize(i.wpos - CamPos);\r\n    float fres = 0.04 + 0.96 * pow(1 - saturate(dot(-v, n)), 5);\r\n    float3 refl = Sky(reflect(v, n)) * Shadow(i.wpos, n);\r\n    float3 col = lerp(float3(0.05, 0.05, 0.05), refl, fres);\r\n    float fog = 1 - exp(-length(i.wpos - CamPos) * 0.0022);\r\n    return float4(lerp(col, Sky(v), fog), 1);\r\n}\r\n\r\nstruct FsOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };\r\nFsOut FullscreenVS(uint id : SV_VertexID)\r\n{\r\n    FsOut o; o.uv = float2((id << 1) & 2, id & 2);\r\n    o.pos = float4(o.uv * float2(2, -2) + float2(-1, 1), 1, 1);\r\n    return o;\r\n}\r\n\r\nfloat4 SkyPS(FsOut i) : SV_TARGET\r\n{\r\n    float4 w = mul(float4(i.uv * float2(2, -2) + float2(-1, 1), 1, 1), InvViewProj);\r\n    return float4(Sky(normalize(w.xyz / w.w - CamPos)), 1);\r\n}\r\n\r\nfloat4 PostPS(FsOut i) : SV_TARGET\r\n{\r\n    float3 c = Hdr.Sample(LinearSampler, i.uv).rgb;\r\n    c = c * (2.51 * c + 0.03) / (c * (2.43 * c + 0.59) + 0.14);         // ACES\r\n    float2 d = i.uv - 0.5;\r\n    c *= 1 - dot(d, d) * 0.9;                                           // vinheta\r\n    float g = pow(saturate(c.r), 1 / 2.2);\r\n    return float4(g, g, g, 1);\r\n}";

	public int Width { get; init; } = 1920;

	public int Height { get; init; } = 1080;

	public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(60.0);

	public string AdapterName { get; init; } = "";

	public double Progress { get; private set; }

	public double CurrentFps { get; private set; }

	public string? Error { get; private set; }

	public bool Cancelled { get; private set; }

	public void Cancel()
	{
		_stop = true;
	}

	public static float TerrainHeight(float x, float z)
	{
		float h = 0f;
		float amp = 1f;
		float freq = 0.005f;
		for (int o = 0; o < 6; o++)
		{
			h += amp * ValueNoise(x * freq + (float)o * 17.3f, z * freq - (float)o * 9.1f);
			amp *= 0.5f;
			freq *= 2.03f;
		}
		h = h * 55f + 10f;
		float river = MathF.Sin(z * 0.007f) * 120f;
		float d = MathF.Abs(x - river);
		float valley = MathF.Min(1f, d / 90f);
		return h * (0.25f + 0.75f * valley * valley) - (1f - valley) * 4f;
	}

	private static float ValueNoise(float x, float z)
	{
		int xi = (int)MathF.Floor(x);
		int zi = (int)MathF.Floor(z);
		float fx = x - (float)xi;
		float fz = z - (float)zi;
		float u = fx * fx * (3f - 2f * fx);
		float v = fz * fz * (3f - 2f * fz);
		float a = Hash(xi, zi);
		float b = Hash(xi + 1, zi);
		float c = Hash(xi, zi + 1);
		float d = Hash(xi + 1, zi + 1);
		return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
	}

	private static float Hash(int x, int z)
	{
		int num = x * 374761393 + z * 668265263;
		int num2 = (num ^ (num >>> 13)) * 1274126177;
		return (float)(uint)((num2 ^ (num2 >>> 16)) & 0xFFFFFF) / 16777215f;
	}

	private static (Vertex[] V, uint[] I) BuildTerrain()
	{
		Vertex[] v = new Vertex[262144];
		float step = 1.5655577f;
		float half = 400f;
		for (int z = 0; z < 512; z++)
		{
			for (int x = 0; x < 512; x++)
			{
				float wx = (float)x * step - half;
				float wz = (float)z * step - half;
				float h = TerrainHeight(wx, wz);
				float hx = TerrainHeight(wx + 0.5f, wz) - TerrainHeight(wx - 0.5f, wz);
				float hz = TerrainHeight(wx, wz + 0.5f) - TerrainHeight(wx, wz - 0.5f);
				v[z * 512 + x] = new Vertex
				{
					Pos = new Vector3(wx, h, wz),
					Normal = Vector3.Normalize(new Vector3(0f - hx, 1f, 0f - hz)),
					Mat = 0f
				};
			}
		}
		uint[] idx = new uint[1566726];
		int k = 0;
		for (int i = 0; i < 511; i++)
		{
			for (int j = 0; j < 511; j++)
			{
				uint a = (uint)(i * 512 + j);
				uint b = a + 1;
				uint c = a + 512;
				uint d = c + 1;
				idx[k++] = a;
				idx[k++] = c;
				idx[k++] = b;
				idx[k++] = b;
				idx[k++] = c;
				idx[k++] = d;
			}
		}
		return (V: v, I: idx);
	}

	private static (Vertex[] V, uint[] I) BuildTree()
	{
		List<Vertex> v = new List<Vertex>();
		List<uint> idx = new List<uint>();
		Cone(0f, 2f, 0.25f, 0.2f, 8, 1f);
		Cone(1.4f, 2.6f, 2f, 0.05f, 12, 2f);
		Cone(2.8f, 2.3f, 1.6f, 0.05f, 12, 2f);
		Cone(4f, 2f, 1.1f, 0.02f, 12, 2f);
		return (V: v.ToArray(), I: idx.ToArray());
		void Cone(float baseY, float height, float r0, float r1, int seg, float mat)
		{
			uint start = (uint)v.Count;
			for (int i = 0; i <= seg; i++)
			{
				float x = (float)i / (float)seg * ((float)Math.PI * 2f);
				float c = MathF.Cos(x);
				float s = MathF.Sin(x);
				float slope = (r0 - r1) / height;
				Vector3 n = Vector3.Normalize(new Vector3(c, slope, s));
				v.Add(new Vertex
				{
					Pos = new Vector3(c * r0, baseY, s * r0),
					Normal = n,
					Mat = mat
				});
				v.Add(new Vertex
				{
					Pos = new Vector3(c * r1, baseY + height, s * r1),
					Normal = n,
					Mat = mat
				});
			}
			for (int j = 0; j < seg; j++)
			{
				uint a = start + (uint)(j * 2);
				idx.AddRange(new uint[6]
				{
					a,
					a + 1,
					a + 2,
					a + 2,
					a + 1,
					a + 3
				});
			}
		}
	}

	private static Vector4[] PlaceTrees()
	{
		List<Vector4> list = new List<Vector4>(40000);
		uint s = 2024u;
		while (list.Count < 40000)
		{
			float x = (R() - 0.5f) * 800f * 0.98f;
			float z = (R() - 0.5f) * 800f * 0.98f;
			float h = TerrainHeight(x, z);
			float slope = MathF.Abs(TerrainHeight(x + 1f, z) - TerrainHeight(x - 1f, z)) + MathF.Abs(TerrainHeight(x, z + 1f) - TerrainHeight(x, z - 1f));
			if (!(h < 7.5f) && !(h > 48f) && !(slope > 1.4f))
			{
				list.Add(new Vector4(x, h - 0.2f, z, 0.7f + R() * 0.9f));
			}
		}
		return list.ToArray();
		float R()
		{
			s ^= s << 13;
			s ^= s >> 17;
			s ^= s << 5;
			return (float)(s >> 8) / 16777216f;
		}
	}

	private static Vector2[] BuildPath()
	{
		List<Vector2> p = new List<Vector2>();
		for (int i = 0; i <= 6; i++)
		{
			float z = -330 + i * 110;
			p.Add(new Vector2(MathF.Sin(z * 0.007f) * 120f + 45f, z));
		}
		for (int i2 = 6; i2 >= 0; i2--)
		{
			float z2 = -330 + i2 * 110;
			p.Add(new Vector2(MathF.Sin(z2 * 0.007f) * 120f - 75f, z2));
		}
		return p.ToArray();
	}

	private static Vector2 PathAt(float t)
	{
		int n = Path.Length;
		float num = (t % 1f + 1f) % 1f * (float)n;
		int i = (int)num;
		float u = num - (float)i;
		Vector2 p0 = Path[(i - 1 + n) % n];
		Vector2 p1 = Path[i];
		Vector2 p2 = Path[(i + 1) % n];
		Vector2 p3 = Path[(i + 2) % n];
		return 0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u);
	}

	private static float CamHeight(Vector2 p)
	{
		float h = 6f;
		for (int dz = -2; dz <= 2; dz++)
		{
			for (int dx = -2; dx <= 2; dx++)
			{
				h = MathF.Max(h, TerrainHeight(p.X + (float)(dx * 10), p.Y + (float)(dz * 10)));
			}
		}
		return h + 26f;
	}

	public GpuGameResult? Run()
	{
		ThreadPriority oldPriority = Thread.CurrentThread.Priority;
		Thread.CurrentThread.Priority = ThreadPriority.Highest;
		nint hwnd = IntPtr.Zero;
		string cls = "TriarchGameBench" + Environment.TickCount64;
		nint hInst = GetModuleHandleW(null);
		List<IDisposable> keep = new List<IDisposable>();
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
			hwnd = CreateWindowExW(0u, cls, "TRIARCH BENCH · Cena de jogo", 282001408u, (sw - ww) / 2, (sh - wh) / 2, ww, wh, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
			IDXGIFactory2 factory = K<IDXGIFactory2>(DXGI.CreateDXGIFactory1<IDXGIFactory2>());
			List<IDXGIAdapter1> adapters = new List<IDXGIAdapter1>();
			IDXGIAdapter1 a;
			for (uint i = 0u; factory.EnumAdapters1(i, out a).Success; i++)
			{
				K<IDXGIAdapter1>(a);
				if ((a.Description1.Flags & AdapterFlags.Software) == 0)
				{
					adapters.Add(a);
				}
			}
			IDXGIAdapter1? obj = adapters.FirstOrDefault((IDXGIAdapter1 iDXGIAdapter) => AdapterName.Length > 0 && iDXGIAdapter.Description1.Description.Contains(AdapterName, StringComparison.OrdinalIgnoreCase)) ?? adapters.OrderByDescending((IDXGIAdapter1 iDXGIAdapter) => iDXGIAdapter.Description1.DedicatedVideoMemory).First();
			string adapterName = obj.Description1.Description;
			D3D11.D3D11CreateDevice((IDXGIAdapter?)obj, DriverType.Unknown, DeviceCreationFlags.BgraSupport, new FeatureLevel[2]
			{
				FeatureLevel.Level_11_1,
				FeatureLevel.Level_11_0
			}, out ID3D11Device device, out ID3D11DeviceContext ctx).CheckError();
			K<ID3D11Device>(device);
			K<ID3D11DeviceContext>(ctx);
			IDXGISwapChain1 swap = K<IDXGISwapChain1>(factory.CreateSwapChainForHwnd(device, hwnd, new SwapChainDescription1((uint)Width, (uint)Height)));
			factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);
			ID3D11Texture2D backBuffer = K<ID3D11Texture2D>(swap.GetBuffer<ID3D11Texture2D>(0u));
			Texture2DDescription hdrDesc = Tex(Format.R16G16B16A16_Float, Width, Height, BindFlags.ShaderResource | BindFlags.RenderTarget);
			ID3D11Texture2D hdr = K<ID3D11Texture2D>(device.CreateTexture2D(in hdrDesc));
			ID3D11RenderTargetView hdrRtv = K<ID3D11RenderTargetView>(device.CreateRenderTargetView(hdr));
			ID3D11ShaderResourceView hdrSrv = K<ID3D11ShaderResourceView>(device.CreateShaderResourceView(hdr));
			Texture2DDescription depthDesc = Tex(Format.D32_Float, Width, Height, BindFlags.DepthStencil);
			ID3D11Texture2D depth = K<ID3D11Texture2D>(device.CreateTexture2D(in depthDesc));
			ID3D11DepthStencilView dsv = K<ID3D11DepthStencilView>(device.CreateDepthStencilView(depth));
			Texture2DDescription ldrDesc = Tex(Format.B8G8R8A8_UNorm, Width, Height, BindFlags.RenderTarget);
			ID3D11Texture2D ldr = K<ID3D11Texture2D>(device.CreateTexture2D(in ldrDesc));
			ID3D11RenderTargetView ldrRtv = K<ID3D11RenderTargetView>(device.CreateRenderTargetView(ldr));
			Texture2DDescription smDesc = Tex(Format.R32_Typeless, 4096, 4096, BindFlags.ShaderResource | BindFlags.DepthStencil);
			ID3D11Texture2D shadowTex = K<ID3D11Texture2D>(device.CreateTexture2D(in smDesc));
			ID3D11DepthStencilView shadowDsv = K<ID3D11DepthStencilView>(device.CreateDepthStencilView(shadowTex, new DepthStencilViewDescription(shadowTex, DepthStencilViewDimension.Texture2D, Format.D32_Float)));
			ID3D11ShaderResourceView shadowSrv = K<ID3D11ShaderResourceView>(device.CreateShaderResourceView(shadowTex, new ShaderResourceViewDescription(shadowTex, ShaderResourceViewDimension.Texture2D, Format.R32_Float)));
			ReadOnlyMemory<byte> terrainVsCode = C("TerrainVS", "vs_5_0");
			ID3D11VertexShader terrainVs = K<ID3D11VertexShader>(device.CreateVertexShader(terrainVsCode.Span));
			ID3D11VertexShader treeVs = K<ID3D11VertexShader>(device.CreateVertexShader(C("TreeVS", "vs_5_0").Span));
			ID3D11VertexShader waterVs = K<ID3D11VertexShader>(device.CreateVertexShader(C("WaterVS", "vs_5_0").Span));
			ID3D11VertexShader terrainShadowVs = K<ID3D11VertexShader>(device.CreateVertexShader(C("TerrainShadowVS", "vs_5_0").Span));
			ID3D11VertexShader treeShadowVs = K<ID3D11VertexShader>(device.CreateVertexShader(C("TreeShadowVS", "vs_5_0").Span));
			ID3D11VertexShader fullscreenVs = K<ID3D11VertexShader>(device.CreateVertexShader(C("FullscreenVS", "vs_5_0").Span));
			ID3D11PixelShader terrainPs = K<ID3D11PixelShader>(device.CreatePixelShader(C("TerrainPS", "ps_5_0").Span));
			ID3D11PixelShader treePs = K<ID3D11PixelShader>(device.CreatePixelShader(C("TreePS", "ps_5_0").Span));
			ID3D11PixelShader waterPs = K<ID3D11PixelShader>(device.CreatePixelShader(C("WaterPS", "ps_5_0").Span));
			ID3D11PixelShader skyPs = K<ID3D11PixelShader>(device.CreatePixelShader(C("SkyPS", "ps_5_0").Span));
			ID3D11PixelShader postPs = K<ID3D11PixelShader>(device.CreatePixelShader(C("PostPS", "ps_5_0").Span));
			ID3D11InputLayout layout = K<ID3D11InputLayout>(device.CreateInputLayout(new InputElementDescription[3]
			{
				new InputElementDescription("POSITION", 0u, Format.R32G32B32_Float, 0u, 0u),
				new InputElementDescription("NORMAL", 0u, Format.R32G32B32_Float, 12u, 0u),
				new InputElementDescription("TEXCOORD", 0u, Format.R32_Float, 24u, 0u)
			}, terrainVsCode.Span));
			(Vertex[] V, uint[] I) tuple = BuildTerrain();
			Vertex[] tv = tuple.V;
			uint[] ti = tuple.I;
			ID3D11Buffer terrainVb = K<ID3D11Buffer>(device.CreateBuffer(tv, BindFlags.VertexBuffer));
			ID3D11Buffer terrainIb = K<ID3D11Buffer>(device.CreateBuffer(ti, BindFlags.IndexBuffer));
			(Vertex[] V, uint[] I) tuple2 = BuildTree();
			Vertex[] treeV = tuple2.V;
			uint[] treeI = tuple2.I;
			ID3D11Buffer treeVb = K<ID3D11Buffer>(device.CreateBuffer(treeV, BindFlags.VertexBuffer));
			ID3D11Buffer treeIb = K<ID3D11Buffer>(device.CreateBuffer(treeI, BindFlags.IndexBuffer));
			Vector4[] trees = PlaceTrees();
			ID3D11Buffer treeBuf = K<ID3D11Buffer>(device.CreateBuffer(trees, BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 0u, 16u));
			ID3D11ShaderResourceView treeSrv = K<ID3D11ShaderResourceView>(device.CreateShaderResourceView(treeBuf));
			float wh2 = 400f;
			Vertex[] water = new Vertex[4]
			{
				new Vertex
				{
					Pos = new Vector3(0f - wh2, 6f, 0f - wh2)
				},
				new Vertex
				{
					Pos = new Vector3(wh2, 6f, 0f - wh2)
				},
				new Vertex
				{
					Pos = new Vector3(0f - wh2, 6f, wh2)
				},
				new Vertex
				{
					Pos = new Vector3(wh2, 6f, wh2)
				}
			};
			ID3D11Buffer waterVb = K<ID3D11Buffer>(device.CreateBuffer(water, BindFlags.VertexBuffer));
			ID3D11Buffer waterIb = K<ID3D11Buffer>(device.CreateBuffer(new uint[6] { 0u, 2u, 1u, 1u, 2u, 3u }, BindFlags.IndexBuffer));
			ID3D11Buffer cb = K<ID3D11Buffer>(device.CreateBuffer((uint)Marshal.SizeOf<FrameCB>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
			ID3D11RasterizerState rsMain = K<ID3D11RasterizerState>(device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid)));
			ID3D11RasterizerState rsShadow = K<ID3D11RasterizerState>(device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid)
			{
				DepthBias = 800,
				SlopeScaledDepthBias = 2f,
				DepthClipEnable = true
			}));
			ID3D11DepthStencilState noDepth = K<ID3D11DepthStencilState>(device.CreateDepthStencilState(DepthStencilDescription.None));
			ID3D11SamplerState cmpSampler = K<ID3D11SamplerState>(device.CreateSamplerState(new SamplerDescription
			{
				Filter = Filter.ComparisonMinMagLinearMipPoint,
				AddressU = TextureAddressMode.Clamp,
				AddressV = TextureAddressMode.Clamp,
				AddressW = TextureAddressMode.Clamp,
				ComparisonFunc = ComparisonFunction.LessEqual,
				MaxLOD = float.MaxValue
			}));
			ID3D11SamplerState linSampler = K<ID3D11SamplerState>(device.CreateSamplerState(SamplerDescription.LinearClamp));
			Vector3 sunDir = Vector3.Normalize(new Vector3(-0.45f, -0.55f, -0.35f));
			Matrix4x4 matrix4x = Matrix4x4.CreateLookAt(-sunDir * 900f, Vector3.Zero, Vector3.UnitY);
			Matrix4x4 lightProj = Matrix4x4.CreateOrthographic(900f, 900f, 1f, 1900f);
			Matrix4x4 lightVP = matrix4x * lightProj;
			Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView((float)Math.PI / 3f, (float)Width / (float)Height, 0.5f, 2000f);
			uint vStride = (uint)Marshal.SizeOf<Vertex>();
			ID3D11Query[] fences = new ID3D11Query[8];
			for (int i2 = 0; i2 < 8; i2++)
			{
				fences[i2] = K<ID3D11Query>(device.CreateQuery(QueryType.Event));
			}
			List<double> frameTimes = new List<double>(20000);
			Stopwatch clock = Stopwatch.StartNew();
			double lastDone = -1.0;
			double lastPresent = 0.0;
			double lastTitle = 0.0;
			int titleFrames = 0;
			long frame = 0L;
			int frames = 0;
			while (!_stop && clock.Elapsed < Duration)
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
				ID3D11Query fence = fences[frame % 8];
				if (frame >= 8)
				{
					while (!ctx.IsDataAvailable(fence) && !_stop)
					{
						Thread.SpinWait(20);
					}
					double done = clock.Elapsed.TotalMilliseconds;
					if (lastDone >= 0.0)
					{
						frameTimes.Add(done - lastDone);
					}
					lastDone = done;
					frames++;
					titleFrames++;
				}
				float num = (float)(clock.Elapsed.TotalSeconds / Duration.TotalSeconds);
				Vector2 p2 = PathAt(num);
				Vector2 ahead = PathAt(num + 0.035f);
				Vector3 eye = new Vector3(p2.X, CamHeight(p2), p2.Y);
				Vector3 target = new Vector3(ahead.X, CamHeight(ahead) - 22f, ahead.Y);
				Matrix4x4 vp = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY) * proj;
				Matrix4x4.Invert(vp, out var inv);
				Marshal.StructureToPtr(new FrameCB
				{
					ViewProj = vp,
					LightViewProj = lightVP,
					InvViewProj = inv,
					CamPos = eye,
					Time = (float)clock.Elapsed.TotalSeconds,
					SunDir = sunDir
				}, ctx.Map(cb, MapMode.WriteDiscard).DataPointer, fDeleteOld: false);
				ctx.Unmap(cb);
				ctx.VSSetConstantBuffer(0u, cb);
				ctx.PSSetConstantBuffer(0u, cb);
				ctx.IASetInputLayout(layout);
				ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
				ctx.VSSetShaderResource(1u, treeSrv);
				ctx.PSSetShaderResource(0u, null);
				ctx.OMSetRenderTargets((ID3D11RenderTargetView)null, shadowDsv);
				ctx.ClearDepthStencilView(shadowDsv, DepthStencilClearFlags.Depth, 1f, 0);
				ctx.RSSetViewport(0f, 0f, 4096f, 4096f);
				ctx.RSSetState(rsShadow);
				ctx.OMSetDepthStencilState(null);
				ctx.PSSetShader(null);
				ctx.VSSetShader(terrainShadowVs);
				ctx.IASetVertexBuffer(0u, terrainVb, vStride);
				ctx.IASetIndexBuffer(terrainIb, Format.R32_UInt, 0u);
				ctx.DrawIndexed((uint)ti.Length, 0u, 0);
				ctx.VSSetShader(treeShadowVs);
				ctx.IASetVertexBuffer(0u, treeVb, vStride);
				ctx.IASetIndexBuffer(treeIb, Format.R32_UInt, 0u);
				ctx.DrawIndexedInstanced((uint)treeI.Length, (uint)trees.Length, 0u, 0, 0u);
				ctx.OMSetRenderTargets(hdrRtv, dsv);
				ctx.ClearDepthStencilView(dsv, DepthStencilClearFlags.Depth, 1f, 0);
				ctx.RSSetViewport(0f, 0f, Width, Height);
				ctx.RSSetState(rsMain);
				ctx.PSSetShaderResource(0u, shadowSrv);
				ctx.PSSetSampler(0u, cmpSampler);
				ctx.PSSetSampler(1u, linSampler);
				ctx.OMSetDepthStencilState(noDepth);
				ctx.VSSetShader(fullscreenVs);
				ctx.PSSetShader(skyPs);
				ctx.Draw(3u, 0u);
				ctx.OMSetDepthStencilState(null);
				ctx.VSSetShader(terrainVs);
				ctx.PSSetShader(terrainPs);
				ctx.IASetVertexBuffer(0u, terrainVb, vStride);
				ctx.IASetIndexBuffer(terrainIb, Format.R32_UInt, 0u);
				ctx.DrawIndexed((uint)ti.Length, 0u, 0);
				ctx.VSSetShader(treeVs);
				ctx.PSSetShader(treePs);
				ctx.IASetVertexBuffer(0u, treeVb, vStride);
				ctx.IASetIndexBuffer(treeIb, Format.R32_UInt, 0u);
				ctx.DrawIndexedInstanced((uint)treeI.Length, (uint)trees.Length, 0u, 0, 0u);
				ctx.VSSetShader(waterVs);
				ctx.PSSetShader(waterPs);
				ctx.IASetVertexBuffer(0u, waterVb, vStride);
				ctx.IASetIndexBuffer(waterIb, Format.R32_UInt, 0u);
				ctx.DrawIndexed(6u, 0u, 0);
				ctx.OMSetRenderTargets(ldrRtv);
				ctx.OMSetDepthStencilState(noDepth);
				ctx.PSSetShaderResource(0u, null);
				ctx.PSSetShaderResource(2u, hdrSrv);
				ctx.VSSetShader(fullscreenVs);
				ctx.PSSetShader(postPs);
				ctx.Draw(3u, 0u);
				ctx.PSSetShaderResource(2u, null);
				ctx.End(fence);
				ctx.Flush();
				frame++;
				double now = clock.Elapsed.TotalSeconds;
				if (now - lastPresent >= 1.0 / 60.0)
				{
					ctx.CopyResource(backBuffer, ldr);
					if (swap.Present(0u, PresentFlags.DoNotWait).Success)
					{
						lastPresent = now;
					}
				}
				if (now - lastTitle >= 0.5)
				{
					CurrentFps = (double)titleFrames / (now - lastTitle);
					titleFrames = 0;
					lastTitle = now;
					Progress = now / Duration.TotalSeconds;
					SetWindowTextW(hwnd, $"TRIARCH BENCH · Cena de jogo · {Width}x{Height} · {CurrentFps:0} FPS · {Progress:P0}");
				}
			}
			if (_stop)
			{
				Cancelled = true;
				return null;
			}
			double[] sorted = frameTimes.OrderBy((double x) => x).ToArray();
			double p99 = ((sorted.Length != 0) ? sorted[Math.Min(sorted.Length - 1, (int)((double)sorted.Length * 0.99))] : 0.0);
			double secs = frameTimes.Sum() / 1000.0;
			return new GpuGameResult((secs > 0.0) ? ((double)frameTimes.Count / secs) : 0.0, (p99 > 0.0) ? (1000.0 / p99) : 0.0, frames, clock.Elapsed.TotalSeconds, Width, Height, adapterName);
		}
		catch (Exception ex)
		{
			Error = ex.Message;
			return null;
		}
		finally
		{
			for (int i3 = keep.Count - 1; i3 >= 0; i3--)
			{
				try
				{
					keep[i3].Dispose();
				}
				catch
				{
				}
			}
			if (hwnd != IntPtr.Zero)
			{
				DestroyWindow(hwnd);
			}
			UnregisterClassW(cls, hInst);
			Thread.CurrentThread.Priority = oldPriority;
		}
		static ReadOnlyMemory<byte> C(string entry, string profile)
		{
			return Compiler.Compile("\r\ncbuffer Frame : register(b0)\r\n{\r\n    row_major float4x4 ViewProj;\r\n    row_major float4x4 LightViewProj;\r\n    row_major float4x4 InvViewProj;\r\n    float3 CamPos; float Time;\r\n    float3 SunDir; float Pad;\r\n};\r\nStructuredBuffer<float4> Trees : register(t1);\r\nTexture2D<float> ShadowMap : register(t0);\r\nSamplerComparisonState ShadowSampler : register(s0);\r\nTexture2D<float4> Hdr : register(t2);\r\nSamplerState LinearSampler : register(s1);\r\n\r\nstruct VSIn { float3 pos : POSITION; float3 nrm : NORMAL; float mat : TEXCOORD0; uint iid : SV_InstanceID; };\r\nstruct PSIn { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 nrm : NORMAL; float mat : TEXCOORD1; };\r\n\r\nfloat3 TreeWorld(float3 p, uint iid, out float3x3 rot)\r\n{\r\n    float4 t = Trees[iid];\r\n    float a = frac(sin(iid * 12.9898) * 43758.5453) * 6.2831;\r\n    float c = cos(a), s = sin(a);\r\n    rot = float3x3(c, 0, -s, 0, 1, 0, s, 0, c);\r\n    float sway = sin(Time * 1.3 + iid) * 0.03 * p.y;   // vento\r\n    return mul(p, rot) * t.w + t.xyz + float3(sway, 0, sway * 0.5);\r\n}\r\n\r\nPSIn TerrainVS(VSIn i)\r\n{\r\n    PSIn o; o.wpos = i.pos; o.nrm = i.nrm; o.mat = i.mat;\r\n    o.pos = mul(float4(i.pos, 1), ViewProj);\r\n    return o;\r\n}\r\n\r\nPSIn TreeVS(VSIn i)\r\n{\r\n    PSIn o; float3x3 rot;\r\n    o.wpos = TreeWorld(i.pos, i.iid, rot);\r\n    o.nrm = mul(i.nrm, rot); o.mat = i.mat;\r\n    o.pos = mul(float4(o.wpos, 1), ViewProj);\r\n    return o;\r\n}\r\n\r\nfloat4 TerrainShadowVS(VSIn i) : SV_POSITION { return mul(float4(i.pos, 1), LightViewProj); }\r\nfloat4 TreeShadowVS(VSIn i) : SV_POSITION { float3x3 r; return mul(float4(TreeWorld(i.pos, i.iid, r), 1), LightViewProj); }\r\n\r\nfloat3 Sky(float3 dir)\r\n{\r\n    float up = saturate(dir.y);\r\n    float3 col = lerp(float3(0.42, 0.42, 0.42), float3(0.12, 0.12, 0.12), pow(up, 0.6));\r\n    float sun = saturate(dot(dir, -SunDir));\r\n    col += pow(sun, 600) * 12 + pow(sun, 12) * 0.25;\r\n    return col;\r\n}\r\n\r\nfloat Shadow(float3 wpos, float3 n)\r\n{\r\n    float4 lp = mul(float4(wpos + n * 0.15, 1), LightViewProj);\r\n    float2 uv = lp.xy * float2(0.5, -0.5) + 0.5;\r\n    if (any(uv < 0) || any(uv > 1)) return 1;\r\n    float sum = 0;\r\n    [unroll] for (int y = -2; y <= 2; y++)\r\n        [unroll] for (int x = -2; x <= 2; x++)\r\n            sum += ShadowMap.SampleCmpLevelZero(ShadowSampler, uv + float2(x, y) / 4096.0, lp.z - 0.0015);\r\n    return sum / 25;\r\n}\r\n\r\nfloat3 Shade(float3 wpos, float3 n, float albedo)\r\n{\r\n    float3 l = -SunDir;\r\n    float ndl = saturate(dot(n, l));\r\n    float3 direct = ndl * Shadow(wpos, n) * 1.5;\r\n    float3 ambient = lerp(0.06, 0.16, n.y * 0.5 + 0.5);\r\n    float3 col = albedo * (direct + ambient);\r\n    float dist = length(wpos - CamPos);\r\n    float fog = 1 - exp(-dist * 0.0022);\r\n    return lerp(col, Sky(normalize(wpos - CamPos)), fog);\r\n}\r\n\r\nfloat4 TerrainPS(PSIn i) : SV_TARGET\r\n{\r\n    float3 n = normalize(i.nrm);\r\n    float slope = 1 - n.y;\r\n    float h = i.wpos.y;\r\n    float albedo = lerp(0.22, 0.15, saturate((h - 8) / 25));               // grama\r\n    albedo = lerp(albedo, 0.28, saturate((slope - 0.25) * 4));             // rocha nas encostas\r\n    albedo = lerp(albedo, 0.70, saturate((h - 62) / 10) * saturate(1 - slope * 3)); // neve só nos picos\r\n    albedo *= 0.9 + 0.1 * frac(sin(dot(floor(i.wpos.xz * 2), float2(12.9898, 78.233))) * 43758.5453);\r\n    return float4(Shade(i.wpos, n, albedo), 1);\r\n}\r\n\r\nfloat4 TreePS(PSIn i) : SV_TARGET\r\n{\r\n    float albedo = i.mat < 1.5 ? 0.08 : 0.11;\r\n    return float4(Shade(i.wpos, normalize(i.nrm), albedo), 1);\r\n}\r\n\r\nPSIn WaterVS(VSIn i)\r\n{\r\n    PSIn o; o.wpos = i.pos; o.nrm = float3(0, 1, 0); o.mat = 0;\r\n    o.pos = mul(float4(i.pos, 1), ViewProj);\r\n    return o;\r\n}\r\n\r\nfloat4 WaterPS(PSIn i) : SV_TARGET\r\n{\r\n    float2 p = i.wpos.xz;\r\n    float3 n = float3(0, 1, 0);\r\n    [unroll] for (int k = 0; k < 6; k++)           // ondas somadas\r\n    {\r\n        float2 d = float2(cos(k * 1.7), sin(k * 1.7));\r\n        float f = 0.15 * (k + 1), ph = dot(d, p) * f + Time * (1 + k * 0.4);\r\n        n.xz += d * cos(ph) * 0.06 / (k + 1);\r\n    }\r\n    n = normalize(n);\r\n    float3 v = normalize(i.wpos - CamPos);\r\n    float fres = 0.04 + 0.96 * pow(1 - saturate(dot(-v, n)), 5);\r\n    float3 refl = Sky(reflect(v, n)) * Shadow(i.wpos, n);\r\n    float3 col = lerp(float3(0.05, 0.05, 0.05), refl, fres);\r\n    float fog = 1 - exp(-length(i.wpos - CamPos) * 0.0022);\r\n    return float4(lerp(col, Sky(v), fog), 1);\r\n}\r\n\r\nstruct FsOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };\r\nFsOut FullscreenVS(uint id : SV_VertexID)\r\n{\r\n    FsOut o; o.uv = float2((id << 1) & 2, id & 2);\r\n    o.pos = float4(o.uv * float2(2, -2) + float2(-1, 1), 1, 1);\r\n    return o;\r\n}\r\n\r\nfloat4 SkyPS(FsOut i) : SV_TARGET\r\n{\r\n    float4 w = mul(float4(i.uv * float2(2, -2) + float2(-1, 1), 1, 1), InvViewProj);\r\n    return float4(Sky(normalize(w.xyz / w.w - CamPos)), 1);\r\n}\r\n\r\nfloat4 PostPS(FsOut i) : SV_TARGET\r\n{\r\n    float3 c = Hdr.Sample(LinearSampler, i.uv).rgb;\r\n    c = c * (2.51 * c + 0.03) / (c * (2.43 * c + 0.59) + 0.14);         // ACES\r\n    float2 d = i.uv - 0.5;\r\n    c *= 1 - dot(d, d) * 0.9;                                           // vinheta\r\n    float g = pow(saturate(c.r), 1 / 2.2);\r\n    return float4(g, g, g, 1);\r\n}", entry, "scene.hlsl", profile, ShaderFlags.OptimizationLevel3);
		}
		T K<T>(T d) where T : notnull, IDisposable
		{
			keep.Add(d);
			return d;
		}
		static Texture2DDescription Tex(Format f, int w, int h, BindFlags b)
		{
			return new Texture2DDescription(f, (uint)w, (uint)h, 1u, 1u, b);
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
