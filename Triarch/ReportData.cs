using System;
using System.Collections.Generic;

namespace Triarch;

public sealed class ReportData
{
	public DateTime Date { get; init; } = DateTime.Now;

	public string Machine { get; init; } = Environment.MachineName;

	public string AppVersion { get; init; } = "";

	public string? LogoBase64 { get; init; }

	public List<ReportNode> System { get; init; } = new List<ReportNode>();

	public List<ReportGpu> Gpus { get; init; } = new List<ReportGpu>();

	public List<ReportDisk> Disks { get; init; } = new List<ReportDisk>();

	public List<ReportDisk> Power { get; init; } = new List<ReportDisk>();

	public List<ReportDisk> Network { get; init; } = new List<ReportDisk>();

	public List<ReportSensor> Sensors { get; init; } = new List<ReportSensor>();

	public List<ReportBench> Benchmarks { get; init; } = new List<ReportBench>();
}
