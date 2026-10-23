using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Triarch;

internal static class Dxgi
{
	public record Adapter(string Description, uint VendorId, uint DeviceId, ulong DedicatedVideo, ulong DedicatedSystem, ulong SharedSystem, string? FeatureLevel);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct AdapterDesc1
	{
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string Description;

		public uint VendorId;

		public uint DeviceId;

		public uint SubSysId;

		public uint Revision;

		public nuint DedicatedVideoMemory;

		public nuint DedicatedSystemMemory;

		public nuint SharedSystemMemory;

		public uint LuidLow;

		public int LuidHigh;

		public uint Flags;
	}

	[ComImport]
	[Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IDXGIFactory1
	{
		void SetPrivateData();

		void SetPrivateDataInterface();

		void GetPrivateData();

		void GetParent();

		void EnumAdapters();

		void MakeWindowAssociation();

		void GetWindowAssociation();

		void CreateSwapChain();

		void CreateSoftwareAdapter();

		[PreserveSig]
		int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
	}

	[ComImport]
	[Guid("29038f61-3839-4626-91fd-086879011a05")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IDXGIAdapter1
	{
		void SetPrivateData();

		void SetPrivateDataInterface();

		void GetPrivateData();

		void GetParent();

		void EnumOutputs();

		void GetDesc();

		void CheckInterfaceSupport();

		[PreserveSig]
		int GetDesc1(out AdapterDesc1 desc);
	}

	public static List<Adapter> Enumerate()
	{
		List<Adapter> list = new List<Adapter>();
		try
		{
			Guid iid = typeof(IDXGIFactory1).GUID;
			if (CreateDXGIFactory1(ref iid, out IDXGIFactory1 factory) < 0)
			{
				return list;
			}
			IDXGIAdapter1 a;
			for (uint i = 0u; factory.EnumAdapters1(i, out a) == 0; i++)
			{
				if (a.GetDesc1(out var d) == 0 && (d.Flags & 2) == 0)
				{
					list.Add(new Adapter(d.Description, d.VendorId, d.DeviceId, d.DedicatedVideoMemory, d.DedicatedSystemMemory, d.SharedSystemMemory, FeatureLevel(a)));
				}
				Marshal.ReleaseComObject(a);
			}
			Marshal.ReleaseComObject(factory);
		}
		catch
		{
		}
		return list;
	}

	private static string? FeatureLevel(IDXGIAdapter1 adapter)
	{
		Guid iid = new Guid("189819f1-1db6-4b57-be54-1821339b85f7");
		(int, string)[] levels = new (int, string)[5]
		{
			(49664, "12_2"),
			(49408, "12_1"),
			(49152, "12_0"),
			(45312, "11_1"),
			(45056, "11_0")
		};
		try
		{
			(int, string)[] array = levels;
			for (int i = 0; i < array.Length; i++)
			{
				var (level, name) = array[i];
				if (D3D12CreateDevice(adapter, level, ref iid, IntPtr.Zero) >= 0)
				{
					return name;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	[DllImport("dxgi.dll")]
	private static extern int CreateDXGIFactory1(ref Guid riid, out IDXGIFactory1 factory);

	[DllImport("d3d12.dll")]
	private static extern int D3D12CreateDevice([MarshalAs(UnmanagedType.IUnknown)] object adapter, int minimumFeatureLevel, ref Guid riid, nint device);
}
