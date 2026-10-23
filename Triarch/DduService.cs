using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Triarch;

public static class DduService
{
	private const int ErrorCancelled = 1223;

	private const string ExeName = "Display Driver Uninstaller.exe";

	private static readonly string[] FallbackDownloadUrls = new string[3] { "https://download.wagnardsoft.com/DDU/DDU%20v18.1.6.2.exe", "https://download.wagnardsoft.com/DDU/DDU%20v18.1.6.1.exe", "https://download.wagnardsoft.com/DDU/DDU%20v18.1.6.0.exe" };

	private static readonly HttpClient Http = CreateHttpClient();

	public static string DduDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "tools", "ddu");

	public static string ExecutablePath => Path.Combine(DduDirectory, "Display Driver Uninstaller.exe");

	private static HttpClient CreateHttpClient()
	{
		HttpClient httpClient = new HttpClient();
		httpClient.Timeout = TimeSpan.FromMinutes(5.0);
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Triarch/1.0");
		return httpClient;
	}

	public static bool IsInstalled()
	{
		return File.Exists(ExecutablePath);
	}

	public static DduGpuDetection DetectPrimaryGpu()
	{
		List<(string, DduGpuVendor, bool, ulong)> candidates = new List<(string, DduGpuVendor, bool, ulong)>();
		try
		{
			using ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID, AdapterCompatibility, AdapterRAM FROM Win32_VideoController");
			foreach (ManagementObject vc in searcher.Get())
			{
				string name = vc["Name"]?.ToString()?.Trim() ?? "";
				string pnp = vc["PNPDeviceID"]?.ToString()?.ToUpperInvariant() ?? "";
				string compat = vc["AdapterCompatibility"]?.ToString() ?? "";
				ulong vram = 0uL;
				try
				{
					object ramObj = vc["AdapterRAM"];
					if (ramObj != null)
					{
						vram = Convert.ToUInt64(ramObj);
					}
				}
				catch
				{
				}
				if (!string.IsNullOrWhiteSpace(name))
				{
					DduGpuVendor vendor = ResolveVendor(name, pnp, compat);
					bool isDiscrete = vendor == DduGpuVendor.Nvidia || (vendor == DduGpuVendor.Amd && (name.Contains("RX ", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon VII", StringComparison.OrdinalIgnoreCase) || name.Contains("PRO ", StringComparison.OrdinalIgnoreCase))) || (vendor == DduGpuVendor.Intel && name.Contains("Arc", StringComparison.OrdinalIgnoreCase));
					candidates.Add((name, vendor, isDiscrete, vram));
				}
			}
		}
		catch
		{
		}
		(string, DduGpuVendor, bool, ulong) best = candidates.OrderByDescending(((string Name, DduGpuVendor Vendor, bool IsDiscrete, ulong Vram) c) => c.IsDiscrete).ThenByDescending(((string Name, DduGpuVendor Vendor, bool IsDiscrete, ulong Vram) c) => c.Vendor != DduGpuVendor.Unknown).ThenByDescending(((string Name, DduGpuVendor Vendor, bool IsDiscrete, ulong Vram) c) => c.Vram)
			.FirstOrDefault();
		if (!string.IsNullOrEmpty(best.Item1))
		{
			return new DduGpuDetection(best.Item1, best.Item2);
		}
		return new DduGpuDetection("GPU não identificada", DduGpuVendor.Unknown);
	}

	public static Task<DduGpuDetection> DetectPrimaryGpuAsync()
	{
		return Task.Run((Func<DduGpuDetection>)DetectPrimaryGpu);
	}

	private static DduGpuVendor ResolveVendor(string name, string pnp, string compat)
	{
		if (pnp.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) || name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || compat.Contains("AMD", StringComparison.OrdinalIgnoreCase) || compat.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase))
		{
			return DduGpuVendor.Amd;
		}
		if (pnp.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) || name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) || name.Contains("RTX", StringComparison.OrdinalIgnoreCase) || name.Contains("GTX", StringComparison.OrdinalIgnoreCase) || compat.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
		{
			return DduGpuVendor.Nvidia;
		}
		if (pnp.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase) || name.Contains("Intel", StringComparison.OrdinalIgnoreCase) || name.Contains("Arc ", StringComparison.OrdinalIgnoreCase) || name.Contains("Iris", StringComparison.OrdinalIgnoreCase) || compat.Contains("Intel", StringComparison.OrdinalIgnoreCase))
		{
			return DduGpuVendor.Intel;
		}
		return DduGpuVendor.Unknown;
	}

	public static string GetSilentArguments(DduGpuVendor vendor)
	{
		return vendor switch
		{
			DduGpuVendor.Amd => "-silent -cleanamd -restart", 
			DduGpuVendor.Nvidia => "-silent -cleannvidia -restart", 
			DduGpuVendor.Intel => "-silent -cleanintel -restart", 
			_ => throw new InvalidOperationException("Não foi possível determinar o fabricante da GPU (AMD, NVIDIA ou Intel) para a limpeza silenciosa."), 
		};
	}

	public static async Task EnsureInstalledAsync(IProgress<int>? progress = null, IProgress<string>? statusProgress = null, CancellationToken ct = default(CancellationToken))
	{
		if (IsInstalled())
		{
			progress?.Report(100);
			statusProgress?.Report("Pronto");
			return;
		}
		Directory.CreateDirectory(DduDirectory);
		string tempDir = Path.Combine(Path.GetTempPath(), "Triarch-DDU-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempDir);
		try
		{
			progress?.Report(2);
			statusProgress?.Report("Localizando versão portátil mais recente do DDU…");
			List<string> urls = await ResolveCandidateUrlsAsync(ct);
			string archivePath = Path.Combine(tempDir, "ddu-portable-package.bin");
			Exception lastError = null;
			bool downloaded = false;
			foreach (string url in urls)
			{
				try
				{
					statusProgress?.Report("Baixando Display Driver Uninstaller (DDU)…");
					using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
					response.EnsureSuccessStatusCode();
					long totalBytes = response.Content.Headers.ContentLength.GetValueOrDefault();
					await using Stream netStream = await response.Content.ReadAsStreamAsync(ct);
					await using (FileStream fileStream = File.Create(archivePath))
					{
						byte[] buffer = new byte[81920];
						long totalRead = 0L;
						while (true)
						{
							int num;
							int read = (num = await netStream.ReadAsync(buffer, ct));
							if (num <= 0)
							{
								break;
							}
							await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
							totalRead += read;
							if (totalBytes > 0)
							{
								int pct = (int)Math.Clamp(5.0 + (double)totalRead * 75.0 / (double)totalBytes, 5.0, 80.0);
								progress?.Report(pct);
								statusProgress?.Report($"Baixando DDU… {(double)totalRead / 1048576.0:0.1} / {(double)totalBytes / 1048576.0:0.1} MB ({pct}%)");
							}
							else
							{
								statusProgress?.Report($"Baixando DDU… {(double)totalRead / 1048576.0:0.1} MB");
							}
						}
					}
					downloaded = true;
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception ex2)
				{
					lastError = ex2;
					continue;
				}
				break;
			}
			if (!downloaded)
			{
				throw new InvalidOperationException("Não foi possível baixar o pacote portátil do DDU: " + (lastError?.Message ?? "sem conexão"));
			}
			progress?.Report(85);
			statusProgress?.Report("Extraindo arquivos do DDU…");
			string extractDir = Path.Combine(tempDir, "extracted");
			Directory.CreateDirectory(extractDir);
			await Task.Run(() =>
			{
				ExtractPackage(archivePath, extractDir);
			}, ct);
			progress?.Report(95);
			statusProgress?.Report("Instalando em resources/tools/ddu…");
			string foundExe = Directory.EnumerateFiles(extractDir, "Display Driver Uninstaller.exe", SearchOption.AllDirectories).FirstOrDefault();
			if (foundExe == null)
			{
				foreach (string nested in (from f in Directory.EnumerateFiles(extractDir, "*.*", SearchOption.AllDirectories)
					where f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
					select f).ToList())
				{
					string nestedOut = Path.Combine(extractDir, "nested_" + Path.GetFileNameWithoutExtension(nested));
					Directory.CreateDirectory(nestedOut);
					try
					{
						ExtractPackage(nested, nestedOut);
					}
					catch
					{
					}
				}
				foundExe = Directory.EnumerateFiles(extractDir, "Display Driver Uninstaller.exe", SearchOption.AllDirectories).FirstOrDefault();
			}
			if (foundExe == null)
			{
				throw new FileNotFoundException("O executável 'Display Driver Uninstaller.exe' não foi encontrado após a extração do pacote.");
			}
			CopyDirectoryRecursive(Path.GetDirectoryName(foundExe), DduDirectory);
			if (!IsInstalled())
			{
				throw new IOException($"Falha ao copiar '{"Display Driver Uninstaller.exe"}' para '{DduDirectory}'.");
			}
			progress?.Report(100);
			statusProgress?.Report("Pronto");
		}
		finally
		{
			try
			{
				Directory.Delete(tempDir, recursive: true);
			}
			catch
			{
			}
		}
	}

	private static async Task<List<string>> ResolveCandidateUrlsAsync(CancellationToken ct)
	{
		List<string> urls = new List<string>();
		try
		{
			using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			cts.CancelAfter(TimeSpan.FromSeconds(6.0));
			using JsonDocument doc = JsonDocument.Parse(await Http.GetStringAsync("https://raw.githubusercontent.com/ScoopInstaller/Extras/master/bucket/ddu.json", cts.Token));
			if (doc.RootElement.TryGetProperty("url", out var urlProp))
			{
				string rawUrl = urlProp.GetString();
				if (rawUrl != null && !string.IsNullOrWhiteSpace(rawUrl))
				{
					string cleanUrl = rawUrl.Split('#')[0].Trim();
					if (cleanUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
					{
						urls.Add(cleanUrl);
					}
				}
			}
		}
		catch
		{
		}
		string[] fallbackDownloadUrls = FallbackDownloadUrls;
		foreach (string fallback in fallbackDownloadUrls)
		{
			if (!urls.Contains(fallback, StringComparer.OrdinalIgnoreCase))
			{
				urls.Add(fallback);
			}
		}
		return urls;
	}

	private static void ExtractPackage(string archivePath, string destinationDir)
	{
		bool isZip = false;
		using (FileStream fs = File.OpenRead(archivePath))
		{
			if (fs.Length >= 4)
			{
				int num = fs.ReadByte();
				int b1 = fs.ReadByte();
				isZip = num == 80 && b1 == 75;
			}
		}
		if (isZip)
		{
			ZipFile.ExtractToDirectory(archivePath, destinationDir, overwriteFiles: true);
			return;
		}
		try
		{
			ZipFile.ExtractToDirectory(archivePath, destinationDir, overwriteFiles: true);
		}
		catch (InvalidDataException)
		{
			string sfxExe = archivePath;
			if (!sfxExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
			{
				sfxExe = archivePath + ".exe";
				File.Copy(archivePath, sfxExe, overwrite: true);
			}
			using Process proc = Process.Start(new ProcessStartInfo
			{
				FileName = sfxExe,
				Arguments = "-y -o\"" + destinationDir + "\"",
				WorkingDirectory = destinationDir,
				UseShellExecute = false,
				CreateNoWindow = true
			});
			proc?.WaitForExit(60000);
		}
	}

	private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
	{
		Directory.CreateDirectory(targetDir);
		string[] files = Directory.GetFiles(sourceDir);
		foreach (string file in files)
		{
			string destFile = Path.Combine(targetDir, Path.GetFileName(file));
			File.Copy(file, destFile, overwrite: true);
		}
		files = Directory.GetDirectories(sourceDir);
		foreach (string subDir in files)
		{
			string destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
			CopyDirectoryRecursive(subDir, destSubDir);
		}
	}

	public static async Task<DduExecutionResult> LaunchAsync(bool silentMode, DduGpuVendor vendor = DduGpuVendor.Unknown, IProgress<int>? progress = null, IProgress<string>? statusProgress = null, CancellationToken ct = default(CancellationToken))
	{
		if (!IsInstalled())
		{
			await EnsureInstalledAsync(progress, statusProgress, ct);
		}
		if (silentMode && vendor == DduGpuVendor.Unknown)
		{
			vendor = (await DetectPrimaryGpuAsync()).Vendor;
		}
		string arguments = (silentMode ? GetSilentArguments(vendor) : "");
		return await Task.Run(() =>
		{
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = ExecutablePath,
					Arguments = arguments,
					WorkingDirectory = DduDirectory,
					UseShellExecute = true,
					Verb = "runas"
				});
				return new DduExecutionResult(Started: true, CancelledByUac: false, silentMode ? "Limpeza silenciosa do DDU iniciada com privilégios de Administrador. O computador será reiniciado ao concluir." : "Painel completo do DDU iniciado como Administrador.");
			}
			catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
			{
				return new DduExecutionResult(Started: false, CancelledByUac: true, "Execução cancelada pelo usuário na solicitação de Administrador (UAC).");
			}
		}, ct);
	}
}
