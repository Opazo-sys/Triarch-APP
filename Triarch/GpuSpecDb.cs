using System;
using System.Collections.Generic;
using System.Linq;

namespace Triarch;

public static class GpuSpecDb
{
	private static readonly Dictionary<string, GpuSpec[]> Db = new Dictionary<string, GpuSpec[]>(StringComparer.OrdinalIgnoreCase)
	{
		["1002:73A5"] = new GpuSpec[1]
		{
			new GpuSpec("Navi 21 KXTX", "RDNA 2.0", "7 nm (TSMC)", 520, 26.8, 5120, 128, 320, "GDDR6", 1890, 2310, 335, "Maio de 2022", "RX 6950 XT", 256, 18.0)
		},
		["1002:73AF"] = new GpuSpec[1]
		{
			new GpuSpec("Navi 21 XTXH", "RDNA 2.0", "7 nm (TSMC)", 520, 26.8, 5120, 128, 320, "GDDR6", 1825, 2250, 300, "Dezembro de 2020", "RX 6900 XT", 256, 16.0)
		},
		["1002:73BF"] = new GpuSpec[3]
		{
			new GpuSpec("Navi 21 XTX", "RDNA 2.0", "7 nm (TSMC)", 520, 26.8, 5120, 128, 320, "GDDR6", 1825, 2250, 300, "Dezembro de 2020", "RX 6900 XT", 256, 16.0),
			new GpuSpec("Navi 21 XT", "RDNA 2.0", "7 nm (TSMC)", 520, 26.8, 4608, 128, 288, "GDDR6", 1825, 2250, 300, "Novembro de 2020", "RX 6800 XT", 256, 16.0),
			new GpuSpec("Navi 21 XL", "RDNA 2.0", "7 nm (TSMC)", 520, 26.8, 3840, 96, 240, "GDDR6", 1700, 2105, 250, "Novembro de 2020", "RX 6800", 256, 16.0)
		},
		["1002:73DF"] = new GpuSpec[4]
		{
			new GpuSpec("Navi 22 KXT", "RDNA 2.0", "7 nm (TSMC)", 335, 17.2, 2560, 64, 160, "GDDR6", 2150, 2600, 250, "Maio de 2022", "RX 6750 XT", 192, 18.0),
			new GpuSpec("Navi 22 XT", "RDNA 2.0", "7 nm (TSMC)", 335, 17.2, 2560, 64, 160, "GDDR6", 2321, 2581, 230, "Março de 2021", "RX 6700 XT", 192, 16.0),
			new GpuSpec("Navi 22 XL", "RDNA 2.0", "7 nm (TSMC)", 335, 17.2, 2304, 64, 144, "GDDR6", 1941, 2450, 175, "Junho de 2021", "RX 6700", 160, 16.0),
			new GpuSpec("Navi 22 XT", "RDNA 2.0", "7 nm (TSMC)", 335, 17.2, 2560, 64, 160, "GDDR6", 2321, 2581, 230, "Março de 2021", "6700 XT", 192, 16.0)
		},
		["1002:73EF"] = new GpuSpec[2]
		{
			new GpuSpec("Navi 23 KXT", "RDNA 2.0", "7 nm (TSMC)", 237, 11.1, 2048, 64, 128, "GDDR6", 2055, 2635, 180, "Maio de 2022", "RX 6650 XT", 128, 17.5),
			new GpuSpec("Navi 23 KXT", "RDNA 2.0", "7 nm (TSMC)", 237, 11.1, 2048, 64, 128, "GDDR6", 2055, 2635, 180, "Maio de 2022", "RX 6650M XT", 128, 17.5)
		},
		["1002:73FF"] = new GpuSpec[3]
		{
			new GpuSpec("Navi 23 XT", "RDNA 2.0", "7 nm (TSMC)", 237, 11.1, 2048, 64, 128, "GDDR6", 1968, 2589, 160, "Agosto de 2021", "RX 6600 XT", 128, 16.0),
			new GpuSpec("Navi 23 XM", "RDNA 2.0", "7 nm (TSMC)", 237, 11.1, 1792, 64, 112, "GDDR6", 2068, 2416, 100, "Junho de 2021", "RX 6600M", 128, 14.0),
			new GpuSpec("Navi 23 XL", "RDNA 2.0", "7 nm (TSMC)", 237, 11.1, 1792, 64, 112, "GDDR6", 1626, 2491, 132, "Outubro de 2021", "RX 6600", 128, 14.0)
		},
		["1002:743F"] = new GpuSpec[2]
		{
			new GpuSpec("Navi 24 XT", "RDNA 2.0", "6 nm (TSMC)", 107, 5.4, 1024, 32, 64, "GDDR6", 2310, 2815, 107, "Janeiro de 2022", "RX 6500 XT", 64, 18.0),
			new GpuSpec("Navi 24 XL", "RDNA 2.0", "6 nm (TSMC)", 107, 5.4, 768, 32, 48, "GDDR6", 1923, 2321, 53, "Abril de 2022", "RX 6400", 64, 16.0)
		},
		["1002:164E"] = new GpuSpec[1]
		{
			new GpuSpec("Raphael (iGPU)", "RDNA 2.0", "6 nm (TSMC)", 122, 3.4, 128, 4, 8, "Memória do sistema (DDR5)", 400, 2200, 0, "Setembro de 2022", "Radeon Graphics")
		},
		["1002:744C"] = new GpuSpec[3]
		{
			new GpuSpec("Navi 31 XTX", "RDNA 3.0", "TSMC 5 nm + 6 nm", 529, 57.7, 6144, 192, 384, "GDDR6", 1855, 2499, 355, "Dezembro de 2022", "RX 7900 XTX", 384, 20.0),
			new GpuSpec("Navi 31 XT", "RDNA 3.0", "TSMC 5 nm + 6 nm", 529, 57.7, 5376, 192, 336, "GDDR6", 1500, 2394, 315, "Dezembro de 2022", "RX 7900 XT", 320, 20.0),
			new GpuSpec("Navi 31 XL", "RDNA 3.0", "TSMC 5 nm + 6 nm", 529, 57.7, 5120, 160, 320, "GDDR6", 1270, 2245, 260, "Julho de 2023", "RX 7900 GRE", 256, 18.0)
		},
		["1002:7448"] = new GpuSpec[1]
		{
			new GpuSpec("Navi 31 XL", "RDNA 3.0", "TSMC 5 nm + 6 nm", 529, 57.7, 5120, 160, 320, "GDDR6", 1270, 2245, 260, "Julho de 2023", "RX 7900 GRE", 256, 18.0)
		},
		["1002:747E"] = new GpuSpec[2]
		{
			new GpuSpec("Navi 32 XT", "RDNA 3.0", "TSMC 5 nm + 6 nm", 346, 28.1, 3840, 96, 240, "GDDR6", 1295, 2430, 263, "Setembro de 2023", "RX 7800 XT", 256, 19.5),
			new GpuSpec("Navi 32 XL", "RDNA 3.0", "TSMC 5 nm + 6 nm", 346, 28.1, 3456, 96, 216, "GDDR6", 1435, 2544, 245, "Setembro de 2023", "RX 7700 XT", 192, 18.0)
		},
		["1002:7480"] = new GpuSpec[2]
		{
			new GpuSpec("Navi 33 XT", "RDNA 3.0", "6 nm (TSMC)", 204, 13.3, 2048, 64, 128, "GDDR6", 1720, 2755, 190, "Janeiro de 2024", "RX 7600 XT", 128, 18.0),
			new GpuSpec("Navi 33 XL", "RDNA 3.0", "6 nm (TSMC)", 204, 13.3, 2048, 64, 128, "GDDR6", 1720, 2655, 165, "Maio de 2023", "RX 7600", 128, 18.0)
		},
		["1002:15BF"] = new GpuSpec[2]
		{
			new GpuSpec("Phoenix (iGPU)", "RDNA 3.0", "4 nm (TSMC)", 178, 25.0, 768, 32, 48, "Memória do sistema (DDR5)", 800, 2700, 0, "Janeiro de 2023", "780M"),
			new GpuSpec("Phoenix (iGPU)", "RDNA 3.0", "4 nm (TSMC)", 178, 25.0, 512, 16, 32, "Memória do sistema (DDR5)", 800, 2600, 0, "Janeiro de 2023", "760M")
		},
		["1002:7550"] = new GpuSpec[2]
		{
			new GpuSpec("Navi 48 XTX", "RDNA 4", "TSMC N4P (4 nm)", 357, 53.9, 4096, 128, 256, "GDDR6", 2400, 2970, 304, "Março de 2025", "RX 9070 XT", 256, 20.0),
			new GpuSpec("Navi 48 XT", "RDNA 4", "TSMC N4P (4 nm)", 357, 53.9, 3584, 128, 224, "GDDR6", 2070, 2520, 220, "Março de 2025", "RX 9070", 256, 20.0)
		},
		["10DE:2203"] = new GpuSpec[1]
		{
			new GpuSpec("GA102-350", "Ampere", "8 nm (Samsung)", 628, 28.3, 10752, 112, 336, "GDDR6X", 1560, 1860, 450, "Março de 2022", "RTX 3090 Ti", 384, 21.0)
		},
		["10DE:2204"] = new GpuSpec[1]
		{
			new GpuSpec("GA102-300", "Ampere", "8 nm (Samsung)", 628, 28.3, 10496, 112, 328, "GDDR6X", 1395, 1695, 350, "Setembro de 2020", "RTX 3090", 384, 19.5)
		},
		["10DE:2208"] = new GpuSpec[1]
		{
			new GpuSpec("GA102-225", "Ampere", "8 nm (Samsung)", 628, 28.3, 10240, 112, 320, "GDDR6X", 1365, 1665, 350, "Junho de 2021", "RTX 3080 Ti", 384, 19.0)
		},
		["10DE:220A"] = new GpuSpec[1]
		{
			new GpuSpec("GA102-220", "Ampere", "8 nm (Samsung)", 628, 28.3, 8960, 96, 280, "GDDR6X", 1260, 1710, 350, "Janeiro de 2022", "RTX 3080", 384, 19.0)
		},
		["10DE:2206"] = new GpuSpec[1]
		{
			new GpuSpec("GA102-200", "Ampere", "8 nm (Samsung)", 628, 28.3, 8704, 96, 272, "GDDR6X", 1440, 1710, 320, "Setembro de 2020", "RTX 3080", 320, 19.0)
		},
		["10DE:2216"] = new GpuSpec[1]
		{
			new GpuSpec("GA102-202", "Ampere", "8 nm (Samsung)", 628, 28.3, 8704, 96, 272, "GDDR6X", 1440, 1710, 320, "Maio de 2021", "RTX 3080", 320, 19.0)
		},
		["10DE:2482"] = new GpuSpec[1]
		{
			new GpuSpec("GA104-400", "Ampere", "8 nm (Samsung)", 392, 17.4, 6144, 96, 192, "GDDR6X", 1575, 1770, 290, "Junho de 2021", "RTX 3070 Ti", 256, 19.0)
		},
		["10DE:2484"] = new GpuSpec[1]
		{
			new GpuSpec("GA104-300", "Ampere", "8 nm (Samsung)", 392, 17.4, 5888, 96, 184, "GDDR6", 1500, 1725, 220, "Outubro de 2020", "RTX 3070", 256, 14.0)
		},
		["10DE:2488"] = new GpuSpec[1]
		{
			new GpuSpec("GA104-302", "Ampere", "8 nm (Samsung)", 392, 17.4, 5888, 96, 184, "GDDR6", 1500, 1725, 220, "Maio de 2021", "RTX 3070", 256, 14.0)
		},
		["10DE:2486"] = new GpuSpec[1]
		{
			new GpuSpec("GA104-200", "Ampere", "8 nm (Samsung)", 392, 17.4, 4864, 80, 152, "GDDR6", 1410, 1665, 200, "Dezembro de 2020", "RTX 3060 Ti", 256, 14.0)
		},
		["10DE:2489"] = new GpuSpec[1]
		{
			new GpuSpec("GA104-202", "Ampere", "8 nm (Samsung)", 392, 17.4, 4864, 80, 152, "GDDR6", 1410, 1665, 200, "Maio de 2021", "RTX 3060 Ti", 256, 14.0)
		},
		["10DE:24C9"] = new GpuSpec[1]
		{
			new GpuSpec("GA104-202", "Ampere", "8 nm (Samsung)", 392, 17.4, 4864, 80, 152, "GDDR6X", 1410, 1665, 200, "Outubro de 2022", "RTX 3060 Ti", 256, 19.0)
		},
		["10DE:2503"] = new GpuSpec[1]
		{
			new GpuSpec("GA106-300", "Ampere", "8 nm (Samsung)", 276, 12.0, 3584, 48, 112, "GDDR6", 1320, 1777, 170, "Fevereiro de 2021", "RTX 3060", 192, 15.0)
		},
		["10DE:2504"] = new GpuSpec[1]
		{
			new GpuSpec("GA106-302", "Ampere", "8 nm (Samsung)", 276, 12.0, 3584, 48, 112, "GDDR6", 1320, 1777, 170, "Maio de 2021", "RTX 3060", 192, 15.0)
		},
		["10DE:2544"] = new GpuSpec[1]
		{
			new GpuSpec("GA106-302", "Ampere", "8 nm (Samsung)", 276, 12.0, 3584, 48, 112, "GDDR6", 1320, 1777, 170, "Outubro de 2022", "RTX 3060", 128, 15.0)
		},
		["10DE:2507"] = new GpuSpec[1]
		{
			new GpuSpec("GA106-150", "Ampere", "8 nm (Samsung)", 276, 12.0, 2560, 32, 80, "GDDR6", 1552, 1777, 130, "Janeiro de 2022", "RTX 3050", 128, 14.0)
		},
		["10DE:2582"] = new GpuSpec[1]
		{
			new GpuSpec("GA107-150", "Ampere", "8 nm (Samsung)", 200, 8.7, 2560, 32, 80, "GDDR6", 1552, 1777, 115, "Dezembro de 2022", "RTX 3050", 128, 14.0)
		},
		["10DE:2584"] = new GpuSpec[1]
		{
			new GpuSpec("GA107-325", "Ampere", "8 nm (Samsung)", 200, 8.7, 2304, 32, 72, "GDDR6", 1042, 1470, 70, "Fevereiro de 2024", "RTX 3050", 96, 14.0)
		},
		["10DE:2520"] = new GpuSpec[1]
		{
			new GpuSpec("GA106M", "Ampere", "8 nm (Samsung)", 276, 12.0, 3840, 48, 120, "GDDR6", 900, 1425, 115, "Janeiro de 2021", "RTX 3060 Laptop", 192, 14.0)
		},
		["10DE:25A0"] = new GpuSpec[1]
		{
			new GpuSpec("GA107M", "Ampere", "8 nm (Samsung)", 200, 8.7, 2560, 32, 80, "GDDR6", 1035, 1485, 80, "Maio de 2021", "RTX 3050 Ti Laptop", 128, 12.0)
		},
		["10DE:25A2"] = new GpuSpec[1]
		{
			new GpuSpec("GA107M", "Ampere", "8 nm (Samsung)", 200, 8.7, 2048, 32, 64, "GDDR6", 1057, 1500, 75, "Maio de 2021", "RTX 3050 Laptop", 128, 12.0)
		},
		["10DE:2684"] = new GpuSpec[1]
		{
			new GpuSpec("AD102-300", "Ada Lovelace", "TSMC 4N (5 nm)", 609, 76.3, 16384, 176, 512, "GDDR6X", 2235, 2520, 450, "Outubro de 2022", "RTX 4090", 384, 21.0)
		},
		["10DE:2685"] = new GpuSpec[1]
		{
			new GpuSpec("AD102-250", "Ada Lovelace", "TSMC 4N (5 nm)", 609, 76.3, 14592, 176, 456, "GDDR6X", 2280, 2520, 425, "Janeiro de 2024", "RTX 4090 D", 384, 21.0)
		},
		["10DE:2702"] = new GpuSpec[1]
		{
			new GpuSpec("AD103-400", "Ada Lovelace", "TSMC 4N (5 nm)", 379, 45.9, 10240, 112, 320, "GDDR6X", 2295, 2550, 320, "Janeiro de 2024", "RTX 4080 SUPER", 256, 23.0)
		},
		["10DE:2704"] = new GpuSpec[1]
		{
			new GpuSpec("AD103-300", "Ada Lovelace", "TSMC 4N (5 nm)", 379, 45.9, 9728, 112, 304, "GDDR6X", 2205, 2505, 320, "Novembro de 2022", "RTX 4080", 256, 22.4)
		},
		["10DE:2705"] = new GpuSpec[1]
		{
			new GpuSpec("AD103-275", "Ada Lovelace", "TSMC 4N (5 nm)", 379, 45.9, 8448, 96, 264, "GDDR6X", 2340, 2610, 285, "Janeiro de 2024", "RTX 4070 Ti SUPER", 256, 21.0)
		},
		["10DE:2689"] = new GpuSpec[1]
		{
			new GpuSpec("AD102-175", "Ada Lovelace", "TSMC 4N (5 nm)", 609, 76.3, 8448, 96, 264, "GDDR6X", 2340, 2610, 285, "Janeiro de 2024", "RTX 4070 Ti SUPER", 256, 21.0)
		},
		["10DE:2782"] = new GpuSpec[1]
		{
			new GpuSpec("AD104-400", "Ada Lovelace", "TSMC 4N (5 nm)", 295, 35.8, 7680, 80, 240, "GDDR6X", 2310, 2610, 285, "Janeiro de 2023", "RTX 4070 Ti", 192, 21.0)
		},
		["10DE:2783"] = new GpuSpec[1]
		{
			new GpuSpec("AD104-350", "Ada Lovelace", "TSMC 4N (5 nm)", 295, 35.8, 7168, 80, 224, "GDDR6X", 1980, 2475, 220, "Janeiro de 2024", "RTX 4070 SUPER", 192, 21.0)
		},
		["10DE:2786"] = new GpuSpec[1]
		{
			new GpuSpec("AD104-250", "Ada Lovelace", "TSMC 4N (5 nm)", 295, 35.8, 5888, 64, 184, "GDDR6X", 1920, 2475, 200, "Abril de 2023", "RTX 4070", 192, 21.0)
		},
		["10DE:2805"] = new GpuSpec[1]
		{
			new GpuSpec("AD106-351", "Ada Lovelace", "TSMC 4N (5 nm)", 190, 22.9, 4352, 48, 136, "GDDR6", 2310, 2535, 165, "Julho de 2023", "RTX 4060 Ti", 128, 18.0)
		},
		["10DE:2803"] = new GpuSpec[1]
		{
			new GpuSpec("AD106-350", "Ada Lovelace", "TSMC 4N (5 nm)", 190, 22.9, 4352, 48, 136, "GDDR6", 2310, 2535, 160, "Maio de 2023", "RTX 4060 Ti", 128, 18.0)
		},
		["10DE:2882"] = new GpuSpec[1]
		{
			new GpuSpec("AD107-400", "Ada Lovelace", "TSMC 4N (5 nm)", 159, 18.9, 3072, 48, 96, "GDDR6", 1830, 2460, 115, "Junho de 2023", "RTX 4060", 128, 17.0)
		},
		["10DE:2860"] = new GpuSpec[1]
		{
			new GpuSpec("AD106M", "Ada Lovelace", "TSMC 4N (5 nm)", 190, 22.9, 4608, 48, 144, "GDDR6", 1605, 2175, 140, "Fevereiro de 2023", "RTX 4070 Laptop", 128, 16.0)
		},
		["10DE:28E0"] = new GpuSpec[1]
		{
			new GpuSpec("AD107M", "Ada Lovelace", "TSMC 4N (5 nm)", 159, 18.9, 3072, 48, 96, "GDDR6", 1470, 2370, 140, "Fevereiro de 2023", "RTX 4060 Laptop", 128, 16.0)
		},
		["10DE:28E1"] = new GpuSpec[1]
		{
			new GpuSpec("AD107M", "Ada Lovelace", "TSMC 4N (5 nm)", 159, 18.9, 2560, 32, 80, "GDDR6", 1605, 2370, 115, "Fevereiro de 2023", "RTX 4050 Laptop", 96, 16.0)
		},
		["10DE:2B85"] = new GpuSpec[1]
		{
			new GpuSpec("GB202-300", "Blackwell", "TSMC 4N (5 nm)", 750, 92.2, 21760, 176, 680, "GDDR7", 2017, 2407, 575, "Janeiro de 2025", "RTX 5090", 512, 28.0)
		},
		["10DE:2C02"] = new GpuSpec[1]
		{
			new GpuSpec("GB203-400", "Blackwell", "TSMC 4N (5 nm)", 378, 45.6, 10752, 112, 336, "GDDR7", 2295, 2617, 360, "Janeiro de 2025", "RTX 5080", 256, 30.0)
		},
		["10DE:2C05"] = new GpuSpec[1]
		{
			new GpuSpec("GB203-300", "Blackwell", "TSMC 4N (5 nm)", 378, 45.6, 8960, 96, 280, "GDDR7", 2295, 2452, 300, "Fevereiro de 2025", "RTX 5070 Ti", 256, 28.0)
		},
		["10DE:2F04"] = new GpuSpec[1]
		{
			new GpuSpec("GB205-300", "Blackwell", "TSMC 4N (5 nm)", 263, 31.1, 6144, 80, 192, "GDDR7", 2325, 2512, 250, "Março de 2025", "RTX 5070", 192, 28.0)
		},
		["10DE:2D04"] = new GpuSpec[1]
		{
			new GpuSpec("GB206-300", "Blackwell", "TSMC 4N (5 nm)", 181, 21.9, 4608, 48, 144, "GDDR7", 2407, 2572, 180, "Abril de 2025", "RTX 5060 Ti", 128, 28.0)
		},
		["10DE:2D05"] = new GpuSpec[1]
		{
			new GpuSpec("GB206-250", "Blackwell", "TSMC 4N (5 nm)", 181, 21.9, 3840, 48, 120, "GDDR7", 2280, 2497, 145, "Maio de 2025", "RTX 5060", 128, 28.0)
		},
		["8086:E20B"] = new GpuSpec[1]
		{
			new GpuSpec("BMG-G21", "Xe2 (Battlemage)", "5 nm (TSMC N5)", 272, 19.6, 2560, 80, 160, "GDDR6", 2670, 2850, 190, "Dezembro de 2024", "Arc B580", 192, 19.0)
		},
		["8086:E20C"] = new GpuSpec[1]
		{
			new GpuSpec("BMG-G21", "Xe2 (Battlemage)", "5 nm (TSMC N5)", 272, 19.6, 2304, 72, 144, "GDDR6", 2500, 2750, 150, "Janeiro de 2025", "Arc B570", 160, 19.0)
		},
		["8086:56A0"] = new GpuSpec[1]
		{
			new GpuSpec("ACM-G10", "Xe-HPG (Alchemist)", "6 nm (TSMC N6)", 406, 21.7, 4096, 128, 256, "GDDR6", 2100, 2400, 225, "Outubro de 2022", "Arc A770", 256, 17.5)
		},
		["8086:56A1"] = new GpuSpec[1]
		{
			new GpuSpec("ACM-G10", "Xe-HPG (Alchemist)", "6 nm (TSMC N6)", 406, 21.7, 3584, 112, 224, "GDDR6", 2050, 2400, 225, "Outubro de 2022", "Arc A750", 256, 16.0)
		},
		["8086:56A2"] = new GpuSpec[1]
		{
			new GpuSpec("ACM-G10", "Xe-HPG (Alchemist)", "6 nm (TSMC N6)", 406, 21.7, 3072, 96, 192, "GDDR6", 1700, 2000, 185, "Outubro de 2023", "Arc A580", 256, 16.0)
		},
		["8086:56A5"] = new GpuSpec[1]
		{
			new GpuSpec("ACM-G11", "Xe-HPG (Alchemist)", "6 nm (TSMC N6)", 157, 7.2, 1024, 32, 64, "GDDR6", 2000, 2050, 75, "Junho de 2022", "Arc A380", 96, 15.5)
		},
		["8086:56A6"] = new GpuSpec[1]
		{
			new GpuSpec("ACM-G11", "Xe-HPG (Alchemist)", "6 nm (TSMC N6)", 157, 7.2, 768, 24, 48, "GDDR6", 2000, 2000, 75, "Setembro de 2022", "Arc A310", 64, 15.5)
		}
	};

