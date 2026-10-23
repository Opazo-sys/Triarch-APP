using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Triarch;

public sealed class GpuBurn
{
	private struct Vertex
	{
		public Vector3 Pos;

		public Vector3 Normal;

		public Vector2 Uv;
	}

	private struct Constants
	{
		public Matrix4x4 World;

		public Matrix4x4 ViewProj;

		public float Time;

		public float ShellCount;

		public float FurLength;

		public float Iterations;
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

	private Thread? _thread;

	private volatile bool _stop;

	private long _frames;

	private double _seconds;

	private WndProc? _wndProc;

	private long _presents;

	private const string Hlsl = "\r\ncbuffer CB : register(b0)\r\n{\r\n    row_major float4x4 World;\r\n    row_major float4x4 ViewProj;\r\n    float Time; float ShellCount; float FurLength; float Iterations;\r\n};\r\n\r\nstruct VSIn  { float3 pos : POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; uint inst : SV_InstanceID; };\r\nstruct PSIn  { float4 pos : SV_POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; float shell : TEXCOORD1; };\r\n\r\nPSIn VS(VSIn i)\r\n{\r\n    PSIn o;\r\n    float s = i.inst / ShellCount;                       // 0 = raiz, 1 = ponta\r\n    float3 p = i.pos + i.nrm * (FurLength * s);\r\n    float3 w = mul(float4(p, 1), World).xyz;\r\n    w.y -= 0.10 * s * s;                                  // gravidade\r\n    w   += 0.025 * s * sin(Time * 2.7 + i.pos.yzx * 7.0); // vento\r\n    o.pos = mul(float4(w, 1), ViewProj);\r\n    o.nrm = normalize(mul(float4(i.nrm, 0), World).xyz);\r\n    o.uv = i.uv;\r\n    o.shell = s;\r\n    return o;\r\n}\r\n\r\nfloat hash(float2 p)\r\n{\r\n    p = frac(p * float2(123.34, 456.21));\r\n    p += dot(p, p + 45.32);\r\n    return frac(p.x * p.y);\r\n}\r\n\r\nfloat4 PS(PSIn i) : SV_TARGET\r\n{\r\n    float h = hash(floor(i.uv * float2(900, 380)));\r\n\r\n    // Carga de ALU: cadeia de FMAs vetoriais (o que mais consome energia na GPU), antes do descarte\r\n    // e ligada a ele, para o compilador não poder pular o cálculo.\r\n    float4 a = float4(h, i.uv, i.shell), b = a.wzyx + 0.5;\r\n    [loop] for (int k = 0; k < (int)Iterations; k++)\r\n    {\r\n        a = mad(a, 1.0001, b * 0.0003);\r\n        b = mad(b, 0.9997, a * 0.0002);\r\n    }\r\n    float acc = frac(dot(a, b));\r\n\r\n    if (i.shell > 0 && h + acc * 1e-6 < i.shell) discard; // fios afinam até a ponta\r\n\r\n    float3 L = normalize(float3(-0.4, 0.8, -0.5));\r\n    float diff = saturate(dot(i.nrm, L)) * 0.75 + 0.25;\r\n    float ao = lerp(0.18, 1.0, i.shell);                  // raiz escura, ponta clara\r\n    float c = diff * ao * (0.9 + 0.1 * acc);\r\n    return float4(c, c, c, lerp(1.0, 0.55, i.shell));     // pontas translúcidas\r\n}";

	private const uint WS_OVERLAPPEDWINDOW = 13565952u;

	private const uint WS_POPUP = 2147483648u;

	private const uint WS_VISIBLE = 268435456u;

	public bool Running => _thread?.IsAlive ?? false;

	public double Fps { get; private set; }

	public long Frames => Interlocked.Read(in _frames);

	public long Presents => Interlocked.Read(in _presents);

	public double AverageFps
	{
		get
		{
			if (!(_seconds > 0.0))
			{
				return 0.0;
			}
			return (double)Frames / _seconds;
		}
	}

