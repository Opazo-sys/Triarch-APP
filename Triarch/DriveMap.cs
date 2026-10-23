using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace Triarch;

public static class DriveMap
{
	public static List<DriveCard> Read()
	{
		Dictionary<string, (string, string)> disks = PhysicalDiskByLetter();
		List<DriveCard> list = new List<DriveCard>();
		DriveInfo[] drives = DriveInfo.GetDrives();
		foreach (DriveInfo d in drives)
		{
			try
			{
				if (d.IsReady)
				{
					string letter = d.Name.TrimEnd('\\');
					disks.TryGetValue(letter, out var disk);
					bool usb = disk.Item2?.Equals("USB", StringComparison.OrdinalIgnoreCase) ?? false;
					string kind = d.DriveType switch
					{
						DriveType.Removable => "Removível", 
						DriveType.Fixed => (!usb) ? "Interno" : "Externo (USB)", 
						DriveType.Network => "Rede", 
						DriveType.CDRom => "Óptico", 
						_ => d.DriveType.ToString(), 
					};
					list.Add(new DriveCard
					{
						Letter = letter,
						Label = d.VolumeLabel,
						Kind = kind,
						FileSystem = d.DriveFormat,
						Disk = (disk.Item1 ?? ""),
						Total = d.TotalSize,
						Free = d.TotalFreeSpace,
						Removable = ((d.DriveType == DriveType.Removable) | usb)
					});
				}
			}
			catch
			{
			}
		}
		return list.OrderBy((DriveCard c) => c.Letter).ToList();
	}

	private static Dictionary<string, (string? Model, string? Bus)> PhysicalDiskByLetter()
	{
		Dictionary<string, (string, string)> map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
		try
		{
			Dictionary<string, (string, string)> disks = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
			using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT DeviceID, Model, InterfaceType, PNPDeviceID FROM Win32_DiskDrive"))
			{
				foreach (ManagementObject d in s.Get())
				{
					string bus = d["InterfaceType"]?.ToString();
					object obj = d["PNPDeviceID"];
					if (obj != null && obj.ToString()?.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase) == true)
					{
						bus = "USB";
					}
					disks[d["DeviceID"].ToString()] = (d["Model"]?.ToString()?.Trim(), bus);
				}
			}
			Dictionary<string, string> diskOfPartition = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			using (ManagementObjectSearcher s2 = new ManagementObjectSearcher("SELECT Antecedent, Dependent FROM Win32_DiskDriveToDiskPartition"))
			{
				foreach (ManagementObject a in s2.Get())
				{
					string disk = RefId(a["Antecedent"]);
					if (disk != null)
					{
						string part = RefId(a["Dependent"]);
						if (part != null)
						{
							diskOfPartition[part] = disk;
						}
					}
				}
			}
			using ManagementObjectSearcher s3 = new ManagementObjectSearcher("SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition");
			foreach (ManagementObject a2 in s3.Get())
			{
				string part2 = RefId(a2["Antecedent"]);
				if (part2 != null)
				{
					string letter = RefId(a2["Dependent"]);
					if (letter != null && diskOfPartition.TryGetValue(part2, out var disk2) && disks.TryGetValue(disk2, out var info))
					{
						map[letter] = info;
					}
				}
			}
		}
		catch
		{
		}
		return map;
	}

	private static string? RefId(object? reference)
	{
		Match m = Regex.Match(reference?.ToString() ?? "", "DeviceID=\"(.*)\"");
		if (!m.Success)
		{
			return null;
		}
		return m.Groups[1].Value.Replace("\\\\", "\\");
	}
}