	private static readonly GpuSpec[] NameCatalog = (from s in Db.Values.SelectMany((GpuSpec[] x) => x)
		where !string.IsNullOrWhiteSpace(s.NameMatch)
		orderby s.NameMatch.Length descending
		select s).ToArray();

	public static readonly Dictionary<string, string> ArchFeatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["Blackwell"] = "DLSS 4 (Multi Frame Generation) · Ray Tracing 4ª ger. · Tensor 5ª ger. · Reflex 2 · NVENC 9ª ger.",
		["Ada Lovelace"] = "DLSS 3 (Frame Generation) · Ray Tracing 3ª ger. · Tensor 4ª ger. · Reflex · NVENC AV1",
		["Ampere"] = "DLSS 2 · Ray Tracing 2ª ger. · Tensor 3ª ger. · Reflex · Resizable BAR",
		["RDNA 4"] = "FSR 4 · Ray Accelerators 3ª ger. · AI Accelerators · Anti-Lag 2 · AV1",
		["RDNA 3"] = "FSR 3 · Ray Accelerators 2ª ger. · AI Accelerators · Smart Access Memory · AV1",
		["RDNA 3.0"] = "FSR 3 · Ray Accelerators 2ª ger. · AI Accelerators · Smart Access Memory · AV1",
		["RDNA 2"] = "FSR · Ray Accelerators 1ª ger. · Infinity Cache · Smart Access Memory",
		["RDNA 2.0"] = "FSR · Ray Accelerators 1ª ger. · Infinity Cache · Smart Access Memory",
		["Xe-HPG (Alchemist)"] = "XeSS · Ray Tracing Hardware · XMX AI Engines · Deep Link · AV1 Hardware Encode/Decode",
		["Xe2 (Battlemage)"] = "XeSS 2 (Frame Generation + Low Latency) · Ray Tracing 2ª ger. · XMX AI 2ª ger. · AV1"
	};

	public static GpuSpec? Find(string vendorId, string deviceId, string name)
	{
		string v = (vendorId ?? "").Trim().ToUpperInvariant();
		string d = (deviceId ?? "").Trim().ToUpperInvariant();
		string n = (name ?? "").Trim();
		if (v.Length > 0 && d.Length > 0 && Db.TryGetValue(v + ":" + d, out GpuSpec[] specs))
		{
			return (from s in specs
				where !string.IsNullOrEmpty(s.NameMatch)
				orderby s.NameMatch.Length descending
				select s).FirstOrDefault((GpuSpec s) => n.Contains(s.NameMatch, StringComparison.OrdinalIgnoreCase)) ?? specs[0];
		}
		if (d.Length > 0)
		{
			GpuSpec[] byDeviceOnly = Db.FirstOrDefault((KeyValuePair<string, GpuSpec[]> kv) => kv.Key.EndsWith(":" + d, StringComparison.OrdinalIgnoreCase)).Value;
			if (byDeviceOnly != null && byDeviceOnly.Length > 0)
			{
				return (from s in byDeviceOnly
					where !string.IsNullOrEmpty(s.NameMatch)
					orderby s.NameMatch.Length descending
					select s).FirstOrDefault((GpuSpec s) => n.Contains(s.NameMatch, StringComparison.OrdinalIgnoreCase)) ?? byDeviceOnly[0];
			}
		}
		if (n.Length > 0)
		{
			GpuSpec byCommercialName = NameCatalog.FirstOrDefault((GpuSpec s) => n.Contains(s.NameMatch, StringComparison.OrdinalIgnoreCase));
			if (byCommercialName != null)
			{
				return byCommercialName;
			}
		}
		return null;
	}

	public static string SubvendorName(string id)
	{
		return id.ToUpperInvariant() switch
		{
			"1043" => "ASUS", 
			"1458" => "Gigabyte", 
			"1462" => "MSI", 
			"3842" => "EVGA", 
			"19DA" => "Zotac", 
			"196E" => "PNY", 
			"10DE" => "NVIDIA", 
			"1002" => "AMD", 
			"1DA2" => "Sapphire", 
			"1682" => "XFX", 
			"148C" => "PowerColor", 
			"1849" => "ASRock", 
			"7377" => "Colorful", 
			"1569" => "Palit", 
			"10B0" => "Gainward", 
			"1B4C" => "Galax / KFA2", 
			"8086" => "Intel", 
			"1028" => "Dell", 
			"103C" => "HP", 
			"17AA" => "Lenovo", 
			"1025" => "Acer", 
			"144D" => "Samsung", 
			"1D05" => "Tongfang", 
			_ => "Desconhecido (" + id + ")", 
		};
	}
}
