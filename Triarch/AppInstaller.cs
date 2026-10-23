using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Triarch;

public static class AppInstaller
{
	public static readonly IReadOnlyList<InstallableApp> CatalogList = new InstallableApp[41]
	{
		new InstallableApp
		{
			Id = "Google.Chrome",
			Name = "Google Chrome",
			Category = "Navegador",
			Description = "Navegador do Google",
			Publisher = "Google",
			WingetName = "Google Chrome",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=chrome.google.com",
			RegistryHints = new string[1] { "Google Chrome" },
			PathHints = new string[2] { "%ProgramFiles%\\Google\\Chrome\\Application\\chrome.exe", "%LocalAppData%\\Google\\Chrome\\Application\\chrome.exe" }
		},
		new InstallableApp
		{
			Id = "Mozilla.Firefox",
			Name = "Mozilla Firefox",
			Category = "Navegador",
			Description = "Navegador aberto e leve",
			Publisher = "Mozilla",
			WingetName = "Mozilla Firefox",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=firefox.com",
			RegistryHints = new string[1] { "Mozilla Firefox" },
			PathHints = new string[1] { "%ProgramFiles%\\Mozilla Firefox\\firefox.exe" }
		},
		new InstallableApp
		{
			Id = "Brave.Brave",
			Name = "Brave",
			Category = "Navegador",
			Description = "Navegador com bloqueador de anúncios integrado",
			Publisher = "Brave",
			WingetName = "Brave",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=brave.com",
			RegistryHints = new string[1] { "Brave" },
			PathHints = new string[2] { "%ProgramFiles%\\BraveSoftware\\Brave-Browser\\Application\\brave.exe", "%LocalAppData%\\BraveSoftware\\Brave-Browser\\Application\\brave.exe" }
		},
		new InstallableApp
		{
			Id = "Opera.OperaGX",
			Name = "Opera GX",
			Category = "Navegador",
			Description = "Navegador voltado para jogos com limitadores de CPU e RAM",
			Publisher = "Opera",
			WingetName = "Opera GX",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=opera.com",
			RegistryHints = new string[1] { "Opera GX" },
			PathHints = new string[1] { "%LocalAppData%\\Programs\\Opera GX\\opera.exe" }
		},
		new InstallableApp
		{
			Id = "Discord.Discord",
			Name = "Discord",
			Category = "Comunicação",
			Description = "Chat de voz, vídeo e texto para jogos",
			Publisher = "Discord",
			WingetName = "Discord",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=discord.com",
			RegistryHints = new string[1] { "Discord" },
			PathHints = new string[1] { "%LocalAppData%\\Discord\\Update.exe" }
		},
		new InstallableApp
		{
			Id = "Telegram.TelegramDesktop",
			Name = "Telegram",
			Category = "Comunicação",
			Description = "Mensageiro rápido para desktop",
			Publisher = "Telegram",
			WingetName = "Telegram Desktop",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=telegram.org",
			RegistryHints = new string[1] { "Telegram Desktop" },
			PathHints = new string[1] { "%AppData%\\Telegram Desktop\\Telegram.exe" }
		},
		new InstallableApp
		{
			Id = "9NKSQGP7F2NH",
			Name = "WhatsApp",
			Category = "Comunicação",
			Description = "Aplicativo oficial do WhatsApp (Microsoft Store)",
			Publisher = "Meta",
			WingetName = "WhatsApp",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=whatsapp.com",
			RegistryHints = new string[1] { "WhatsApp" }
		},
		new InstallableApp
		{
			Id = "Valve.Steam",
			Name = "Steam",
			Category = "Jogos",
			Description = "Plataforma e loja de jogos para PC",
			Publisher = "Valve",
			WingetName = "Steam",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=store.steampowered.com",
			RegistryHints = new string[1] { "Steam" },
			PathHints = new string[2] { "%ProgramFiles(x86)%\\Steam\\steam.exe", "C:\\Steam\\steam.exe" }
		},
		new InstallableApp
		{
			Id = "EpicGames.EpicGamesLauncher",
			Name = "Epic Games Launcher",
			Category = "Jogos",
			Description = "Loja Epic Games, Fortnite e Unreal Engine",
			Publisher = "Epic Games",
			WingetName = "Epic Games Launcher",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=epicgames.com",
			RegistryHints = new string[1] { "Epic Games Launcher" },
			PathHints = new string[1] { "%ProgramFiles(x86)%\\Epic Games\\Launcher\\Portal\\Binaries\\Win64\\EpicGamesLauncher.exe" }
		},
		new InstallableApp
		{
			Id = "ElectronicArts.EADesktop",
			Name = "EA App",
			Category = "Jogos",
			Description = "Plataforma de jogos da Electronic Arts",
			Publisher = "EA",
			WingetName = "EA app",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=ea.com",
			RegistryHints = new string[1] { "EA app" },
			PathHints = new string[1] { "%ProgramFiles%\\Electronic Arts\\EA Desktop\\EA Desktop\\EADesktop.exe" }
		},
		new InstallableApp
		{
			Id = "Ubisoft.Connect",
			Name = "Ubisoft Connect",
			Category = "Jogos",
			Description = "Plataforma de jogos da Ubisoft",
			Publisher = "Ubisoft",
			WingetName = "Ubisoft Connect",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=ubisoft.com",
			RegistryHints = new string[1] { "Ubisoft Connect" },
			PathHints = new string[1] { "%ProgramFiles(x86)%\\Ubisoft\\Ubisoft Game Launcher\\UbisoftConnect.exe" }
		},
		new InstallableApp
		{
			Id = "GOG.Galaxy",
			Name = "GOG Galaxy",
			Category = "Jogos",
			Description = "Jogos sem DRM da GOG",
			Publisher = "GOG",
			WingetName = "GOG GALAXY",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=gog.com",
			RegistryHints = new string[1] { "GOG GALAXY" },
			PathHints = new string[1] { "%ProgramFiles(x86)%\\GOG Galaxy\\GalaxyClient.exe" }
		},
		new InstallableApp
		{
			Id = "PuTTY.PuTTY",
			Name = "PuTTY",
			Category = "Infra & Redes",
			Description = "Cliente SSH, Telnet e Serial clássico para administração de servidores",
			Publisher = "Simon Tatham",
			WingetName = "PuTTY",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=putty.org",
			RegistryHints = new string[1] { "PuTTY" },
			PathHints = new string[2] { "%ProgramFiles%\\PuTTY\\putty.exe", "%ProgramFiles(x86)%\\PuTTY\\putty.exe" }
		},
		new InstallableApp
		{
			Id = "WinSCP.WinSCP",
			Name = "WinSCP",
			Category = "Infra & Redes",
			Description = "Cliente SFTP, SCP, FTP e WebDAV para transferência segura com servidores",
			Publisher = "Martin Prikryl",
			WingetName = "WinSCP",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=winscp.net",
			RegistryHints = new string[1] { "WinSCP" },
			PathHints = new string[2] { "%ProgramFiles(x86)%\\WinSCP\\WinSCP.exe", "%LocalAppData%\\Programs\\WinSCP\\WinSCP.exe" }
		},
		new InstallableApp
		{
			Id = "WiresharkFoundation.Wireshark",
			Name = "Wireshark",
			Category = "Infra & Redes",
			Description = "Analisador de protocolos de rede e captura de pacotes em tempo real",
			Publisher = "Wireshark Foundation",
			WingetName = "Wireshark",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=wireshark.org",
			RegistryHints = new string[1] { "Wireshark" },
			PathHints = new string[1] { "%ProgramFiles%\\Wireshark\\Wireshark.exe" }
		},
		new InstallableApp
		{
			Id = "Insecure.Nmap",
			Name = "Nmap",
			Category = "Infra & Redes",
			Description = "Scanner de portas, descoberta de hosts e auditoria de segurança de rede",
			Publisher = "Nmap Project",
			WingetName = "Nmap",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=nmap.org",
			RegistryHints = new string[1] { "Nmap" },
			PathHints = new string[2] { "%ProgramFiles(x86)%\\Nmap\\nmap.exe", "%ProgramFiles%\\Nmap\\nmap.exe" }
		},
		new InstallableApp
		{
			Id = "Famatech.AdvancedIPScanner",
			Name = "Advanced IP Scanner",
			Category = "Infra & Redes",
			Description = "Scanner rápido de rede local (LAN) com detecção de MAC, fabricante e compartilhamentos",
			Publisher = "Famatech",
			WingetName = "Advanced IP Scanner",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=advanced-ip-scanner.com",
			RegistryHints = new string[1] { "Advanced IP Scanner" },
			PathHints = new string[1] { "%ProgramFiles(x86)%\\Advanced IP Scanner\\advanced_ip_scanner.exe" }
		},
		new InstallableApp
		{
			Id = "RustDesk.RustDesk",
			Name = "RustDesk",
			Category = "Infra & Redes",
			Description = "Acesso remoto open-source rápido e seguro para suporte e infraestrutura",
			Publisher = "RustDesk",
			WingetName = "RustDesk",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=rustdesk.com",
			RegistryHints = new string[1] { "RustDesk" },
			PathHints = new string[1] { "%ProgramFiles%\\RustDesk\\rustdesk.exe" }
		},
		new InstallableApp
		{
			Id = "AnyDeskSoftwareGmbH.AnyDesk",
			Name = "AnyDesk",
			Category = "Infra & Redes",
			Description = "Software de acesso e suporte remoto de baixa latência",
			Publisher = "AnyDesk",
			WingetName = "AnyDesk",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=anydesk.com",
			RegistryHints = new string[1] { "AnyDesk" },
			PathHints = new string[2] { "%ProgramFiles(x86)%\\AnyDesk\\AnyDesk.exe", "%ProgramFiles%\\AnyDesk\\AnyDesk.exe" }
		},
		new InstallableApp
		{
			Id = "OpenVPNTechnologies.OpenVPNConnect",
			Name = "OpenVPN Connect",
			Category = "Infra & Redes",
			Description = "Cliente oficial para conexões VPN corporativas OpenVPN",
			Publisher = "OpenVPN",
			WingetName = "OpenVPN Connect",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=openvpn.net",
			RegistryHints = new string[1] { "OpenVPN Connect" },
			PathHints = new string[1] { "%ProgramFiles%\\OpenVPN Connect\\OpenVPNConnect.exe" }
		},
		new InstallableApp
		{
			Id = "Tailscale.Tailscale",
			Name = "Tailscale",
			Category = "Infra & Redes",
			Description = "Rede mesh VPN baseada em WireGuard para acesso seguro a servidores e laboratórios",
			Publisher = "Tailscale",
			WingetName = "Tailscale",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=tailscale.com",
			RegistryHints = new string[1] { "Tailscale" },
			PathHints = new string[1] { "%ProgramFiles%\\Tailscale\\tailscale-ipn.exe" }
		},
		new InstallableApp
		{
			Id = "Notepad++.Notepad++",
			Name = "Notepad++",
			Category = "SysAdmin & Dev",
			Description = "Editor de texto e código leve, essencial para logs, scripts e configurações",
			Publisher = "Don Ho",
			WingetName = "Notepad++",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=notepad-plus-plus.org",
			RegistryHints = new string[1] { "Notepad++" },
			PathHints = new string[2] { "%ProgramFiles%\\Notepad++\\notepad++.exe", "%ProgramFiles(x86)%\\Notepad++\\notepad++.exe" }
		},
		new InstallableApp
		{
			Id = "Microsoft.VisualStudioCode",
			Name = "Visual Studio Code",
			Category = "SysAdmin & Dev",
			Description = "Editor de código com suporte a SSH remoto, PowerShell, YAML, Docker e Git",
			Publisher = "Microsoft",
			WingetName = "Microsoft Visual Studio Code",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=code.visualstudio.com",
			RegistryHints = new string[1] { "Visual Studio Code" },
			PathHints = new string[2] { "%LocalAppData%\\Programs\\Microsoft VS Code\\Code.exe", "%ProgramFiles%\\Microsoft VS Code\\Code.exe" }
		},
		new InstallableApp
		{
			Id = "Git.Git",
			Name = "Git + Git Bash",
			Category = "SysAdmin & Dev",
			Description = "Controle de versão Git, cliente SSH e terminal Bash para Windows",
			Publisher = "Git",
			WingetName = "Git",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=git-scm.com",
			RegistryHints = new string[1] { "Git" },
			PathHints = new string[1] { "%ProgramFiles%\\Git\\cmd\\git.exe" }
		},
		new InstallableApp
		{
			Id = "Microsoft.PowerShell",
			Name = "PowerShell 7",
			Category = "SysAdmin & Dev",
			Description = "Shell de automação moderno da Microsoft para administração de sistemas e nuvem",
			Publisher = "Microsoft",
			WingetName = "PowerShell",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=learn.microsoft.com",
			RegistryHints = new string[1] { "PowerShell 7" },
			PathHints = new string[1] { "%ProgramFiles%\\PowerShell\\7\\pwsh.exe" }
		},
		new InstallableApp
		{
			Id = "Microsoft.Sysinternals.Suite",
			Name = "Sysinternals Suite",
			Category = "SysAdmin & Dev",
			Description = "Pacote oficial Microsoft (Process Explorer, Procmon, Autoruns, TCPView, PsExec)",
			Publisher = "Microsoft",
			WingetName = "Sysinternals Suite",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=learn.microsoft.com",
			RegistryHints = new string[1] { "Sysinternals" }
		},
		new InstallableApp
		{
			Id = "Microsoft.PowerToys",
			Name = "Microsoft PowerToys",
			Category = "SysAdmin & Dev",
			Description = "Utilitários avançados para Windows (FancyZones, File Locksmith, Hosts Editor, Run)",
			Publisher = "Microsoft",
			WingetName = "PowerToys",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=microsoft.com",
			RegistryHints = new string[1] { "PowerToys" },
			PathHints = new string[2] { "%LocalAppData%\\PowerToys\\PowerToys.exe", "%ProgramFiles%\\PowerToys\\PowerToys.exe" }
		},
		new InstallableApp
		{
			Id = "CPUID.CPU-Z",
			Name = "CPU-Z",
			Category = "SysAdmin & Dev",
			Description = "Identificação detalhada de processador, placa-mãe, chipset e timings de memória RAM",
			Publisher = "CPUID",
			WingetName = "CPUID CPU-Z",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=cpuid.com",
			RegistryHints = new string[1] { "CPU-Z" },
			PathHints = new string[1] { "%ProgramFiles%\\CPUID\\CPU-Z\\cpuz.exe" }
		},
		new InstallableApp
		{
			Id = "CPUID.HWMonitor",
			Name = "HWMonitor",
			Category = "SysAdmin & Dev",
			Description = "Monitor clássico de tensões, temperaturas e ventoinhas da CPUID",
			Publisher = "CPUID",
			WingetName = "CPUID HWMonitor",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=cpuid.com",
			RegistryHints = new string[1] { "HWMonitor" },
			PathHints = new string[1] { "%ProgramFiles%\\CPUID\\HWMonitor\\HWMonitor.exe" }
		},
		new InstallableApp
		{
			Id = "CrystalDewWorld.CrystalDiskInfo",
			Name = "CrystalDiskInfo",
			Category = "SysAdmin & Dev",
			Description = "Diagnóstico detalhado S.M.A.R.T. para SSDs NVMe, SATA e HDs",
			Publisher = "Crystal Dew World",
			WingetName = "CrystalDiskInfo",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=crystalmark.info",
			RegistryHints = new string[1] { "CrystalDiskInfo" },
			PathHints = new string[1] { "%ProgramFiles%\\CrystalDiskInfo\\DiskInfo64.exe" }
		},
		new InstallableApp
		{
			Id = "Spotify.Spotify",
			Name = "Spotify",
			Category = "Mídia",
			Description = "Streaming de música e podcasts",
			Publisher = "Spotify",
			WingetName = "Spotify",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=spotify.com",
			RegistryHints = new string[1] { "Spotify" },
			PathHints = new string[1] { "%AppData%\\Spotify\\Spotify.exe" }
		},
		new InstallableApp
		{
			Id = "VideoLAN.VLC",
			Name = "VLC Media Player",
			Category = "Mídia",
			Description = "Reprodutor de vídeo e áudio compatível com qualquer formato",
			Publisher = "VideoLAN",
			WingetName = "VLC media player",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=videolan.org",
			RegistryHints = new string[1] { "VLC media player" },
			PathHints = new string[1] { "%ProgramFiles%\\VideoLAN\\VLC\\vlc.exe" }
		},
		new InstallableApp
		{
			Id = "OBSProject.OBSStudio",
			Name = "OBS Studio",
			Category = "Mídia",
			Description = "Gravação de tela e transmissão ao vivo",
			Publisher = "OBS Project",
			WingetName = "OBS Studio",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=obsproject.com",
			RegistryHints = new string[1] { "OBS Studio" },
			PathHints = new string[1] { "%ProgramFiles%\\obs-studio\\bin\\64bit\\obs64.exe" }
		},
		new InstallableApp
		{
			Id = "HandBrake.HandBrake",
			Name = "HandBrake",
			Category = "Mídia",
			Description = "Conversor e compressor de vídeos",
			Publisher = "HandBrake",
			WingetName = "HandBrake",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=handbrake.fr",
			RegistryHints = new string[1] { "HandBrake" },
			PathHints = new string[1] { "%ProgramFiles%\\HandBrake\\HandBrake.exe" }
		},
		new InstallableApp
		{
			Id = "7zip.7zip",
			Name = "7-Zip",
			Category = "Utilitários",
			Description = "Compactador e descompactador (.7z, .zip, .rar)",
			Publisher = "Igor Pavlov",
			WingetName = "7-Zip",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=7-zip.org",
			RegistryHints = new string[1] { "7-Zip" },
			PathHints = new string[1] { "%ProgramFiles%\\7-Zip\\7zFM.exe" }
		},
		new InstallableApp
		{
			Id = "RARLab.WinRAR",
			Name = "WinRAR",
			Category = "Utilitários",
			Description = "Compactador clássico para arquivos .rar e .zip",
			Publisher = "win.rar GmbH",
			WingetName = "WinRAR",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=win-rar.com",
			RegistryHints = new string[1] { "WinRAR" },
			PathHints = new string[1] { "%ProgramFiles%\\WinRAR\\WinRAR.exe" }
		},
		new InstallableApp
		{
			Id = "voidtools.Everything",
			Name = "Everything",
			Category = "Utilitários",
			Description = "Busca instantânea de arquivos no Windows",
			Publisher = "voidtools",
			WingetName = "Everything",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=voidtools.com",
			RegistryHints = new string[1] { "Everything" },
			PathHints = new string[1] { "%ProgramFiles%\\Everything\\Everything.exe" }
		},
		new InstallableApp
		{
			Id = "ShareX.ShareX",
			Name = "ShareX",
			Category = "Utilitários",
			Description = "Captura de tela avançada e gravação de GIF/vídeo",
			Publisher = "ShareX",
			WingetName = "ShareX",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=getsharex.com",
			RegistryHints = new string[1] { "ShareX" },
			PathHints = new string[1] { "%ProgramFiles%\\ShareX\\ShareX.exe" }
		},
		new InstallableApp
		{
			Id = "qBittorrent.qBittorrent",
			Name = "qBittorrent",
			Category = "Utilitários",
			Description = "Cliente de torrent leve e sem anúncios",
			Publisher = "qBittorrent",
			WingetName = "qBittorrent",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=qbittorrent.org",
			RegistryHints = new string[1] { "qBittorrent" },
			PathHints = new string[1] { "%ProgramFiles%\\qBittorrent\\qbittorrent.exe" }
		},
		new InstallableApp
		{
			Id = "Guru3D.Afterburner",
			Name = "MSI Afterburner",
			Category = "Utilitários",
			Description = "Controle de ventoinhas, curva de tensão e monitoramento da GPU",
			Publisher = "MSI / Guru3D",
			WingetName = "MSI Afterburner",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=msi.com",
			RegistryHints = new string[1] { "MSI Afterburner" },
			PathHints = new string[1] { "%ProgramFiles(x86)%\\MSI Afterburner\\MSIAfterburner.exe" }
		},
		new InstallableApp
		{
			Id = "Malwarebytes.Malwarebytes",
			Name = "Malwarebytes",
			Category = "Utilitários",
			Description = "Verificador e removedor de malware",
			Publisher = "Malwarebytes",
			WingetName = "Malwarebytes",
			IconUrl = "https://www.google.com/s2/favicons?sz=64&domain=malwarebytes.com",
			RegistryHints = new string[1] { "Malwarebytes" },
			PathHints = new string[1] { "%ProgramFiles%\\Malwarebytes\\Anti-Malware\\mbam.exe" }
		}
	};

