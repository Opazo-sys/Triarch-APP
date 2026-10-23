using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Triarch;

public static class NetworkTest
{
	private sealed record PingStat(string Name, string Address, int Sent, int Lost, double Min, double Avg, double Max, double Jitter)
	{
		public double LossPct
		{
			get
			{
				if (Sent != 0)
				{
					return 100.0 * (double)Lost / (double)Sent;
				}
				return 0.0;
			}
		}

		public bool Answered => Sent - Lost > 0;

		public string Text
		{
			get
			{
				if (Answered)
				{
					return $"média {Avg:0.#} ms · mín {Min:0.#} · máx {Max:0.#} · jitter {Jitter:0.#} ms · perda {LossPct:0.#}%";
				}
				return "não responde";
			}
		}
	}

	private static readonly HttpClient Http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(30.0)
	};

	private static PingStat Stat(string name, string address, List<double> rtts, int sent)
	{
		if (rtts.Count == 0)
		{
			return new PingStat(name, address, sent, sent, 0.0, 0.0, 0.0, 0.0);
		}
		double jit = 0.0;
		for (int i = 1; i < rtts.Count; i++)
		{
			jit += Math.Abs(rtts[i] - rtts[i - 1]);
		}
		jit = ((rtts.Count > 1) ? (jit / (double)(rtts.Count - 1)) : 0.0);
		return new PingStat(name, address, sent, sent - rtts.Count, rtts.Min(), rtts.Average(), rtts.Max(), jit);
	}

	private static async Task<(List<double> Rtts, int Sent)> PingLoop(IPAddress ip, int count, int intervalMs, CancellationToken ct)
	{
		List<double> rtts = new List<double>();
		int sent = 0;
		using Ping p = new Ping();
		for (int i = 0; count <= 0 || i < count; i++)
		{
			if (ct.IsCancellationRequested)
			{
				break;
			}
			Stopwatch sw = Stopwatch.StartNew();
			sent++;
			try
			{
				PingReply r = await p.SendPingAsync(ip, 1500);
				if (r.Status == IPStatus.Success)
				{
					rtts.Add(Math.Max(1L, r.RoundtripTime));
				}
			}
			catch (PingException)
			{
			}
			catch (InvalidOperationException)
			{
			}
			int wait = intervalMs - (int)sw.ElapsedMilliseconds;
			if (wait > 0)
			{
				try
				{
					await Task.Delay(wait, ct);
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
		}
		return (Rtts: rtts, Sent: sent);
	}

	private static async Task<double?> Download(int seconds, CancellationToken ct)
	{
		long total = 0L;
		Stopwatch sw = Stopwatch.StartNew();
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(seconds * 1000);
		await Task.WhenAll((from _ in Enumerable.Range(0, 4)
			select Task.Run(async () =>
			{
				try
				{
					byte[] buf = new byte[81920];
					while (!cts.IsCancellationRequested)
					{
						using HttpResponseMessage resp = await Http.GetAsync("https://speed.cloudflare.com/__down?bytes=25000000", HttpCompletionOption.ResponseHeadersRead, cts.Token);
						resp.EnsureSuccessStatusCode();
						using Stream st = await resp.Content.ReadAsStreamAsync(cts.Token);
						int n;
						while ((n = await st.ReadAsync(buf, cts.Token)) > 0)
						{
							Interlocked.Add(ref total, n);
						}
					}
				}
				catch (OperationCanceledException)
				{
				}
				catch (HttpRequestException)
				{
				}
			})).ToArray());
		ct.ThrowIfCancellationRequested();
		double secs = sw.Elapsed.TotalSeconds;
		return (total > 0 && secs > 0.0) ? new double?((double)(total * 8) / 1000000.0 / secs) : ((double?)null);
	}

	private static async Task<double?> Upload(int seconds, CancellationToken ct)
	{
		long total = 0L;
		Stopwatch sw = Stopwatch.StartNew();
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(seconds * 1000);
		byte[] payload = new byte[1000000];
		new Random(1).NextBytes(payload);
		await Task.WhenAll((from _ in Enumerable.Range(0, 3)
			select Task.Run(async () =>
			{
				try
				{
					while (!cts.IsCancellationRequested)
					{
						using HttpResponseMessage resp = await Http.PostAsync("https://speed.cloudflare.com/__up", new ByteArrayContent(payload), cts.Token);
						resp.EnsureSuccessStatusCode();
						Interlocked.Add(ref total, payload.Length);
					}
				}
				catch (OperationCanceledException)
				{
				}
				catch (HttpRequestException)
				{
				}
			})).ToArray());
		ct.ThrowIfCancellationRequested();
		double secs = sw.Elapsed.TotalSeconds;
		return (total > 0 && secs > 0.0) ? new double?((double)(total * 8) / 1000000.0 / secs) : ((double?)null);
	}

	private static string AdapterType(NetworkInterfaceType t)
	{
		switch (t)
		{
		case NetworkInterfaceType.Wireless80211:
			return "Wi-Fi";
		case NetworkInterfaceType.Ethernet:
		case NetworkInterfaceType.FastEthernetT:
		case NetworkInterfaceType.GigabitEthernet:
			return "Ethernet (cabo)";
		default:
			return t.ToString();
		}
	}

	private static string FormatMac(PhysicalAddress? mac)
	{
		if (mac == null)
		{
			return "—";
		}
		byte[] bytes = mac.GetAddressBytes();
		if (bytes.Length == 0)
		{
			return "—";
		}
		return string.Join(":", bytes.Select((byte b) => b.ToString("X2")));
	}

	public static async Task<List<DiskHealthCard>> Run(Action<string, double> progress, CancellationToken ct)
	{
		progress("Procurando a conexão ativa…", 0.02);
		NetworkInterface? nic = null;
		try
		{
			nic = (from n in NetworkInterface.GetAllNetworkInterfaces().Where((NetworkInterface n) => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel && n.GetIPProperties().GatewayAddresses.Any((GatewayIPAddressInformation g) => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
				orderby n.Speed descending
				select n).FirstOrDefault();
		}
		catch
		{
		}
		if (nic == null)
		{
			return new List<DiskHealthCard>
			{
				new DiskHealthCard
				{
					Title = "Sem conexão",
					Meta = "Nenhum adaptador conectado com gateway",
					Verdict = DiskVerdict.Critical,
					Label = "SEM REDE",
					Reasons = "O Windows não encontrou uma conexão ativa com acesso ao roteador. Verifique o cabo, o Wi-Fi ou o adaptador."
				}
			};
		}
		IPInterfaceProperties props = nic.GetIPProperties();
		IPAddress gw = props.GatewayAddresses.First((GatewayIPAddressInformation g) => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)).Address;
		UnicastIPAddressInformation? uniV4 = props.UnicastAddresses.FirstOrDefault((UnicastIPAddressInformation u) => u.Address.AddressFamily == AddressFamily.InterNetwork);
		string ip = ((uniV4 != null) ? $"{uniV4.Address}/{uniV4.PrefixLength}" : "—");
		string ipv6 = props.UnicastAddresses.FirstOrDefault((UnicastIPAddressInformation u) => u.Address.AddressFamily == AddressFamily.InterNetworkV6 && !u.Address.IsIPv6LinkLocal)?.Address.ToString() ?? "—";
		string dnsList = string.Join(", ", props.DnsAddresses.Where((IPAddress d) => d.AddressFamily == AddressFamily.InterNetwork).Select((IPAddress d) => d.ToString()));
		if (string.IsNullOrEmpty(dnsList))
		{
			dnsList = "—";
		}
		string dhcp = props.DhcpServerAddresses.FirstOrDefault((IPAddress d) => d.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "Estático / Não informado";
		string mac = FormatMac(nic.GetPhysicalAddress());
		string link = ((nic.Speed > 0) ? $"{(double)nic.Speed / 1000000.0:0} Mbps" : "—");
		progress("Medindo latência e perda de pacotes (~ 7 s)…", 0.08);
		PingStat[] pings = await Task.WhenAll(new (string, IPAddress)[3]
		{
			("Roteador / Gateway", gw),
			("Internet · Cloudflare", IPAddress.Parse("1.1.1.1")),
			("Internet · Google", IPAddress.Parse("8.8.8.8"))
		}.Select(async ((string Name, IPAddress Addr) t) =>
		{
			var (rtts, sent) = await PingLoop(t.Addr, 30, 200, ct);
			return Stat(t.Name, t.Addr.ToString(), rtts, sent);
		}));
		ct.ThrowIfCancellationRequested();
		progress("Testando o DNS…", 0.3);
		double? dnsMs = null;
		try
		{
			Stopwatch sw = Stopwatch.StartNew();
			await Dns.GetHostAddressesAsync("www.cloudflare.com", ct);
			dnsMs = sw.Elapsed.TotalMilliseconds;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
		}
		progress("Medindo a velocidade de download (~ 8 s)…", 0.36);
		using CancellationTokenSource loadStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
		Task<(List<double> Rtts, int Sent)> loadedPing = PingLoop(IPAddress.Parse("1.1.1.1"), 0, 200, loadStop.Token);
		double? down = null;
		string? netErr = null;
		try
		{
			down = await Download(8, ct);
		}
		catch (OperationCanceledException)
		{
			loadStop.Cancel();
			throw;
		}
		catch (Exception ex3)
		{
			netErr = ex3.Message;
		}
		loadStop.Cancel();
		List<double> lrtts = (await loadedPing).Item1;
		double? loadedMs = ((lrtts.Count >= 5) ? new double?(lrtts.Average()) : ((double?)null));
		progress("Medindo a velocidade de upload (~ 6 s)…", 0.66);
		double? up = null;
		try
		{
			up = await Upload(6, ct);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex5)
		{
			if (netErr == null)
			{
				netErr = ex5.Message;
			}
		}
		progress("Analisando…", 0.95);
		PingStat gwS = pings[0];
		List<PingStat> net = (from pingStat in pings.Skip(1)
			where pingStat.Answered
			select pingStat).ToList();
		double netAvg = ((net.Count > 0) ? net.Average((PingStat pingStat) => pingStat.Avg) : 0.0);
		double netLoss = pings.Skip(1).Average((PingStat pingStat) => pingStat.LossPct);
		double netJit = ((net.Count > 0) ? net.Max((PingStat pingStat) => pingStat.Jitter) : 0.0);
		List<string> warn = new List<string>();
		bool bad = false;
		bool lanBad = gwS.Answered && (gwS.LossPct > 2.0 || gwS.Avg > 30.0 || gwS.Jitter > 15.0);
		if (lanBad)
		{
			warn.Add($"A rede local está instável (roteador: {gwS.Avg:0} ms, perda {gwS.LossPct:0.#}%, jitter {gwS.Jitter:0} ms). Se for Wi-Fi, aproxime-se do roteador ou use cabo.");
		}
		if (!gwS.Answered)
		{
			warn.Add("O roteador não responde a ping (alguns modelos bloqueiam). Isso por si só não é problema.");
		}
		if (net.Count == 0)
		{
			bad = true;
			warn.Add("Nenhum dos servidores da internet respondeu: sem acesso à internet ou ping bloqueado pela rede.");
		}
		else
		{
			if (netLoss > 5.0 || netAvg > 150.0)
			{
				bad = true;
			}
			if (netLoss > 1.0)
			{
				warn.Add($"Há perda de pacotes até a internet ({netLoss:0.#}%). {(lanBad ? "Provavelmente vem da rede local." : "Como o roteador está bem, o problema deve estar na linha ou no provedor.")}");
			}
			if (netAvg > 80.0)
			{
				warn.Add($"Latência alta até a internet ({netAvg:0} ms).{(lanBad ? "" : " O roteador responde bem, então o atraso vem do provedor.")}");
			}
			if (netJit > 20.0)
			{
				warn.Add($"O jitter está alto ({netJit:0} ms): a latência oscila, o que causa engasgos em jogos online.");
			}
		}
		if (loadedMs.HasValue && net.Count > 0 && loadedMs - net.Min((PingStat pingStat) => pingStat.Avg) > 100.0)
		{
			warn.Add($"A latência sobe para {loadedMs:0} ms quando a conexão está cheia (bufferbloat). Ative SQM/QoS no roteador ou limite a velocidade a ~90%.");
		}
		if (dnsMs > 150.0)
		{
			warn.Add($"O DNS demorou {dnsMs:0} ms. Trocar para 1.1.1.1 ou 8.8.8.8 pode acelerar o carregamento de sites.");
		}
		if (down.HasValue && down.GetValueOrDefault() < 10.0)
		{
			warn.Add($"Download baixo ({down:0.#} Mbps) para jogos, streaming e atualizações.");
		}
		if (netErr != null && !down.HasValue)
		{
			warn.Add("Não foi possível medir a velocidade (speed.cloudflare.com inacessível ou bloqueado).");
		}
		DiskVerdict verdict = (bad ? DiskVerdict.Critical : (warn.Any((string w) => !w.StartsWith("O roteador não responde")) ? DiskVerdict.Warning : DiskVerdict.Good));
		string label = verdict switch
		{
			DiskVerdict.Critical => "RUIM", 
			DiskVerdict.Warning => "ATENÇÃO", 
			_ => "ESTÁVEL", 
		};
		string reasons = ((warn.Count > 0) ? string.Join(" ", warn) : "Latência baixa, sem perda de pacotes e sem sinais de bufferbloat. Se o jogo ainda engasga, o problema provavelmente não é a conexão: veja o Gargalo e as temperaturas.");
		List<KeyValuePair<string, string>> m = new List<KeyValuePair<string, string>>();
		foreach (PingStat p in pings)
		{
			m.Add(new KeyValuePair<string, string>(p.Name + " (" + p.Address + ")", p.Text));
		}
		if (dnsMs.HasValue)
		{
			m.Add(new KeyValuePair<string, string>("Resolução de DNS", $"{dnsMs:0} ms"));
		}
		m.Add(new KeyValuePair<string, string>("Download", down.HasValue ? $"{down:0.#} Mbps" : "—"));
		m.Add(new KeyValuePair<string, string>("Upload", up.HasValue ? $"{up:0.#} Mbps" : "—"));
		if (loadedMs.HasValue)
		{
			m.Add(new KeyValuePair<string, string>("Latência sob carga", $"{loadedMs:0} ms (durante o download)"));
		}
		m.Add(new KeyValuePair<string, string>("Adaptador / Placa", $"{nic.Name} ({nic.Description})"));
		m.Add(new KeyValuePair<string, string>("Tipo / Velocidade do Link", $"{AdapterType(nic.NetworkInterfaceType)} · {link}"));
		m.Add(new KeyValuePair<string, string>("Endereço MAC", mac));
		m.Add(new KeyValuePair<string, string>("IPv4 / Gateway", $"{ip} · Gateway {gw}"));
		m.Add(new KeyValuePair<string, string>("Servidor DHCP / DNS", $"DHCP {dhcp} · DNS {dnsList}"));
		if (ipv6 != "—")
		{
			m.Add(new KeyValuePair<string, string>("IPv6 Global", ipv6));
		}
		progress("Concluído", 1.0);
		return new List<DiskHealthCard>
		{
			new DiskHealthCard
			{
				Title = "Diagnóstico de rede e link",
				Meta = $"{AdapterType(nic.NetworkInterfaceType)} · {link} · testado às {DateTime.Now:HH:mm:ss}",
				Verdict = verdict,
				Label = label,
				Reasons = reasons,
				Metrics = m
			}
		};
	}

	public static async Task<string> TestHostPortAsync(string input, CancellationToken ct = default)
	{
		string raw = input.Trim();
		if (string.IsNullOrEmpty(raw))
		{
			return "Digite um host ou IP (ex.: 1.1.1.1, google.com ou 192.168.1.1:443).";
		}
		string host = raw;
		int? customPort = null;
		int colon = raw.LastIndexOf(':');
		if (colon > 0 && colon < raw.Length - 1 && int.TryParse(raw.Substring(colon + 1), out var pNum) && pNum >= 1 && pNum <= 65535)
		{
			host = raw.Substring(0, colon);
			customPort = pNum;
		}
		StringBuilder sb = new StringBuilder();
		IPAddress? resolvedIp = null;
		Stopwatch dnsSw = Stopwatch.StartNew();
		try
		{
			if (IPAddress.TryParse(host, out var parsed))
			{
				resolvedIp = parsed;
				sb.Append($"IP: {resolvedIp}");
			}
			else
			{
				IPAddress[] addrs = await Dns.GetHostAddressesAsync(host, ct);
				dnsSw.Stop();
				resolvedIp = addrs.FirstOrDefault((IPAddress a) => a.AddressFamily == AddressFamily.InterNetwork) ?? addrs.FirstOrDefault();
				if (resolvedIp == null)
				{
					return $"Não foi possível resolver o DNS de '{host}'.";
				}
				sb.Append($"DNS: {host} -> {resolvedIp} ({dnsSw.ElapsedMilliseconds} ms)");
			}
		}
		catch (Exception ex)
		{
			return $"Falha na resolução DNS de '{host}': {ex.Message}";
		}
		try
		{
			using Ping ping = new Ping();
			PingReply reply = await ping.SendPingAsync(resolvedIp, 2000);
			sb.Append((reply.Status == IPStatus.Success) ? $"  ·  Ping ICMP: {reply.RoundtripTime} ms (TTL {reply.Options?.Ttl})" : $"  ·  Ping ICMP: {reply.Status}");
		}
		catch
		{
			sb.Append("  ·  Ping ICMP: sem resposta");
		}
		int[] ports = (customPort.HasValue ? new int[1] { customPort.Value } : new int[4] { 22, 80, 443, 3389 });
		List<string> portResults = new List<string>();
		foreach (int port in ports)
		{
			Stopwatch tcpSw = Stopwatch.StartNew();
			try
			{
				using TcpClient tcp = new TcpClient();
				using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
				timeoutCts.CancelAfter(1800);
				await tcp.ConnectAsync(resolvedIp, port, timeoutCts.Token);
				tcpSw.Stop();
				portResults.Add($"TCP {port}: ABERTA ({tcpSw.ElapsedMilliseconds} ms)");
			}
			catch
			{
				portResults.Add($"TCP {port}: fechada/filtrada");
			}
		}
		sb.Append("\n" + string.Join("  ·  ", portResults));
		return sb.ToString();
	}

	public static async Task<string> RunNetCmdAsync(string mode)
	{
		string args = mode switch
		{
			"flushdns" => "/c ipconfig /flushdns", 
			"renew" => "/c ipconfig /release && ipconfig /renew", 
			"winsock" => "/c netsh winsock reset && netsh int ip reset", 
			_ => "/c ipconfig /flushdns", 
		};
		return await Task.Run(() =>
		{
			try
			{
				ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", args)
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};
				using Process? p = Process.Start(psi);
				if (p == null)
				{
					return "Não foi possível iniciar o comando de rede.";
				}
				p.WaitForExit(20000);
				return mode switch
				{
					"flushdns" => "Cache DNS limpo com sucesso (ipconfig /flushdns).", 
					"renew" => "Concessão DHCP renovada (ipconfig /release & /renew).", 
					"winsock" => "Catálogo Winsock e pilha TCP/IP redefinidos. Reinicie o PC se necessário.", 
					_ => "Comando executado.", 
				};
			}
			catch (Exception ex)
			{
				return "Erro ao executar comando: " + ex.Message;
			}
		});
	}
}
