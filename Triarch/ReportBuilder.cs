using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Triarch;

public static class ReportBuilder
{
	private const string Css = "\r\n:root{--bg:#fff;--fg:#111;--muted:#666;--line:#e3e3e3;--card:#fafafa;--ok:#111;--badbg:#111;--badfg:#fff}\r\n@media (prefers-color-scheme:dark){:root{--bg:#000;--fg:#f2f2f2;--muted:#8c8c8c;--line:#222;--card:#0c0c0c;--ok:#f2f2f2;--badbg:#f2f2f2;--badfg:#000}}\r\n*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.5 'Segoe UI',system-ui,sans-serif}\r\n.wrap{max-width:980px;margin:0 auto;padding:32px 20px 60px}\r\nheader{display:flex;gap:16px;align-items:center;margin-bottom:6px}.logo{width:44px;height:44px}\r\nh1{margin:0;font-size:26px;letter-spacing:.5px}h2{margin:34px 0 12px;font-size:17px;letter-spacing:.4px}h3{margin:0 0 8px;font-size:14px}\r\n.sub,.muted{color:var(--muted)}.sub{margin:2px 0 0}.note{color:var(--muted);font-size:12px;margin:0 0 4px}\r\n.card{background:var(--card);border:1px solid var(--line);border-radius:6px;padding:14px 16px;margin:0 0 12px}\r\n.row{display:flex;justify-content:space-between;gap:12px;align-items:center}\r\n.badge{font-size:11px;font-weight:600;letter-spacing:.6px;padding:3px 10px;border-radius:3px;border:1px solid var(--ok);color:var(--ok)}\r\n.badge.bad,.badge.na{background:var(--badbg);color:var(--badfg);border-color:var(--badbg)}.badge.na{opacity:.6}\r\ntable{width:100%;border-collapse:collapse}td,th{padding:5px 8px;border-bottom:1px solid var(--line);vertical-align:top;text-align:left}\r\nth{color:var(--muted);font-weight:600;font-size:12px}.k{color:var(--muted);width:38%}.num{font-family:Consolas,monospace;text-align:right;white-space:nowrap}\r\n.grp{font-weight:600;padding-top:12px}.inner{padding:0 0 0 14px;border-bottom:0}.nested td{font-size:13px}\r\nfooter{margin-top:40px;color:var(--muted);font-size:12px}\r\n@media print{body{background:#fff;color:#000}.card{break-inside:avoid}}\r\n";