	public static List<InstallableApp> Catalog() => CatalogList.ToList();

	public static bool WingetAvailable => FindWinget() != null;

	public static void RefreshInstalled(IEnumerable<InstallableApp> apps)
	{
		HashSet<string> regNames = ReadUninstallNames();
		foreach (InstallableApp a in apps)
		{
			bool found = a.PathHints.Any((string ph) => File.Exists(Environment.ExpandEnvironmentVariables(ph))) || a.RegistryHints.Any((string rh) => regNames.Any((string n) => n.Contains(rh, StringComparison.OrdinalIgnoreCase)));
			a.Installed = found;
			a.Status = (found ? "Já instalado neste PC." : "");
			if (found)
			{
				a.Selected = false;
			}
		}
	}

	public static async Task<(bool Ok, string Message)> InstallAsync(InstallableApp app, CancellationToken ct)
	{
		var (ok, outText) = await InstallOneAsync(app, delegate { }, ct);
		if (ok)
		{
			return (Ok: true, Message: "Instalado com sucesso.");
		}
		string lastLine = outText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "erro desconhecido";
		return (Ok: false, Message: "Falhou: " + lastLine);
	}

	public static string? FindWinget()
	{
		string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft\\WindowsApps\\winget.exe");
		if (!File.Exists(local))
		{
			return null;
		}
		return local;
	}

