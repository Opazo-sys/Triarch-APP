using System.Globalization;

namespace Triarch;

public sealed class GpuDriverStatus
{
	public string Vendor { get; set; } = "";

	public string GpuName { get; set; } = "";

	public string Installed { get; set; } = "";

	public string Latest { get; set; } = "";

	public string DownloadUrl { get; set; } = "";

	public string SizeText { get; set; } = "";

	public string OfficialPage { get; set; } = "";

	public string Message { get; set; } = "";

	public bool CanAutoInstall
	{
		get
		{
			string vendor = Vendor;
			if ((vendor == "NVIDIA" || vendor == "AMD") ? true : false)
			{
				return DownloadUrl.Length > 0;
			}
			return false;
		}
	}

	public bool UpdateAvailable
	{
		get
		{
			if (Vendor == "NVIDIA" && Latest.Length > 0 && Installed.Length > 0)
			{
				return CompareVersions(Latest, Installed) > 0;
			}
			return false;
		}
	}

	public static int CompareVersions(string a, string b)
	{
		return V(a).CompareTo(V(b));
		static double V(string s)
		{
			if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
			{
				return 0.0;
			}
			return d;
		}
	}
}