	public static ReportNode? ToNode(InfoNode n)
	{
		string h = n.Header ?? "";
		if (h.Contains("série", StringComparison.OrdinalIgnoreCase) || h.Contains("serial", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		List<ReportNode> kids = (from k in n.Children.Select(ToNode)
			where k != null
			select k).Cast<ReportNode>().ToList();
		return new ReportNode(h, string.IsNullOrEmpty(n.Value) ? null : n.Value, (kids.Count > 0) ? kids : null);
	}

	public static string Json(ReportData d)
	{
		return JsonSerializer.Serialize(new
		{
			gerado_em = d.Date.ToString("s"),
			computador = d.Machine,
			versao_do_programa = d.AppVersion,
			sistema = d.System,
			placas_de_video = d.Gpus,
			discos = d.Disks.Select((ReportDisk x) => new
			{
				Name = x.Name,
				Details = x.Details,
				saude = x.Verdict,
				Reasons = x.Reasons,
				metricas = x.Metrics.ToDictionary((KeyValuePair<string, string> m) => m.Key, (KeyValuePair<string, string> m) => m.Value)
			}),
			energia = d.Power.Select((ReportDisk x) => new
			{
				Name = x.Name,
				Details = x.Details,
				estado = x.Verdict,
				Reasons = x.Reasons,
				metricas = x.Metrics.ToDictionary((KeyValuePair<string, string> m) => m.Key, (KeyValuePair<string, string> m) => m.Value)
			}),
			rede = d.Network.Select((ReportDisk x) => new
			{
				Name = x.Name,
				Details = x.Details,
				estado = x.Verdict,
				Reasons = x.Reasons,
				metricas = x.Metrics.ToDictionary((KeyValuePair<string, string> m) => m.Key, (KeyValuePair<string, string> m) => m.Value)
			}),
			sensores = d.Sensors,
			benchmarks = d.Benchmarks
		}, new JsonSerializerOptions
		{
			WriteIndented = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		});
	}

	private static void Section(StringBuilder sb, string title, List<ReportDisk> items)
	{
		if (items.Count == 0)
		{
			return;
		}
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(18, 1, stringBuilder);
		handler.AppendLiteral("<section><h2>");
		handler.AppendFormatted(E(title));
		handler.AppendLiteral("</h2>");
		stringBuilder2.Append(ref handler);
		foreach (ReportDisk disk in items)
		{
			string text;
			switch (disk.Verdict)
			{
			case "SAUDÁVEL":
			case "ESTÁVEL":
			case "EM USO":
				text = "ok";
				break;
			case "SEM DADOS":
				text = "na";
				break;
			default:
				text = "bad";
				break;
			}
			string cls = text;
			stringBuilder = sb;
			StringBuilder stringBuilder3 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(76, 3, stringBuilder);
			handler.AppendLiteral("<div class=\"card\"><div class=\"row\"><b>");
			handler.AppendFormatted(E(disk.Name));
			handler.AppendLiteral("</b><span class=\"badge ");
			handler.AppendFormatted(cls);
			handler.AppendLiteral("\">");
			handler.AppendFormatted(E(disk.Verdict));
			handler.AppendLiteral("</span></div>");
			stringBuilder3.Append(ref handler);
			if (disk.Details.Length > 0)
			{
				stringBuilder = sb;
				StringBuilder stringBuilder4 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(21, 1, stringBuilder);
				handler.AppendLiteral("<p class=\"muted\">");
				handler.AppendFormatted(E(disk.Details));
				handler.AppendLiteral("</p>");
				stringBuilder4.Append(ref handler);
			}
			stringBuilder = sb;
			StringBuilder stringBuilder5 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder);
			handler.AppendLiteral("<p>");
			handler.AppendFormatted(E(disk.Reasons));
			handler.AppendLiteral("</p>");
			stringBuilder5.Append(ref handler);
			if (disk.Metrics.Count > 0)
			{
				sb.Append("<table>");
				foreach (KeyValuePair<string, string> m in disk.Metrics)
				{
					stringBuilder = sb;
					StringBuilder stringBuilder6 = stringBuilder;
					handler = new StringBuilder.AppendInterpolatedStringHandler(37, 2, stringBuilder);
					handler.AppendLiteral("<tr><td class=\"k\">");
					handler.AppendFormatted(E(m.Key));
					handler.AppendLiteral("</td><td>");
					handler.AppendFormatted(E(m.Value));
					handler.AppendLiteral("</td></tr>");
					stringBuilder6.Append(ref handler);
				}
				sb.Append("</table>");
			}
			sb.Append("</div>");
		}
		sb.Append("</section>");
	}

	private static string E(string? s)
	{
		return WebUtility.HtmlEncode(s ?? "");
	}

	public static string Html(ReportData d)
	{
		StringBuilder sb = new StringBuilder(65536);
		sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(83, 2, stringBuilder);
		handler.AppendLiteral("<title>Triarch · Relatório · ");
		handler.AppendFormatted(E(d.Machine));
		handler.AppendLiteral("</title><style>");
		handler.AppendFormatted("\r\n:root{--bg:#fff;--fg:#111;--muted:#666;--line:#e3e3e3;--card:#fafafa;--ok:#111;--badbg:#111;--badfg:#fff}\r\n@media (prefers-color-scheme:dark){:root{--bg:#000;--fg:#f2f2f2;--muted:#8c8c8c;--line:#222;--card:#0c0c0c;--ok:#f2f2f2;--badbg:#f2f2f2;--badfg:#000}}\r\n*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.5 'Segoe UI',system-ui,sans-serif}\r\n.wrap{max-width:980px;margin:0 auto;padding:32px 20px 60px}\r\nheader{display:flex;gap:16px;align-items:center;margin-bottom:6px}.logo{width:44px;height:44px}\r\nh1{margin:0;font-size:26px;letter-spacing:.5px}h2{margin:34px 0 12px;font-size:17px;letter-spacing:.4px}h3{margin:0 0 8px;font-size:14px}\r\n.sub,.muted{color:var(--muted)}.sub{margin:2px 0 0}.note{color:var(--muted);font-size:12px;margin:0 0 4px}\r\n.card{background:var(--card);border:1px solid var(--line);border-radius:6px;padding:14px 16px;margin:0 0 12px}\r\n.row{display:flex;justify-content:space-between;gap:12px;align-items:center}\r\n.badge{font-size:11px;font-weight:600;letter-spacing:.6px;padding:3px 10px;border-radius:3px;border:1px solid var(--ok);color:var(--ok)}\r\n.badge.bad,.badge.na{background:var(--badbg);color:var(--badfg);border-color:var(--badbg)}.badge.na{opacity:.6}\r\ntable{width:100%;border-collapse:collapse}td,th{padding:5px 8px;border-bottom:1px solid var(--line);vertical-align:top;text-align:left}\r\nth{color:var(--muted);font-weight:600;font-size:12px}.k{color:var(--muted);width:38%}.num{font-family:Consolas,monospace;text-align:right;white-space:nowrap}\r\n.grp{font-weight:600;padding-top:12px}.inner{padding:0 0 0 14px;border-bottom:0}.nested td{font-size:13px}\r\nfooter{margin-top:40px;color:var(--muted);font-size:12px}\r\n@media print{body{background:#fff;color:#000}.card{break-inside:avoid}}\r\n");
		handler.AppendLiteral("</style></head><body><div class=\"wrap\">");
		stringBuilder2.Append(ref handler);
		sb.Append("<header>");
		if (!string.IsNullOrEmpty(d.LogoBase64))
		{
			stringBuilder = sb;
			StringBuilder stringBuilder3 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(54, 1, stringBuilder);
			handler.AppendLiteral("<img class=\"logo\" alt=\"\" src=\"data:image/png;base64,");
			handler.AppendFormatted(d.LogoBase64);
			handler.AppendLiteral("\">");
			stringBuilder3.Append(ref handler);
		}
		stringBuilder = sb;
		StringBuilder stringBuilder4 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(63, 2, stringBuilder);
		handler.AppendLiteral("<div><h1>TRIARCH</h1><p class=\"sub\">Relatório de hardware · ");
		handler.AppendFormatted(E(d.Machine));
		handler.AppendLiteral(" · ");
		handler.AppendFormatted(d.Date, "dd/MM/yyyy HH:mm");
		stringBuilder4.Append(ref handler);
		if (d.AppVersion.Length > 0)
		{
			stringBuilder = sb;
			StringBuilder stringBuilder5 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder);
			handler.AppendLiteral(" · v");
			handler.AppendFormatted(E(d.AppVersion));
			stringBuilder5.Append(ref handler);
		}
		sb.Append("</p></div></header>");
		sb.Append("<p class=\"note\">Números de série foram omitidos deste relatório.</p>");
		Section(sb, "Saúde dos discos", d.Disks);
		Section(sb, "Bateria e energia", d.Power);
		Section(sb, "Teste de rede", d.Network);
		if (d.System.Count > 0)
		{
			sb.Append("<section><h2>Sistema</h2>");
			foreach (ReportNode n in d.System)
			{
				stringBuilder = sb;
				StringBuilder stringBuilder6 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(27, 1, stringBuilder);
				handler.AppendLiteral("<div class=\"card\"><h3>");
				handler.AppendFormatted(E(n.Name));
				handler.AppendLiteral("</h3>");
				stringBuilder6.Append(ref handler);
				Tree(sb, n.Children, 0);
				sb.Append("</div>");
			}
			sb.Append("</section>");
		}
		foreach (ReportGpu g in d.Gpus)
		{
			stringBuilder = sb;
			StringBuilder stringBuilder7 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(35, 1, stringBuilder);
			handler.AppendLiteral("<section><h2>Placa de vídeo · ");
			handler.AppendFormatted(E(g.Name));
			handler.AppendLiteral("</h2>");
			stringBuilder7.Append(ref handler);
			if (g.Subtitle.Length > 0)
			{
				stringBuilder = sb;
				StringBuilder stringBuilder8 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(21, 1, stringBuilder);
				handler.AppendLiteral("<p class=\"muted\">");
				handler.AppendFormatted(E(g.Subtitle));
				handler.AppendLiteral("</p>");
				stringBuilder8.Append(ref handler);
			}
			foreach (ReportNode s in g.Sections)
			{
				stringBuilder = sb;
				StringBuilder stringBuilder9 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(27, 1, stringBuilder);
				handler.AppendLiteral("<div class=\"card\"><h3>");
				handler.AppendFormatted(E(s.Name));
				handler.AppendLiteral("</h3>");
				stringBuilder9.Append(ref handler);
				Tree(sb, s.Children, 0);
				sb.Append("</div>");
			}
			sb.Append("</section>");
		}
		if (d.Sensors.Count > 0)
		{
			sb.Append("<section><h2>Sensores (leitura no momento da exportação)</h2>");
			foreach (IGrouping<string, ReportSensor> grp in from reportSensor in d.Sensors
				group reportSensor by reportSensor.Hardware)
			{
				stringBuilder = sb;
				StringBuilder stringBuilder10 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(116, 1, stringBuilder);
				handler.AppendLiteral("<div class=\"card\"><h3>");
				handler.AppendFormatted(E(grp.Key));
				handler.AppendLiteral("</h3><table><tr><th>Categoria</th><th>Sensor</th><th>Valor</th><th>Mín.</th><th>Máx.</th></tr>");
				stringBuilder10.Append(ref handler);
				foreach (ReportSensor s2 in grp)
				{
					stringBuilder = sb;
					StringBuilder stringBuilder11 = stringBuilder;
					handler = new StringBuilder.AppendInterpolatedStringHandler(116, 5, stringBuilder);
					handler.AppendLiteral("<tr><td class=\"muted\">");
					handler.AppendFormatted(E(s2.Category));
					handler.AppendLiteral("</td><td>");
					handler.AppendFormatted(E(s2.Name));
					handler.AppendLiteral("</td><td class=\"num\">");
					handler.AppendFormatted(E(s2.Value));
					handler.AppendLiteral("</td><td class=\"num muted\">");
					handler.AppendFormatted(E(s2.Min));
					handler.AppendLiteral("</td><td class=\"num muted\">");
					handler.AppendFormatted(E(s2.Max));
					handler.AppendLiteral("</td></tr>");
					stringBuilder11.Append(ref handler);
				}
				sb.Append("</table></div>");
			}
			sb.Append("</section>");
		}
		if (d.Benchmarks.Count > 0)
		{
			sb.Append("<section><h2>Benchmarks e testes recentes</h2><div class=\"card\"><table><tr><th>Data</th><th>Teste</th><th>Resultado</th></tr>");
			foreach (ReportBench b in d.Benchmarks)
			{
				stringBuilder = sb;
				StringBuilder stringBuilder12 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(50, 3, stringBuilder);
				handler.AppendLiteral("<tr><td class=\"muted\">");
				handler.AppendFormatted(b.Date, "dd/MM/yyyy HH:mm");
				handler.AppendLiteral("</td><td>");
				handler.AppendFormatted(E(b.Test));
				handler.AppendLiteral("</td><td>");
				handler.AppendFormatted(E(b.Summary));
				handler.AppendLiteral("</td></tr>");
				stringBuilder12.Append(ref handler);
			}
			sb.Append("</table></div></section>");
		}
		sb.Append("<footer>Gerado pelo Triarch. Dados lidos do Windows, do driver da placa de vídeo e dos sensores da placa-mãe; podem variar conforme o hardware.</footer>");
		sb.Append("</div></body></html>");
		return sb.ToString();
	}