	public static async Task RefreshInstalledAsync(IEnumerable<InstallableApp> apps)
	{
		List<InstallableApp> list = apps.ToList();
		HashSet<string> regNames = await Task.Run((Func<HashSet<string>>)ReadUninstallNames);
		string winget = FindWinget();
		string wingetOut = "";
		if (winget != null)
		{
			try
			{
				ProcessStartInfo psi = new ProcessStartInfo(winget, "list --disable-interactivity")
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					StandardOutputEncoding = Encoding.UTF8
				};
				using Process p = Process.Start(psi);
				if (p != null)
				{
					using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(12.0));
					wingetOut = await p.StandardOutput.ReadToEndAsync(cts.Token);
				}
			}
			catch
			{
			}
		}
		foreach (InstallableApp a in list)
		{
			bool found = a.PathHints.Any((string ph) => File.Exists(Environment.ExpandEnvironmentVariables(ph))) || a.RegistryHints.Any((string rh) => regNames.Any((string n) => n.Contains(rh, StringComparison.OrdinalIgnoreCase))) || (wingetOut.Length > 0 && wingetOut.Contains(a.Id, StringComparison.OrdinalIgnoreCase));
			a.Installed = found;
			a.Status = (found ? "Instalado" : "");
			if (found)
			{
				a.Selected = false;
			}
		}
	}

	private static HashSet<string> ReadUninstallNames()
	{
		HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		(RegistryHive, RegistryView)[] array = new(RegistryHive, RegistryView)[3]
		{
			(RegistryHive.CurrentUser, RegistryView.Default),
			(RegistryHive.LocalMachine, RegistryView.Registry64),
			(RegistryHive.LocalMachine, RegistryView.Registry32)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(RegistryHive, RegistryView) tuple = array[i];
			RegistryHive hive = tuple.Item1;
			RegistryView view = tuple.Item2;
			try
			{
				using RegistryKey root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall");
				if (root == null)
				{
					continue;
				}
				string[] subKeyNames = root.GetSubKeyNames();
				foreach (string sub in subKeyNames)
				{
					using RegistryKey k = root.OpenSubKey(sub);
					string name = k?.GetValue("DisplayName")?.ToString();
					if (!string.IsNullOrWhiteSpace(name))
					{
						set.Add(name);
					}
				}
			}
			catch
			{
			}
		}
		return set;
	}

	public static async Task<(bool Ok, string Output)> InstallOneAsync(InstallableApp app, Action<string> onLine, CancellationToken ct)
	{
		string winget = FindWinget() ?? throw new InvalidOperationException("O winget não foi encontrado neste PC.");
		ProcessStartInfo psi = new ProcessStartInfo(winget, "install --id \"" + app.Id + "\" -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity")
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		StringBuilder sb = new StringBuilder();
		using Process p = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o winget.");
		Task readOut = PumpAsync(p.StandardOutput, sb, onLine, ct);
		Task readErr = PumpAsync(p.StandardError, sb, onLine, ct);
		try
		{
			await p.WaitForExitAsync(ct);
		}
		catch (OperationCanceledException)
		{
			try
			{
				p.Kill(entireProcessTree: true);
			}
			catch
			{
			}
			throw;
		}
		await Task.WhenAll(readOut, readErr);
		string all = sb.ToString();
		bool already = p.ExitCode == -1978335189 || all.Contains("already installed", StringComparison.OrdinalIgnoreCase) || all.Contains("já está instalado", StringComparison.OrdinalIgnoreCase) || all.Contains("No available upgrade", StringComparison.OrdinalIgnoreCase) || all.Contains("Nenhuma atualização disponível", StringComparison.OrdinalIgnoreCase);
		return (Ok: p.ExitCode == 0 || already, Output: all);
	}

	private static async Task PumpAsync(StreamReader r, StringBuilder sb, Action<string> onLine, CancellationToken ct)
	{
		try
		{
			while (true)
			{
				string text = await r.ReadLineAsync(ct);
				if (text == null)
				{
					break;
				}
				string clean = text.Trim();
				if (clean.Length > 1 && !clean.All((char c) => c == '-' || c == '\\' || c == '|' || c == '/' || c == '█' || c == '▒'))
				{
					lock (sb)
					{
						sb.AppendLine(clean);
					}
					onLine(clean);
				}
			}
		}
		catch
		{
		}
	}
}
