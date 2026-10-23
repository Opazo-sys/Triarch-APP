using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace Triarch;

public static class BottleneckAnalyzer
{
	public static LoadSample Sample(SensorMonitor m, GpuInfo? gpu, FrameStats? frames)
	{
		ObservableCollection<SensorRow> rows = m.Rows;
		float? gpuLoad = null;
		float? gpuTemp = null;
		double? vramPct = null;
		bool thermal = false;
		if (gpu != null)
		{
			gpuLoad = rows.FirstOrDefault((SensorRow sensorRow) => sensorRow.Hardware == gpu.Name && sensorRow.Type == SensorType.Load && sensorRow.Name == "GPU Core")?.RawValue;
			List<SensorRow> temps = rows.Where((SensorRow sensorRow) => sensorRow.Hardware == gpu.Name && sensorRow.Type == SensorType.Temperature).ToList();
			gpuTemp = (temps.FirstOrDefault((SensorRow sensorRow) => sensorRow.Name == "GPU Core") ?? temps.FirstOrDefault())?.RawValue;
			if (gpu.NvmlHandle != IntPtr.Zero)
			{
				Nvml.Memory? memory = Nvml.MemoryInfo(gpu.NvmlHandle);
				if (memory.HasValue)
				{
					Nvml.Memory mem = memory.GetValueOrDefault();
					if (mem.Total != 0)
					{
						vramPct = 100.0 * (double)mem.Used / (double)mem.Total;
						goto IL_01c9;
					}
				}
			}
			float? used = rows.FirstOrDefault((SensorRow sensorRow) => sensorRow.Hardware == gpu.Name && sensorRow.Name == "GPU Memory Used")?.RawValue;
			float? total = rows.FirstOrDefault((SensorRow sensorRow) => sensorRow.Hardware == gpu.Name && sensorRow.Name == "GPU Memory Total")?.RawValue;
			if (used.HasValue && total > 0f)
			{
				vramPct = 100.0 * (double)used.Value / (double)total.Value;
			}
			goto IL_01c9;
		}
		goto IL_020b;
		IL_020b:
		SensorRow busiest = (from sensorRow in rows.Where((SensorRow sensorRow) => sensorRow.HardwareType == HardwareType.Cpu && sensorRow.Type == SensorType.Load && sensorRow.Name.StartsWith("CPU Core #") && !sensorRow.Name.Contains("Thread") && sensorRow.RawValue.HasValue).ToList()
			orderby sensorRow.RawValue descending
			select sensorRow).FirstOrDefault();
		float? ram = rows.FirstOrDefault((SensorRow sensorRow) => sensorRow.HardwareType == HardwareType.Memory && sensorRow.Type == SensorType.Load && sensorRow.Name == "Memory")?.RawValue;
		return new LoadSample(gpuLoad, m.CpuLoad, busiest?.RawValue, busiest?.Name.Replace("CPU Core #", "núcleo "), vramPct, ram, gpuTemp, m.CpuTemperature, thermal, frames?.Fps);
		IL_01c9:
		if (gpu.NvmlHandle != IntPtr.Zero)
		{
			ulong? num = Nvml.ThrottleReasons(gpu.NvmlHandle);
			if (num.HasValue)
			{
				ulong r = num.GetValueOrDefault();
				thermal = (r & 0x60) != 0;
			}
		}
		goto IL_020b;
	}

	public static string Title(Limiter l)
	{
		return l switch
		{
			Limiter.Gpu => "GPU no limite", 
			Limiter.Cpu => "Gargalo de CPU", 
			Limiter.Vram => "Memória de vídeo cheia", 
			Limiter.Ram => "Memória RAM cheia", 
			Limiter.Thermal => "Limitado por temperatura", 
			_ => "Nada no limite", 
		};
	}

	public static string Advice(Limiter l)
	{
		return l switch
		{
			Limiter.Gpu => "Normal em jogos pesados: a placa de vídeo está sendo bem aproveitada. Para mais FPS, reduza resolução ou qualidade gráfica, ou ative DLSS/FSR.", 
			Limiter.Cpu => "A CPU (ou um núcleo dela) não acompanha a GPU. Subir resolução/gráficos quase não custa FPS. Para ganhar FPS: reduza opções que pesam na CPU (distância de visão, multidões, física), feche programas em segundo plano e confira se o XMP/EXPO da RAM está ativo na BIOS.", 
			Limiter.Vram => "A memória da placa de vídeo encheu, o que causa travadas. Reduza a qualidade das texturas ou a resolução.", 
			Limiter.Ram => "Pouca RAM livre: o Windows passa a usar o disco e o jogo engasga. Feche navegadores e outros programas.", 
			Limiter.Thermal => "Temperatura alta está derrubando o clock. Verifique ventilação do gabinete, poeira e pasta térmica.", 
			_ => "Nem CPU nem GPU estão no limite: provavelmente há trava de FPS, V-Sync/G-Sync limitando, ou o jogo está em menu/pausa. Normal se o FPS já é o desejado.", 
		};
	}

	public static (double Ratio, string Text) Balance(double gpuIdx, double cpuIdx)
	{
		double ratio = gpuIdx / cpuIdx;
		string text;
		if (ratio > 1.8)
		{
			text = "GPU muito mais forte que a CPU: em boa parte dos jogos a CPU vai limitar o FPS, principalmente em 1080p.";
		}
		else if (ratio > 1.3)
		{
			text = "GPU um pouco à frente da CPU: jogos competitivos em 1080p tendem a ficar limitados pela CPU; em 1440p/4K a GPU volta a ser o limite.";
		}
		else if (ratio >= 0.7)
		{
			text = "Hardware equilibrado: na maioria dos jogos a GPU será o limite, que é o cenário ideal.";
		}
		else
		{
			text = ((!(ratio > 0.4)) ? "GPU muito fraca para esta CPU: a placa de vídeo limita quase tudo." : "CPU mais forte que a GPU: a placa de vídeo é o limite. Upgrade de GPU traria o maior ganho.");
		}
		string text2 = text;
		return (Ratio: ratio, Text: text2);
	}
}
