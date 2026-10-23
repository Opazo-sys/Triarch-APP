using System.IO;

namespace Triarch;

public sealed class HelperProgram
{
	public string Path { get; set; } = "";

	public string Args { get; set; } = "";

	public bool CloseWithGame { get; set; } = true;

	public string Display => System.IO.Path.GetFileName(Path) + (string.IsNullOrWhiteSpace(Args) ? "" : (" " + Args));
}
