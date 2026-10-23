using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Triarch;

public static class DriverManager
{
	private static readonly HttpClient Http = CreateClient();

	public static string DownloadFolder => Path.Combine(Path.GetTempPath(), "Triarch", "drivers");

	private static HttpClient CreateClient()
	{
		HttpClient httpClient = new HttpClient();
		httpClient.Timeout = TimeSpan.FromMinutes(30.0);
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Triarch");
		return httpClient;
	}

	private static string? Str(ManagementBaseObject o, string p)
	{
		return o[p]?.ToString()?.Trim();
	}

	private static string WmiDate(string? s)
	{
		try
		{
			if (!string.IsNullOrEmpty(s))
			{
				return ManagementDateTimeConverter.ToDateTime(s).ToString("dd/MM/yyyy");
			}
		}
		catch
		{
		}
		return "—";
	}

	public static List<DriverRow> Inventory()
	{
		List<DriverRow> list = new List<DriverRow>();
		try
		{
			using ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT DeviceName, DeviceClass, Manufacturer, DriverVersion, DriverDate, IsSigned FROM Win32_PnPSignedDriver");
			bool b = default;
			foreach (ManagementObject o in s.Get())
			{
				string name = Str(o, "DeviceName") ?? "";
				if (name.Length == 0)
				{
					continue;
				}
				string cls = Str(o, "DeviceClass") ?? "";
				string maker = Str(o, "Manufacturer") ?? "";
				bool flag;
				switch (cls)
				{
				case "HDC":
				case "NET":
				case "USB":
				case "DISPLAY":
				case "MEDIA":
				case "BLUETOOTH":
				case "SCSIADAPTER":
				case "SYSTEM":
					flag = true;
					break;
				default:
					flag = false;
					break;
				}
				bool main = flag;
				flag = maker.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase);
				if (flag)
				{
					bool flag2;
					switch (cls)
					{
					case "DISPLAY":
					case "NET":
					case "MEDIA":
						flag2 = true;
						break;
					default:
						flag2 = false;
						break;
					}
					flag = !flag2;
				}
				if (!flag && main)
				{
					DriverRow driverRow = new DriverRow
					{
						Device = name,
						Class = cls,
						Vendor = maker,
						Version = (Str(o, "DriverVersion") ?? "—"),
						Date = WmiDate(Str(o, "DriverDate"))
					};
					object obj = o["IsSigned"];
					int num;
					if (obj is bool)
					{
						b = (bool)obj;
						num = 1;
					}
					else
					{
						num = 0;
					}
					driverRow.Signed = (byte)((uint)num & (b ? 1u : 0u)) != 0;
					list.Add(driverRow);
					o.Dispose();
				}
			}
		}
		catch
		{
		}
		return (from r in list
			orderby r.Class, r.Device
			select r).ToList();
	}

	public static string NvidiaFriendly(string wmiVersion)
	{
		string[] p = wmiVersion.Split('.');
		if (p.Length != 4)
		{
			return wmiVersion;
		}
		string digits = p[2] + p[3];
		if (digits.Length < 5)
		{
			return wmiVersion;
		}
		string text = digits;
		digits = text.Substring(text.Length - 5);
		return digits.Substring(0, 3) + "." + digits.Substring(3);
	}

	public static List<GpuDriverStatus> ReadGpus()
	{
		List<GpuDriverStatus> list = new List<GpuDriverStatus>();
		try
		{
			using ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterCompatibility FROM Win32_VideoController");
			foreach (ManagementObject o in s.Get())
			{
				string name = Str(o, "Name") ?? "";
				string maker = Str(o, "AdapterCompatibility") + " " + name;
				string ver = Str(o, "DriverVersion") ?? "";
				if (name.Length != 0 && !name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase))
				{
					GpuDriverStatus g = new GpuDriverStatus
					{
						GpuName = name
					};
					if (maker.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
					{
						g.Vendor = "NVIDIA";
						g.Installed = NvidiaFriendly(ver);
						g.OfficialPage = "https://www.nvidia.com/Download/index.aspx";
					}
					else if (maker.Contains("AMD", StringComparison.OrdinalIgnoreCase) || maker.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || maker.Contains("Advanced Micro", StringComparison.OrdinalIgnoreCase))
					{
						g.Vendor = "AMD";
						g.Installed = ver;
						g.OfficialPage = "https://www.amd.com/en/support/download/drivers.html";
					}
					else if (maker.Contains("Intel", StringComparison.OrdinalIgnoreCase))
					{
						g.Vendor = "Intel";
						g.Installed = ver;
						g.OfficialPage = "https://www.intel.com/content/www/us/en/download-center/home.html";
					}
					else
					{
						g.Vendor = "";
						g.Installed = ver;
					}
					list.Add(g);
					o.Dispose();
				}
			}
		}
		catch
		{
		}
		return list;
	}

	public static async Task CheckNvidiaAsync(GpuDriverStatus g, bool studio, CancellationToken ct)
	{
		g.Latest = "";
		g.DownloadUrl = "";
		g.SizeText = "";
		try
		{
			(int, int) tuple = await ResolveNvidiaIdsAsync(g.GpuName, ct);
			int psid = tuple.Item1;
			int pfid = tuple.Item2;
			string url = "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php" + $"?func=DriverManualLookup&psid={psid}&pfid={pfid}&osID=57&languageCode=1033&beta=0&isWHQL=1&dch=1" + $"&upCRD={(studio ? 1 : 0)}&qnf=0&sort1=1&numberOfResults=1";
			using JsonDocument doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
			string ver = null;
			string dl = null;
			string size = null;
			Find(doc.RootElement, ref ver, ref dl, ref size);
			if (string.IsNullOrEmpty(ver) || string.IsNullOrEmpty(dl))
			{
				g.Message = "A NVIDIA não devolveu um driver para esta placa. Use a página oficial.";
				return;
			}
			g.Latest = ver.Trim();
			g.DownloadUrl = dl.Replace("http://", "https://");
			g.SizeText = size ?? "";
			g.Message = (g.UpdateAvailable ? $"Há um driver mais novo: {g.Latest} (instalado: {g.Installed})." : ("Você já está com o mais recente (" + g.Installed + ")."));
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex2)
		{
			g.Message = "Não foi possível consultar a NVIDIA: " + ex2.Message;
		}
	}

	public static async Task CheckAmdAsync(GpuDriverStatus g, CancellationToken ct)
	{
		g.Latest = "";
		g.DownloadUrl = "";
		g.SizeText = "";
		try
		{
			Match m = Regex.Match(g.GpuName, "RX\\s*(\\d{4})\\s*(XTX|XT|GRE|M)?", RegexOptions.IgnoreCase);
			if (!m.Success)
			{
				g.Message = "AMD: modelo não reconhecido para consulta automática. Use a página oficial.";
				return;
			}
			int n = int.Parse(m.Groups[1].Value);
			string fam = $"radeon-rx-{n / 1000}000-series";
			string suffix = (m.Groups[2].Success ? ("-" + m.Groups[2].Value.ToLowerInvariant()) : "");
			using HttpRequestMessage req = new HttpRequestMessage(requestUri: g.OfficialPage = $"https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/{fam}/amd-radeon-rx-{n}{suffix}.html", method: HttpMethod.Get);
			req.Headers.Referrer = new Uri("https://www.amd.com/");
			using HttpResponseMessage resp = await Http.SendAsync(req, ct);
			string html = await resp.Content.ReadAsStringAsync(ct);
			Match hit = Regex.Match(html, "https://drivers\\.amd\\.com/drivers/[^\"'\\s<>\\\\]*win11[^\"'\\s<>\\\\]*\\.exe", RegexOptions.IgnoreCase);
			if (!hit.Success)
			{
				hit = Regex.Match(html, "https://drivers\\.amd\\.com/drivers/[^\"'\\s<>\\\\]*\\.exe", RegexOptions.IgnoreCase);
			}
			if (!hit.Success)
			{
				g.Message = "AMD: o site não devolveu o link do instalador (pode estar protegido). Abra a página oficial do seu modelo.";
				return;
			}
			g.DownloadUrl = hit.Value;
			Match v = Regex.Match(hit.Value, "edition-([\\d.]+)", RegexOptions.IgnoreCase);
			g.Latest = (v.Success ? v.Groups[1].Value : Path.GetFileNameWithoutExtension(hit.Value));
			g.Message = $"AMD Software Adrenalin {g.Latest} disponível para o seu modelo. Compare com a versão mostrada no AMD Software (o Windows mostra {g.Installed}).";
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex2)
		{
			g.Message = "Não foi possível consultar a AMD: " + ex2.Message;
		}
	}

	private static void Find(JsonElement e, ref string? ver, ref string? dl, ref string? size)
	{
		if (e.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty p in e.EnumerateObject())
			{
				if (p.Value.ValueKind == JsonValueKind.String)
				{
					if (p.NameEquals("Version") && ver == null)
					{
						ver = p.Value.GetString();
					}
					else if (p.NameEquals("DownloadURL") && dl == null)
					{
						dl = p.Value.GetString();
					}
					else if (p.NameEquals("DownloadURLFileSize") && size == null)
					{
						size = p.Value.GetString();
					}
				}
				else
				{
					Find(p.Value, ref ver, ref dl, ref size);
				}
			}
			return;
		}
		if (e.ValueKind != JsonValueKind.Array)
		{
			return;
		}
		foreach (JsonElement item in e.EnumerateArray())
		{
			Find(item, ref ver, ref dl, ref size);
		}
	}

	private static async Task<(int psid, int pfid)> ResolveNvidiaIdsAsync(string gpuName, CancellationToken ct)
	{
		bool mobile = Regex.IsMatch(gpuName, "Laptop|Max-Q|Mobile|Notebook", RegexOptions.IgnoreCase);
		Match m = Regex.Match(gpuName, "(RTX|GTX|GT)\\s*(\\d{3,4})", RegexOptions.IgnoreCase);
		if (!m.Success)
		{
			throw new InvalidOperationException("Modelo de placa não reconhecido: " + gpuName);
		}
		string text = m.Groups[1].Value.ToUpperInvariant();
		int n = int.Parse(m.Groups[2].Value);
		string series;
		if (text == "RTX")
		{
			series = $"RTX {n / 100} Series";
		}
		else if (n >= 1600 && n < 1700)
		{
			series = "16 Series";
		}
		else if (n >= 1000 && n < 1100)
		{
			series = "10 Series";
		}
		else if (n >= 900 && n < 1000)
		{
			series = "900 Series";
		}
		else if (n >= 700 && n < 800)
		{
			series = "700 Series";
		}
		else
		{
			series = $"{n / 100 * 100} Series";
		}
		if (text == "RTX" && n >= 2000)
		{
			series = $"RTX {n / 100} Series";
		}
		List<(string, string)> cand = (from s in (from x in XDocument.Parse(await Http.GetStringAsync("https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=2&ParentID=1", ct)).Descendants("LookupValue")
				select (Name: ((string?)x.Element("Name")) ?? "", Value: ((string?)x.Element("Value")) ?? "")).ToList()
			where s.Name.Contains(series, StringComparison.OrdinalIgnoreCase) && s.Name.Contains("Notebook", StringComparison.OrdinalIgnoreCase) == mobile
			orderby s.Name.Length
			select s).ToList();
		if (cand.Count == 0)
		{
			throw new InvalidOperationException("Série da placa não encontrada na NVIDIA (" + series + ").");
		}
		int psid = int.Parse(cand[0].Item2);
		List<(string, string)> prods = (from x in XDocument.Parse(await Http.GetStringAsync($"https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3&ParentID={psid}", ct)).Descendants("LookupValue")
			select (Name: ((string?)x.Element("Name")) ?? "", Value: ((string?)x.Element("Value")) ?? "")).ToList();
		if (prods.Count == 0)
		{
			throw new InvalidOperationException("Nenhum produto listado para a série.");
		}
		string key = Regex.Replace(gpuName, "^NVIDIA\\s+", "", RegexOptions.IgnoreCase).Replace("Laptop GPU", "").Trim();
		(string, string) exact = prods.FirstOrDefault(((string Name, string Value) p) => string.Equals(p.Name.Trim(), key, StringComparison.OrdinalIgnoreCase));
		(string, string) pick = ((exact.Item2 != null) ? exact : prods.FirstOrDefault(((string Name, string Value) p) => p.Name.Contains(m.Value, StringComparison.OrdinalIgnoreCase)));
		if (pick.Item2 == null)
		{
			pick = prods[0];
		}
		return (psid: psid, pfid: int.Parse(pick.Item2));
	}

	public static async Task<string> DownloadAndLaunchAsync(GpuDriverStatus g, Action<string, double> progress, CancellationToken ct)
	{
		Directory.CreateDirectory(DownloadFolder);
		string file = Path.Combine(DownloadFolder, Path.GetFileName(new Uri(g.DownloadUrl).AbsolutePath));
		progress("Baixando o driver…", 0.0);
		using HttpRequestMessage dreq = new HttpRequestMessage(HttpMethod.Get, g.DownloadUrl);
		if (g.Vendor == "AMD")
		{
			dreq.Headers.Referrer = new Uri("https://www.amd.com/");
		}
		using (HttpResponseMessage resp = await Http.SendAsync(dreq, HttpCompletionOption.ResponseHeadersRead, ct))
		{
			resp.EnsureSuccessStatusCode();
			long total = resp.Content.Headers.ContentLength.GetValueOrDefault();
			await using Stream src = await resp.Content.ReadAsStreamAsync(ct);
			await using FileStream dst = File.Create(file);
			byte[] buf = new byte[1048576];
			long done = 0L;
			Stopwatch sw = Stopwatch.StartNew();
			while (true)
			{
				int num;
				int r = (num = await src.ReadAsync(buf, ct));
				if (num <= 0)
				{
					break;
				}
				await dst.WriteAsync(buf.AsMemory(0, r), ct);
				done += r;
				double mb = (double)done / 1048576.0;
				double speed = mb / Math.Max(0.1, sw.Elapsed.TotalSeconds);
				progress($"Baixando… {mb:F0} de {((total > 0) ? ((double)total / 1048576.0).ToString("F0") : "?")} MB · {speed:F1} MB/s", (total > 0) ? ((double)done / (double)total) : 0.0);
			}
		}
		progress("Conferindo a assinatura digital…", 1.0);
		VerifyPublisher(file, (g.Vendor == "AMD") ? "Advanced Micro Devices" : "NVIDIA");
		progress("Criando ponto de restauração (se o Windows permitir)…", 1.0);
		string restore = await TryRestorePointAsync();
		Process.Start(new ProcessStartInfo(file)
		{
			UseShellExecute = true,
			WorkingDirectory = DownloadFolder
		});
		return $"Instalador {g.Latest} aberto (assinatura {g.Vendor} válida). {restore} Siga o instalador; a tela pode piscar durante a instalação.";
	}

	private static void VerifyPublisher(string path, string publisher)
	{
		int trust = WinVerifyTrustFile(path);
		if (trust != 0)
		{
			try
			{
				File.Delete(path);
			}
			catch
			{
			}
			throw new InvalidOperationException($"Assinatura digital inválida (0x{trust:X8}). O arquivo foi descartado.");
		}
		string signer = "";
		try
		{
			using X509Certificate cert = X509Certificate.CreateFromSignedFile(path);
			signer = new X509Certificate2(cert).Subject;
		}
		catch
		{
		}
		if (!signer.Contains(publisher, StringComparison.OrdinalIgnoreCase))
		{
			try
			{
				File.Delete(path);
			}
			catch
			{
			}
			throw new InvalidOperationException("O arquivo está assinado por outro fabricante (" + signer + "). Foi descartado.");
		}
	}

	private static async Task<string> TryRestorePointAsync()
	{
		try
		{
			ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"Checkpoint-Computer -Description 'Triarch - antes do driver' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop\"")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardError = true,
				RedirectStandardOutput = true
			};
			using Process p = Process.Start(psi);
			using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(60.0));
			await p.WaitForExitAsync(cts.Token);
			return (p.ExitCode == 0) ? "Ponto de restauração criado." : "Ponto de restauração não criado (proteção do sistema desligada ou já criado hoje).";
		}
		catch
		{
			return "Ponto de restauração não criado.";
		}
	}

	public static BoardInfo ReadBoard()
	{
		BoardInfo b = new BoardInfo();
		try
		{
			using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
			{
				using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = s.Get().GetEnumerator();
				if (managementObjectEnumerator.MoveNext())
				{
					ManagementObject o = (ManagementObject)managementObjectEnumerator.Current;
					b.Maker = Str(o, "Manufacturer") ?? "";
					b.Model = Str(o, "Product") ?? "";
				}
			}
			using (ManagementObjectSearcher s2 = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))
			{
				using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = s2.Get().GetEnumerator();
				if (managementObjectEnumerator.MoveNext())
				{
					ManagementObject o2 = (ManagementObject)managementObjectEnumerator.Current;
					b.Bios = Str(o2, "SMBIOSBIOSVersion") + " (" + WmiDate(Str(o2, "ReleaseDate")) + ")";
				}
			}
			string sys = "";
			string sysModel = "";
			using (ManagementObjectSearcher s3 = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
			{
				using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = s3.Get().GetEnumerator();
				if (managementObjectEnumerator.MoveNext())
				{
					ManagementObject o3 = (ManagementObject)managementObjectEnumerator.Current;
					sys = Str(o3, "Manufacturer") ?? "";
					sysModel = Str(o3, "Model") ?? "";
				}
			}
			string all = b.Maker + " " + sys;
			string q = Uri.EscapeDataString((b.Model.Length > 0 && !b.Model.Contains("Default", StringComparison.OrdinalIgnoreCase)) ? b.Model : sysModel);
			if (all.Contains("ASUS", StringComparison.OrdinalIgnoreCase))
			{
				b.Brand = "ASUS";
				b.SupportUrl = "https://www.asus.com/support/download-center/";
			}
			else if (all.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) || all.Contains("MSI", StringComparison.OrdinalIgnoreCase))
			{
				b.Brand = "MSI";
				b.SupportUrl = "https://www.msi.com/search/" + q;
			}
			else if (all.Contains("GIGABYTE", StringComparison.OrdinalIgnoreCase))
			{
				b.Brand = "Gigabyte";
				b.SupportUrl = "https://www.gigabyte.com/Support";
			}
			else if (all.Contains("ASRock", StringComparison.OrdinalIgnoreCase))
			{
				b.Brand = "ASRock";
				b.SupportUrl = "https://www.asrock.com/support/index.asp";
			}
			else if (all.Contains("Dell", StringComparison.OrdinalIgnoreCase))
			{
				b.Brand = "Dell";
				b.SupportUrl = "https://www.dell.com/support/home/";
			}
			else if (all.Contains("LENOVO", StringComparison.OrdinalIgnoreCase))
			{
				b.Brand = "Lenovo";
				b.SupportUrl = "https://support.lenovo.com/";
			}
			else if (all.Contains("HP", StringComparison.OrdinalIgnoreCase) || all.Contains("Hewlett", StringComparison.OrdinalIgnoreCase))
			{
				b.Brand = "HP";
				b.SupportUrl = "https://support.hp.com/";
			}
			else if (all.Contains("Acer", StringComparison.OrdinalIgnoreCase))
			{
				b.Brand = "Acer";
				b.SupportUrl = "https://www.acer.com/support";
			}
			if (b.Maker.Length == 0)
			{
				b.Maker = sys;
			}
			if (b.Model.Length == 0)
			{
				b.Model = sysModel;
			}
		}
		catch
		{
		}
		return b;
	}

	private static dynamic NewCom(string progId)
	{
		return Activator.CreateInstance(Type.GetTypeFromProgID(progId) ?? throw new InvalidOperationException("Serviço do Windows Update indisponível."));
	}

	public static Task<List<WuDriver>> SearchWindowsUpdateAsync()
	{
		return Task.Run(() =>
		{
			List<WuDriver> list = new List<WuDriver>();
			dynamic val = NewCom("Microsoft.Update.Session");
			dynamic val2 = val.CreateUpdateSearcher();
			dynamic val3 = val2.Search("IsInstalled=0 and Type='Driver' and IsHidden=0");
			foreach (dynamic current in val3.Updates)
			{
				list.Add(new WuDriver
				{
					Title = (string)current.Title,
					SizeBytes = (long)Convert.ToInt64(current.MaxDownloadSize),
					Update = current
				});
			}
			return list;
		});
	}

	public static Task<string> InstallWindowsUpdateAsync(List<WuDriver> items, Action<string> progress)
	{
		return Task.Run(() =>
		{
			dynamic val = NewCom("Microsoft.Update.Session");
			dynamic val2 = NewCom("Microsoft.Update.UpdateColl");
			foreach (WuDriver item in items)
			{
				dynamic update = item.Update;
				try
				{
					update.AcceptEula();
				}
				catch
				{
				}
				val2.Add(update);
			}
			progress("Baixando drivers do Windows Update…");
			dynamic val3 = val.CreateUpdateDownloader();
			val3.Updates = val2;
			val3.Download();
			progress("Instalando…");
			dynamic val4 = val.CreateUpdateInstaller();
			val4.Updates = val2;
			dynamic val5 = val4.Install();
			int num = (int)val5.ResultCode;
			bool flag = (bool)val5.RebootRequired;
			bool flag2 = (uint)(num - 2) <= 1u;
			return (flag2 ? "Instalação concluída." : $"Instalação terminou com código {num}.") + (flag ? " Reinicie o PC para finalizar." : "");
		});
	}

	public static async Task<string> BackupAsync(Action<string> progress, CancellationToken ct)
	{
		string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Triarch-Drivers", DateTime.Now.ToString("yyyyMMdd-HHmm"));
		Directory.CreateDirectory(dir);
		ProcessStartInfo psi = new ProcessStartInfo("pnputil.exe", "/export-driver * \"" + dir + "\"")
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true
		};
		using Process p = Process.Start(psi);
		int n = 0;
		string line;
		while ((line = await p.StandardOutput.ReadLineAsync(ct)) != null)
		{
			if (line.Contains(".inf", StringComparison.OrdinalIgnoreCase))
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(20, 1);
				defaultInterpolatedStringHandler.AppendLiteral("Exportando drivers… ");
				int num = n + 1;
				n = num;
				defaultInterpolatedStringHandler.AppendFormatted(num);
				progress(defaultInterpolatedStringHandler.ToStringAndClear());
			}
		}
		await p.WaitForExitAsync(ct);
		int folders = Directory.GetDirectories(dir).Length;
		return (folders > 0) ? $"{folders} drivers salvos em {dir}" : "Nada foi exportado (execute o programa como administrador).";
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct WINTRUST_FILE_INFO
	{
		public uint cbStruct;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string pcwszFilePath;

		public nint hFile;

		public nint pgKnownSubject;
	}

	private struct WINTRUST_DATA
	{
		public uint cbStruct;

		public nint pPolicyCallbackData;

		public nint pSIPClientData;

		public uint dwUIChoice;

		public uint fdwRevocationChecks;

		public uint dwUnionChoice;

		public nint pFile;

		public uint dwStateAction;

		public nint hWVTStateData;

		public nint pwszURLReference;

		public uint dwProvFlags;

		public uint dwUIContext;

		public nint pSignatureSettings;
	}

	[DllImport("wintrust.dll", ExactSpelling = true)]
	private static extern int WinVerifyTrust(nint hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WINTRUST_DATA pWVTData);

	internal static int WinVerifyTrustFile(string path)
	{
		WINTRUST_FILE_INFO file = new WINTRUST_FILE_INFO
		{
			pcwszFilePath = path
		};
		file.cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>();
		nint pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
		try
		{
			Marshal.StructureToPtr(file, pFile, fDeleteOld: false);
			WINTRUST_DATA data = new WINTRUST_DATA
			{
				dwUIChoice = 2u,
				fdwRevocationChecks = 0u,
				dwUnionChoice = 1u,
				pFile = pFile,
				dwStateAction = 0u,
				dwProvFlags = 16u
			};
			data.cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>();
			return WinVerifyTrust(new IntPtr(-1), new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"), ref data);
		}
		finally
		{
			Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFile);
			Marshal.FreeHGlobal(pFile);
		}
	}

	private static void UnusedAnchor()
	{
	}
}
