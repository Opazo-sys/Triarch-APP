using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Triarch;

public sealed class DriveCard
{
	public string Letter { get; init; } = "";

	public string Label { get; init; } = "";

	public string Kind { get; init; } = "";

	public string FileSystem { get; init; } = "";

	public string Disk { get; init; } = "";

	public long Total { get; init; }

	public long Free { get; init; }

	public bool Removable { get; init; }

	public double UsedFraction
	{
		get
		{
			if (Total <= 0)
			{
				return 0.0;
			}
			return (double)(Total - Free) / (double)Total;
		}
	}

	public string UsedPercent => $"{UsedFraction:P0}";

	public string Title
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Label))
			{
				return Letter + "  " + Label;
			}
			return Letter;
		}
	}

	public string Sizes => $"{Fmt(Total - Free)} usados · {Fmt(Free)} livres de {Fmt(Total)}";

	public string Meta => string.Join(" · ", new string[3] { Kind, FileSystem, Disk }.Where((string s) => !string.IsNullOrEmpty(s)));

	public Geometry UsedArc => Arc(UsedFraction, 42.0, 42.0, 36.0);

	public static string Fmt(long b)
	{
		double v = b;
		string[] u = new string[5] { "B", "KB", "MB", "GB", "TB" };
		int i = 0;
		while (v >= 1024.0 && i < u.Length - 1)
		{
			v /= 1024.0;
			i++;
		}
		return $"{v:0.#} {u[i]}";
	}

	private static Geometry Arc(double frac, double cx, double cy, double r)
	{
		frac = Math.Clamp(frac, 0.0, 0.9999);
		if (frac <= 0.0)
		{
			return Geometry.Empty;
		}
		double a = frac * 2.0 * Math.PI;
		Point start = new Point(cx, cy - r);
		Point end = new Point(cx + r * Math.Sin(a), cy - r * Math.Cos(a));
		PathFigure fig = new PathFigure
		{
			StartPoint = start,
			IsClosed = false
		};
		fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0.0, frac > 0.5, SweepDirection.Clockwise, isStroked: true));
		PathGeometry pathGeometry = new PathGeometry(new PathFigure[1] { fig });
		pathGeometry.Freeze();
		return pathGeometry;
	}
}
