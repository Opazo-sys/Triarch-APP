using System.Collections.Generic;
using System.Linq;

namespace Triarch;

public static class OverlayMetrics
{
	public static readonly OverlayMetric[] All = new OverlayMetric[26]
	{
		new OverlayMetric("fps", "FPS", "FPS", Default: true),
		new OverlayMetric("frametime", "FPS", "Frametime (ms)", Default: true),
		new OverlayMetric("low1", "FPS", "1% low", Default: true),
		new OverlayMetric("graph", "FPS", "Gráfico de frametime", Default: true),
		new OverlayMetric("gpu_load", "GPU", "Uso", Default: true),
		new OverlayMetric("gpu_temp", "GPU", "Temperatura", Default: true),
		new OverlayMetric("gpu_hotspot", "GPU", "Hotspot", Default: false),
		new OverlayMetric("gpu_clock", "GPU", "Clock do núcleo", Default: true),
		new OverlayMetric("gpu_memclock", "GPU", "Clock da memória", Default: false),
		new OverlayMetric("gpu_power", "GPU", "Consumo", Default: true),
		new OverlayMetric("gpu_volt", "GPU", "Tensão do núcleo", Default: false),
		new OverlayMetric("gpu_fan", "GPU", "Ventoinha", Default: false),
		new OverlayMetric("gpu_perfcap", "GPU", "Limitador (NVIDIA)", Default: false),
		new OverlayMetric("vram_used", "VRAM", "Usada (GB)", Default: true),
		new OverlayMetric("vram_pct", "VRAM", "Uso (%)", Default: false),
		new OverlayMetric("vram_temp", "VRAM", "Temperatura", Default: false),
		new OverlayMetric("cpu_load", "CPU", "Uso total", Default: true),
		new OverlayMetric("cpu_maxcore", "CPU", "Núcleo mais ocupado", Default: false),
		new OverlayMetric("cpu_temp", "CPU", "Temperatura", Default: true),
		new OverlayMetric("cpu_clock", "CPU", "Clock médio", Default: true),
		new OverlayMetric("cpu_clockmax", "CPU", "Clock máximo (núcleo)", Default: false),
		new OverlayMetric("cpu_power", "CPU", "Consumo", Default: true),
		new OverlayMetric("ram_used", "RAM", "Usada (GB)", Default: true),
		new OverlayMetric("ram_pct", "RAM", "Uso (%)", Default: false),
		new OverlayMetric("game", "Outros", "Nome do jogo", Default: true),
		new OverlayMetric("clock", "Outros", "Hora", Default: false)
	};

	public static HashSet<string> Defaults()
	{
		return (from m in All
			where m.Default
			select m.Key).ToHashSet();
	}
}
