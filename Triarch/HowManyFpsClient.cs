using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Triarch;

public sealed class HowManyFpsClient
{
	public sealed record Result(double AvgFps, double Min1Fps);

	private sealed record CacheEntry(double AvgFps, double Min1Fps, DateTime Fetched);

	private sealed class Response
	{
		[JsonPropertyName("avgFps")]
		public double? AvgFps { get; set; }

		[JsonPropertyName("min1Fps")]
		public double? Min1Fps { get; set; }
	}

	private const string Endpoint = "https://howmanyfps.com/api";

	private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7.0);

	private readonly HttpClient _http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(15.0)
	};

	private readonly Dictionary<string, CacheEntry> _cache;

	private static string CachePath => Path.Combine(AppSettings.Folder, "fps-cache.json");

	public string? ApiKey { get; set; }

	public bool Configured => !string.IsNullOrWhiteSpace(ApiKey);

	public HowManyFpsClient()
	{
		try
		{
			_cache = (File.Exists(CachePath) ? (JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(CachePath)) ?? new Dictionary<string, CacheEntry>()) : new Dictionary<string, CacheEntry>());
		}
		catch
		{
			_cache = new Dictionary<string, CacheEntry>();
		}
	}

	public async Task<Result> GetAsync(string cpu, string gpu, string game, string resolution, string upscaling, CancellationToken ct = default(CancellationToken))
	{
		string key = $"{cpu}|{gpu}|{game}|{resolution}|{upscaling}";
		if (_cache.TryGetValue(key, out CacheEntry c) && DateTime.UtcNow - c.Fetched < CacheTtl)
		{
			return new Result(c.AvgFps, c.Min1Fps);
		}
		if (!Configured)
		{
			throw new InvalidOperationException("Chave da API não configurada.");
		}
		using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "https://howmanyfps.com/api")
		{
			Content = JsonContent.Create(new { cpu, gpu, game, resolution, upscaling })
		};
		req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + ApiKey);
		req.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
		using HttpResponseMessage resp = await _http.SendAsync(req, ct);
		if (!resp.IsSuccessStatusCode)
		{
			string message;
			switch ((int)resp.StatusCode)
			{
			case 401:
			case 403:
				message = "chave recusada pela API";
				break;
			case 404:
				message = "jogo ou hardware não encontrado na base";
				break;
			case 429:
				message = "cota de requisições esgotada";
				break;
			default:
				message = $"HTTP {(int)resp.StatusCode}";
				break;
			}
			throw new HttpRequestException(message);
		}
		Response data = (await resp.Content.ReadFromJsonAsync<Response>(ct)) ?? throw new HttpRequestException("resposta vazia");
		double avg = data.AvgFps ?? throw new HttpRequestException("resposta sem avgFps");
		Result result = new Result(avg, data.Min1Fps.GetValueOrDefault());
		_cache[key] = new CacheEntry(result.AvgFps, result.Min1Fps, DateTime.UtcNow);
		SaveCache();
		return result;
	}

	private void SaveCache()
	{
		try
		{
			Directory.CreateDirectory(AppSettings.Folder);
			File.WriteAllText(CachePath, JsonSerializer.Serialize(_cache));
		}
		catch
		{
		}
	}
}
