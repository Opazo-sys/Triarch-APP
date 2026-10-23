using System.ComponentModel;
using LibreHardwareMonitor.Hardware;

namespace Triarch;

public class SensorRow : INotifyPropertyChanged
{
	private float? _value;

	private float? _min;

	private float? _max;

	public string Id { get; init; } = "";

	public string Hardware { get; init; } = "";

	public string Category { get; init; } = "";

	public string Name { get; init; } = "";

	public SensorType Type { get; init; }

	public HardwareType HardwareType { get; init; }

	public float? RawValue => _value;

	public int Order => Type switch
	{
		SensorType.Clock => 0, 
		SensorType.Temperature => 1, 
		SensorType.Load => 2, 
		SensorType.Fan => 3, 
		SensorType.Control => 4, 
		SensorType.Power => 5, 
		SensorType.Voltage => 6, 
		SensorType.Current => 7, 
		SensorType.SmallData => 8, 
		SensorType.Data => 9, 
		SensorType.Throughput => 10, 
		_ => 11, 
	};

	public string Value => Format(_value);

	public string Min => Format(_min);

	public string Max => Format(_max);

	public event PropertyChangedEventHandler? PropertyChanged;

	public void Update(float? value, float? min, float? max)
	{
		_value = value;
		_min = min;
		_max = max;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Value"));
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Min"));
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Max"));
	}

	private string Format(float? v)
	{
		if (!v.HasValue)
		{
			return "—";
		}
		switch (Type)
		{
		case SensorType.Temperature:
			return $"{v:0.#} °C";
		case SensorType.Load:
		case SensorType.Control:
		case SensorType.Level:
		case SensorType.Humidity:
			return $"{v:0.#} %";
		case SensorType.Clock:
			return $"{v:0} MHz";
		case SensorType.Power:
			return $"{v:0.#} W";
		case SensorType.Voltage:
			return $"{v:0.###} V";
		case SensorType.Current:
			return $"{v:0.###} A";
		case SensorType.Fan:
			return $"{v:0} RPM";
		case SensorType.Flow:
			return $"{v:0.#} L/h";
		case SensorType.Frequency:
			return $"{v:0.#} Hz";
		case SensorType.Data:
			return $"{v:0.##} GB";
		case SensorType.SmallData:
			return $"{v:0.#} MB";
		case SensorType.Throughput:
			return $"{v / 1048576f:0.##} MB/s";
		case SensorType.Energy:
			return $"{v:0.#} mWh";
		default:
			return $"{v:0.##}";
		}
	}

	public static string HardwareGroupPrefix(HardwareType t)
	{
		return t switch
		{
			HardwareType.Cpu => "CPU", 
			HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "GPU", 
			HardwareType.Motherboard or HardwareType.SuperIO => "Placa-mãe", 
			HardwareType.Memory => "RAM", 
			HardwareType.Storage => "Disco", 
			HardwareType.Network => "Rede", 
			_ => "Hardware", 
		};
	}

	public static string CategoryName(SensorType t)
	{
		switch (t)
		{
		case SensorType.Temperature:
			return "Temperatura";
		case SensorType.Load:
			return "Carga";
		case SensorType.Clock:
			return "Clock";
		case SensorType.Power:
			return "Consumo";
		case SensorType.Voltage:
			return "Tensão";
		case SensorType.Current:
			return "Corrente";
		case SensorType.Fan:
			return "Ventoinhas";
		case SensorType.Control:
			return "Controle";
		case SensorType.Level:
			return "Nível";
		case SensorType.Data:
		case SensorType.SmallData:
			return "Dados";
		case SensorType.Throughput:
			return "Taxa";
		case SensorType.Frequency:
			return "Frequência";
		case SensorType.Energy:
			return "Energia";
		case SensorType.Flow:
			return "Fluxo";
		case SensorType.Humidity:
			return "Umidade";
		case SensorType.Factor:
			return "Fator";
		default:
			return t.ToString();
		}
	}
}
