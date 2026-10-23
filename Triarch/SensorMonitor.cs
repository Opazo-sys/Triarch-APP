using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace Triarch;

public sealed class SensorMonitor : IDisposable
{
	public enum CpuSensorProblem
	{
		None,
		NotElevated,
		PawnIOMissing,
		ReadFailed
	}

	private readonly Computer _computer;

	private readonly Dictionary<string, SensorRow> _index = new Dictionary<string, SensorRow>();

	private volatile bool _open;

	public ObservableCollection<SensorRow> Rows { get; } = new ObservableCollection<SensorRow>();

	public bool IsOpen => _open;

	public float? CpuTemperature
	{
		get
		{
			List<SensorRow> temps = Rows.Where((SensorRow r) =>
			{
				if (r.HardwareType == HardwareType.Cpu && r.Type == SensorType.Temperature)
				{
					float? rawValue = r.RawValue;
					if (rawValue.HasValue)
					{
						return rawValue.GetValueOrDefault() > 0f;
					}
					return false;
				}
				return false;
			}).ToList();
			return temps.FirstOrDefault((SensorRow r) => r.Name.Contains("Package") || r.Name.Contains("Tctl") || r.Name.Contains("Tdie"))?.RawValue ?? temps.Max((SensorRow r) => r.RawValue);
		}
	}

	public float? CpuLoad => Rows.FirstOrDefault((SensorRow r) => r.HardwareType == HardwareType.Cpu && r.Type == SensorType.Load && r.Name == "CPU Total")?.RawValue;

	public float? CpuPower => Rows.FirstOrDefault((SensorRow r) => r.HardwareType == HardwareType.Cpu && r.Type == SensorType.Power && r.Name.Contains("Package"))?.RawValue;

	public static bool IsPawnIOInstalled
	{
		get
		{
			using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
			using RegistryKey key = hklm.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\PawnIO");
			return key != null;
		}
	}

	public static bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

	public SensorMonitor()
	{
		_computer = new Computer
		{
			IsCpuEnabled = true,
			IsGpuEnabled = true,
			IsMemoryEnabled = true,
			IsMotherboardEnabled = true,
			IsStorageEnabled = true,
			IsNetworkEnabled = true,
			IsControllerEnabled = true
		};
	}

	public Task OpenAsync()
	{
		return Task.Run(() =>
		{
			_computer.Open();
			_open = true;
		});
	}

	public void Refresh()
	{
		if (!_open)
		{
			return;
		}
		foreach (IHardware hw in _computer.Hardware)
		{
			Walk(hw, null, null);
		}
	}

	public void ResetMinMax()
	{
		if (!_open)
		{
			return;
		}
		foreach (IHardware hw in _computer.Hardware)
		{
			ResetHw(hw);
		}
		Refresh();
		static void ResetHw(IHardware h)
		{
			foreach (ISensor s in h.Sensors)
			{
				s.ResetMin();
				s.ResetMax();
			}
			foreach (IHardware sub in h.SubHardware)
			{
				ResetHw(sub);
			}
		}
	}

	private void Walk(IHardware hw, string? parentName, HardwareType? parentType)
	{
		hw.Update();
		string hwName = ((hw.HardwareType == HardwareType.SuperIO && !string.IsNullOrEmpty(parentName)) ? parentName : hw.Name);
		HardwareType hwType = ((hw.HardwareType == HardwareType.SuperIO && parentType.HasValue) ? parentType.Value : hw.HardwareType);
		ISensor[] sensors = hw.Sensors;
		foreach (ISensor s in sensors)
		{
			string id = s.Identifier.ToString();
			if (!_index.TryGetValue(id, out SensorRow? row))
			{
				row = new SensorRow
				{
					Id = id,
					Hardware = hwName,
					Category = SensorRow.CategoryName(s.SensorType),
					Name = s.Name,
					Type = s.SensorType,
					HardwareType = hwType
				};
				_index[id] = row;
				Rows.Add(row);
			}
			row.Update(s.Value, s.Min, s.Max);
		}
		IHardware[] subHardware = hw.SubHardware;
		foreach (IHardware sub in subHardware)
		{
			Walk(sub, hwName, hwType);
		}
	}

	public CpuSensorProblem DiagnoseCpuSensors()
	{
		if (CpuTemperature.HasValue)
		{
			return CpuSensorProblem.None;
		}
		if (!IsElevated)
		{
			return CpuSensorProblem.NotElevated;
		}
		if (!IsPawnIOInstalled)
		{
			return CpuSensorProblem.PawnIOMissing;
		}
		return CpuSensorProblem.ReadFailed;
	}

	public static string Explain(CpuSensorProblem p)
	{
		return p switch
		{
			CpuSensorProblem.NotElevated => "O Triarch não está rodando como administrador, e o Windows só libera os sensores internos da CPU nesse modo. Feche e abra de novo aceitando o pedido de administrador.", 
			CpuSensorProblem.PawnIOMissing => "Falta o driver PawnIO, que lê a temperatura e o consumo da CPU. Ele é gratuito, assinado e usado também pelo LibreHardwareMonitor e pelo FanControl. Depois de instalar, feche e abra o Triarch.", 
			CpuSensorProblem.ReadFailed => "O PawnIO está instalado, mas a leitura falhou. Se acabou de instalar, reinicie o PC. Se continuar, confira se o antivírus não bloqueou o driver ou se a CPU é muito antiga/recente para a biblioteca de sensores.", 
			_ => "", 
		};
	}

	public void Dispose()
	{
		if (_open)
		{
			_computer.Close();
		}
	}
}
