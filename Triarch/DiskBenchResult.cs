namespace Triarch;

public sealed record DiskBenchResult(string Drive, long FileMb, double SeqReadMBs, double SeqWriteMBs, double ReadIopsQ1, double WriteIopsQ1, double ReadLatencyUs, double WriteLatencyUs, double ReadIopsQ8)
{
	public double Score => (SeqReadMBs + SeqWriteMBs) / 2.0 + (ReadIopsQ1 + WriteIopsQ1) / 200.0 + ReadIopsQ8 / 2000.0;
}