	private static void Tree(StringBuilder sb, List<ReportNode>? nodes, int depth)
	{
		if (nodes == null || nodes.Count == 0)
		{
			return;
		}
		sb.Append((depth == 0) ? "<table>" : "<table class=\"nested\">");
		foreach (ReportNode n in nodes)
		{
			if (n.Children != null)
			{
				StringBuilder stringBuilder = sb;
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(76, 2, stringBuilder);
				handler.AppendLiteral("<tr><td colspan=\"2\" class=\"grp\">");
				handler.AppendFormatted(E(n.Name));
				handler.AppendFormatted((n.Value != null) ? (" · " + E(n.Value)) : "");
				handler.AppendLiteral("</td></tr><tr><td colspan=\"2\" class=\"inner\">");
				stringBuilder2.Append(ref handler);
				Tree(sb, n.Children, depth + 1);
				sb.Append("</td></tr>");
			}
			else
			{
				StringBuilder stringBuilder = sb;
				StringBuilder stringBuilder3 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(37, 2, stringBuilder);
				handler.AppendLiteral("<tr><td class=\"k\">");
				handler.AppendFormatted(E(n.Name));
				handler.AppendLiteral("</td><td>");
				handler.AppendFormatted(E(n.Value));
				handler.AppendLiteral("</td></tr>");
				stringBuilder3.Append(ref handler);
			}
		}
		sb.Append("</table>");
	}
}
