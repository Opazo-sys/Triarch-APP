namespace Triarch;

public sealed record DduGpuDetection(string Name, DduGpuVendor Vendor)
{
	public string VendorLabel => Vendor switch
	{
		DduGpuVendor.Amd => "AMD", 
		DduGpuVendor.Nvidia => "NVIDIA", 
		DduGpuVendor.Intel => "Intel", 
		_ => "Desconhecido", 
	};
}