	public string? Error { get; private set; }

	public string AdapterUsed { get; private set; } = "";

	public bool ClosedByUser { get; private set; }

	public void Start(GpuBurnSettings s)
	{
		if (!Running)
		{
			_stop = false;
			_frames = 0L;
			_seconds = 0.0;
			Fps = 0.0;
			Error = null;
			ClosedByUser = false;
			_thread = new Thread(() =>
			{
				Run(s);
			})
			{
				IsBackground = true,
				Name = "GPU burn"
			};
			_thread.Start();
		}
	}

	public void Stop()
	{
		_stop = true;
		_thread?.Join(3000);
	}

	private void Run(GpuBurnSettings s)
	{
		nint hwnd = IntPtr.Zero;
		string cls = "TriarchGpuBurn" + Environment.TickCount64;
		nint hInst = GetModuleHandleW(null);
		List<IDisposable> disposables = new List<IDisposable>();
		ID3D11Texture2D backBuffer = null;
		ID3D11Texture2D colorMs = null;
		ID3D11Texture2D depthTex = null;
		ID3D11RenderTargetView rtv = null;
		ID3D11DepthStencilView dsv = null;
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
			uint style;
			int x;
			int y;
			int w;
			int h;
			if (s.Fullscreen)
			{
				style = 2415919104u;
				x = 0;
				y = 0;
				w = GetSystemMetrics(0);
				h = GetSystemMetrics(1);
			}
			else
			{
				style = 282001408u;
				RECT r = new RECT
				{
					Right = s.Width,
					Bottom = s.Height
				};
				AdjustWindowRect(ref r, style, menu: false);
				w = r.Right - r.Left;
				h = r.Bottom - r.Top;
				x = Math.Max(0, (GetSystemMetrics(0) - w) / 2);
				y = Math.Max(0, (GetSystemMetrics(1) - h) / 2);
			}
			hwnd = CreateWindowExW(0u, cls, "Triarch · GPU burn", style, x, y, w, h, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
			if (hwnd == IntPtr.Zero)
			{
				throw new InvalidOperationException("Não foi possível criar a janela de renderização.");
			}
			IDXGIFactory2 factory = Keep<IDXGIFactory2>(DXGI.CreateDXGIFactory1<IDXGIFactory2>());
			List<IDXGIAdapter1> candidates = new List<IDXGIAdapter1>();
			IDXGIAdapter1 a;
			for (uint i = 0u; factory.EnumAdapters1(i, out a).Success; i++)
			{
				Keep<IDXGIAdapter1>(a);
				if ((a.Description1.Flags & AdapterFlags.Software) == 0)
				{
					candidates.Add(a);
				}
			}
			IDXGIAdapter1 adapter = candidates.FirstOrDefault((IDXGIAdapter1 iDXGIAdapter) => s.AdapterName.Length > 0 && iDXGIAdapter.Description1.Description.Contains(s.AdapterName, StringComparison.OrdinalIgnoreCase)) ?? candidates.OrderByDescending((IDXGIAdapter1 iDXGIAdapter) => iDXGIAdapter.Description1.DedicatedVideoMemory).FirstOrDefault() ?? throw new InvalidOperationException("Nenhuma GPU compatível com Direct3D 11.");
			AdapterUsed = adapter.Description1.Description;
			D3D11.D3D11CreateDevice((IDXGIAdapter?)adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, new FeatureLevel[2]
			{
				FeatureLevel.Level_11_1,
				FeatureLevel.Level_11_0
			}, out ID3D11Device device, out ID3D11DeviceContext ctx).CheckError();
			Keep<ID3D11Device>(device);
			Keep<ID3D11DeviceContext>(ctx);
			int msaa;
			for (msaa = s.Msaa; msaa > 1 && device.CheckMultisampleQualityLevels(Format.B8G8R8A8_UNorm, (uint)msaa) == 0; msaa /= 2)
			{
			}
			bool tearing;
			using (IDXGIFactory5 f5 = factory.QueryInterfaceOrNull<IDXGIFactory5>())
			{
				tearing = f5?.PresentAllowTearing ?? ((RawBool)false);
			}
			SwapChainFlags scFlags = (tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None);
			GetClientRect(hwnd, out var rc);
			int cw = Math.Max(1, rc.Right);
			int ch = Math.Max(1, rc.Bottom);
			IDXGISwapChain1 swap = Keep<IDXGISwapChain1>(factory.CreateSwapChainForHwnd(device, hwnd, new SwapChainDescription1((uint)cw, (uint)ch, Format.B8G8R8A8_UNorm, stereo: false, Usage.RenderTargetOutput, 2u, Scaling.Stretch, SwapEffect.FlipDiscard, AlphaMode.Ignore, scFlags)));
			factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);
			CreateTargets(cw, ch);
			ReadOnlyMemory<byte> vsCode = Compiler.Compile("\r\ncbuffer CB : register(b0)\r\n{\r\n    row_major float4x4 World;\r\n    row_major float4x4 ViewProj;\r\n    float Time; float ShellCount; float FurLength; float Iterations;\r\n};\r\n\r\nstruct VSIn  { float3 pos : POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; uint inst : SV_InstanceID; };\r\nstruct PSIn  { float4 pos : SV_POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; float shell : TEXCOORD1; };\r\n\r\nPSIn VS(VSIn i)\r\n{\r\n    PSIn o;\r\n    float s = i.inst / ShellCount;                       // 0 = raiz, 1 = ponta\r\n    float3 p = i.pos + i.nrm * (FurLength * s);\r\n    float3 w = mul(float4(p, 1), World).xyz;\r\n    w.y -= 0.10 * s * s;                                  // gravidade\r\n    w   += 0.025 * s * sin(Time * 2.7 + i.pos.yzx * 7.0); // vento\r\n    o.pos = mul(float4(w, 1), ViewProj);\r\n    o.nrm = normalize(mul(float4(i.nrm, 0), World).xyz);\r\n    o.uv = i.uv;\r\n    o.shell = s;\r\n    return o;\r\n}\r\n\r\nfloat hash(float2 p)\r\n{\r\n    p = frac(p * float2(123.34, 456.21));\r\n    p += dot(p, p + 45.32);\r\n    return frac(p.x * p.y);\r\n}\r\n\r\nfloat4 PS(PSIn i) : SV_TARGET\r\n{\r\n    float h = hash(floor(i.uv * float2(900, 380)));\r\n\r\n    // Carga de ALU: cadeia de FMAs vetoriais (o que mais consome energia na GPU), antes do descarte\r\n    // e ligada a ele, para o compilador não poder pular o cálculo.\r\n    float4 a = float4(h, i.uv, i.shell), b = a.wzyx + 0.5;\r\n    [loop] for (int k = 0; k < (int)Iterations; k++)\r\n    {\r\n        a = mad(a, 1.0001, b * 0.0003);\r\n        b = mad(b, 0.9997, a * 0.0002);\r\n    }\r\n    float acc = frac(dot(a, b));\r\n\r\n    if (i.shell > 0 && h + acc * 1e-6 < i.shell) discard; // fios afinam até a ponta\r\n\r\n    float3 L = normalize(float3(-0.4, 0.8, -0.5));\r\n    float diff = saturate(dot(i.nrm, L)) * 0.75 + 0.25;\r\n    float ao = lerp(0.18, 1.0, i.shell);                  // raiz escura, ponta clara\r\n    float c = diff * ao * (0.9 + 0.1 * acc);\r\n    return float4(c, c, c, lerp(1.0, 0.55, i.shell));     // pontas translúcidas\r\n}", "VS", "fur.hlsl", "vs_5_0", ShaderFlags.OptimizationLevel3);
			ReadOnlyMemory<byte> psCode = Compiler.Compile("\r\ncbuffer CB : register(b0)\r\n{\r\n    row_major float4x4 World;\r\n    row_major float4x4 ViewProj;\r\n    float Time; float ShellCount; float FurLength; float Iterations;\r\n};\r\n\r\nstruct VSIn  { float3 pos : POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; uint inst : SV_InstanceID; };\r\nstruct PSIn  { float4 pos : SV_POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; float shell : TEXCOORD1; };\r\n\r\nPSIn VS(VSIn i)\r\n{\r\n    PSIn o;\r\n    float s = i.inst / ShellCount;                       // 0 = raiz, 1 = ponta\r\n    float3 p = i.pos + i.nrm * (FurLength * s);\r\n    float3 w = mul(float4(p, 1), World).xyz;\r\n    w.y -= 0.10 * s * s;                                  // gravidade\r\n    w   += 0.025 * s * sin(Time * 2.7 + i.pos.yzx * 7.0); // vento\r\n    o.pos = mul(float4(w, 1), ViewProj);\r\n    o.nrm = normalize(mul(float4(i.nrm, 0), World).xyz);\r\n    o.uv = i.uv;\r\n    o.shell = s;\r\n    return o;\r\n}\r\n\r\nfloat hash(float2 p)\r\n{\r\n    p = frac(p * float2(123.34, 456.21));\r\n    p += dot(p, p + 45.32);\r\n    return frac(p.x * p.y);\r\n}\r\n\r\nfloat4 PS(PSIn i) : SV_TARGET\r\n{\r\n    float h = hash(floor(i.uv * float2(900, 380)));\r\n\r\n    // Carga de ALU: cadeia de FMAs vetoriais (o que mais consome energia na GPU), antes do descarte\r\n    // e ligada a ele, para o compilador não poder pular o cálculo.\r\n    float4 a = float4(h, i.uv, i.shell), b = a.wzyx + 0.5;\r\n    [loop] for (int k = 0; k < (int)Iterations; k++)\r\n    {\r\n        a = mad(a, 1.0001, b * 0.0003);\r\n        b = mad(b, 0.9997, a * 0.0002);\r\n    }\r\n    float acc = frac(dot(a, b));\r\n\r\n    if (i.shell > 0 && h + acc * 1e-6 < i.shell) discard; // fios afinam até a ponta\r\n\r\n    float3 L = normalize(float3(-0.4, 0.8, -0.5));\r\n    float diff = saturate(dot(i.nrm, L)) * 0.75 + 0.25;\r\n    float ao = lerp(0.18, 1.0, i.shell);                  // raiz escura, ponta clara\r\n    float c = diff * ao * (0.9 + 0.1 * acc);\r\n    return float4(c, c, c, lerp(1.0, 0.55, i.shell));     // pontas translúcidas\r\n}", "PS", "fur.hlsl", "ps_5_0", ShaderFlags.OptimizationLevel3);
			ID3D11VertexShader vs = Keep<ID3D11VertexShader>(device.CreateVertexShader(vsCode.Span));
			ID3D11PixelShader ps = Keep<ID3D11PixelShader>(device.CreatePixelShader(psCode.Span));
			ID3D11InputLayout layout = Keep<ID3D11InputLayout>(device.CreateInputLayout(new InputElementDescription[3]
			{
				new InputElementDescription("POSITION", 0u, Format.R32G32B32_Float, 0u, 0u),
				new InputElementDescription("NORMAL", 0u, Format.R32G32B32_Float, 12u, 0u),
				new InputElementDescription("TEXCOORD", 0u, Format.R32G32_Float, 24u, 0u)
			}, vsCode.Span));
			BuildTorus(out Vertex[] vertices, out uint[] indices);
			ID3D11Buffer vb = Keep<ID3D11Buffer>(device.CreateBuffer(vertices, BindFlags.VertexBuffer));
			ID3D11Buffer ib = Keep<ID3D11Buffer>(device.CreateBuffer(indices, BindFlags.IndexBuffer));
			ID3D11Buffer cb = Keep<ID3D11Buffer>(device.CreateBuffer((uint)Marshal.SizeOf<Constants>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
			ID3D11RasterizerState raster = Keep<ID3D11RasterizerState>(device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid)
			{
				MultisampleEnable = (msaa > 1)
			}));
			BlendDescription blendDesc = new BlendDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha);
			ID3D11BlendState blend = Keep<ID3D11BlendState>(device.CreateBlendState(blendDesc));
			ID3D11Query[] fences = new ID3D11Query[6];
			for (int i2 = 0; i2 < 6; i2++)
			{
				fences[i2] = Keep<ID3D11Query>(device.CreateQuery(QueryType.Event));
			}
			long frame = 0L;
			Stopwatch clock = Stopwatch.StartNew();
			double lastT = 0.0;
			double lastPresent = 0.0;
			long lastFrames = 0L;
			while (!_stop)
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
				GetClientRect(hwnd, out rc);
				if (rc.Right <= 0 || rc.Bottom <= 0)
				{
					Thread.Sleep(50);
					continue;
				}
				if (rc.Right != cw || rc.Bottom != ch)
				{
					cw = rc.Right;
					ch = rc.Bottom;
					ctx.OMSetRenderTargets((ID3D11RenderTargetView)null, (ID3D11DepthStencilView?)null);
					ReleaseTargets();
					swap.ResizeBuffers(2u, (uint)cw, (uint)ch, Format.B8G8R8A8_UNorm, scFlags).CheckError();
					CreateTargets(cw, ch);
				}
				ID3D11Query fence = fences[frame % 6];
				if (frame >= 6)
				{
					while (!ctx.IsDataAvailable(fence) && !_stop)
					{
						Thread.Yield();
					}
				}
				float t = (float)clock.Elapsed.TotalSeconds;
				Matrix4x4 world = Matrix4x4.CreateRotationY(t * 0.6f) * Matrix4x4.CreateRotationX(0.55f + 0.25f * MathF.Sin(t * 0.35f));
				Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0f, 0f, -3.5f), Vector3.Zero, Vector3.UnitY);
				Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView((float)Math.PI / 4f, (float)cw / (float)ch, 0.1f, 50f);
				Marshal.StructureToPtr(new Constants
				{
					World = world,
					ViewProj = view * proj,
					Time = t,
					ShellCount = s.Shells,
					FurLength = 0.2f,
					Iterations = s.Iterations
				}, ctx.Map(cb, MapMode.WriteDiscard).DataPointer, fDeleteOld: false);
				ctx.Unmap(cb);
				ctx.OMSetRenderTargets(rtv, dsv);
				ctx.ClearRenderTargetView(rtv, new Color4(0f, 0f, 0f));
				ctx.ClearDepthStencilView(dsv, DepthStencilClearFlags.Depth, 1f, 0);
				ctx.RSSetViewport(0f, 0f, cw, ch);
				ctx.RSSetState(raster);
				ctx.OMSetBlendState(blend);
				ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
				ctx.IASetInputLayout(layout);
				ctx.IASetVertexBuffer(0u, vb, (uint)Marshal.SizeOf<Vertex>());
				ctx.IASetIndexBuffer(ib, Format.R32_UInt, 0u);
				ctx.VSSetShader(vs);
				ctx.PSSetShader(ps);
				ctx.VSSetConstantBuffer(0u, cb);
				ctx.PSSetConstantBuffer(0u, cb);
				ctx.DrawIndexedInstanced((uint)indices.Length, (uint)s.Shells, 0u, 0, 0u);
				ctx.End(fence);
				frame++;
				Interlocked.Increment(ref _frames);
				double now = clock.Elapsed.TotalSeconds;
				if (now - lastPresent >= 1.0 / 60.0)
				{
					if (msaa > 1)
					{
						ctx.ResolveSubresource(backBuffer, 0u, colorMs, 0u, Format.B8G8R8A8_UNorm);
					}
					else
					{
						ctx.CopyResource(backBuffer, colorMs);
					}
					swap.Present(0u, tearing ? PresentFlags.AllowTearing : PresentFlags.None);
					Interlocked.Increment(ref _presents);
					lastPresent = now;
				}
				_seconds = now;
				if (now - lastT >= 0.5)
				{
					long f6 = Frames;
					Fps = (double)(f6 - lastFrames) / (now - lastT);
					lastT = now;
					lastFrames = f6;
					SetWindowTextW(hwnd, $"Triarch · GPU burn · {Fps:0} FPS · {cw}x{ch}" + ((msaa > 1) ? $" · MSAA {msaa}x" : "") + " · " + AdapterUsed);
				}
			}
			void CreateTargets(int tw, int th)
			{
				backBuffer = swap.GetBuffer<ID3D11Texture2D>(0u);
				Texture2DDescription cd = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)tw, (uint)th, 1u, 1u, BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, (uint)msaa);
				colorMs = device.CreateTexture2D(in cd);
				rtv = device.CreateRenderTargetView(colorMs);
				Texture2DDescription dd = new Texture2DDescription(Format.D32_Float, (uint)tw, (uint)th, 1u, 1u, BindFlags.DepthStencil, ResourceUsage.Default, CpuAccessFlags.None, (uint)msaa);
				depthTex = device.CreateTexture2D(in dd);
				dsv = device.CreateDepthStencilView(depthTex);
			}
		}
		catch (Exception ex)
		{
			Error = ex.Message;
		}
		finally
		{
			ReleaseTargets();
			for (int i3 = disposables.Count - 1; i3 >= 0; i3--)
			{
				try
				{
					disposables[i3].Dispose();
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
			Fps = 0.0;
		}
		T Keep<T>(T d) where T : notnull, IDisposable
		{
			disposables.Add(d);
			return d;
		}
		void ReleaseTargets()
		{
			rtv?.Dispose();
			dsv?.Dispose();
			depthTex?.Dispose();
			colorMs?.Dispose();
			backBuffer?.Dispose();
			rtv = null;
			dsv = null;
			depthTex = null;
			colorMs = null;
			backBuffer = null;
		}
	}

	private static void BuildTorus(out Vertex[] vertices, out uint[] indices)
	{
		vertices = new Vertex[10465];
		int i = 0;
		int k = 0;
		for (; i <= 160; i++)
		{
			float u = (float)i / 160f * ((float)Math.PI * 2f);
			int j = 0;
			while (j <= 64)
			{
				float v = (float)j / 64f * ((float)Math.PI * 2f);
				Vector3 n = new Vector3(MathF.Cos(u) * MathF.Cos(v), MathF.Sin(v), MathF.Sin(u) * MathF.Cos(v));
				vertices[k] = new Vertex
				{
					Pos = new Vector3(MathF.Cos(u) * 1f, 0f, MathF.Sin(u) * 1f) + n * 0.42f,
					Normal = n,
					Uv = new Vector2((float)i / 160f, (float)j / 64f)
				};
				j++;
				k++;
			}
		}
		indices = new uint[61440];
		int l = 0;
		int k2 = 0;
		for (; l < 160; l++)
		{
			for (int m = 0; m < 64; m++)
			{
				uint a = (uint)(l * 65 + m);
				uint b = (uint)((l + 1) * 65 + m);
				indices[k2++] = a;
				indices[k2++] = b;
				indices[k2++] = a + 1;
				indices[k2++] = a + 1;
				indices[k2++] = b;
				indices[k2++] = b + 1;
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
			ClosedByUser = true;
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
	private static extern bool GetClientRect(nint hWnd, out RECT r);

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
