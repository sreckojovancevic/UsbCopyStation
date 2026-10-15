namespace UsbCopyStation.Core;

internal enum SlotPhase { Empty, Ready, HasData, Blocked, Waiting, Writing, Verifying, Done, Failed, Cancelled }

/// <summary>Stanje jednog slota (fizičkog porta). Radni thread piše napredak, UI tajmer čita.</summary>
internal sealed class SlotState
{
    public string Key { get; init; } = "";
    public int Number { get; init; }

    public DiskInfo? Disk { get; set; }
    public bool Present { get; set; }
    public string BlockReason { get; set; } = "";

    public volatile SlotPhase Phase = SlotPhase.Empty;
    public string Message { get; set; } = "";

    private long _bytesDone;
    public long BytesDone => Interlocked.Read(ref _bytesDone);
    public long BytesTotal { get; private set; }
    public DateTime PhaseStartUtc { get; private set; }
    public DateTime JobStartUtc { get; set; }
    public TimeSpan LastDuration { get; set; }

    public bool Running { get; set; }
    public CancellationTokenSource? Cts { get; set; }
    /// <summary>InstanceId stika koji je u ovoj seriji uspešno upisan (da ga automatski režim ne piše ponovo).</summary>
    public string? DoneInstanceId { get; set; }
    /// <summary>InstanceId stika na kom je poslednji posao rađen (rezultat se prikazuje dok je on u portu).</summary>
    public string? LastJobInstanceId { get; set; }

    public void BeginPhase(SlotPhase phase, long total)
    {
        BytesTotal = total;
        Interlocked.Exchange(ref _bytesDone, 0);
        PhaseStartUtc = DateTime.UtcNow;
        Phase = phase;
    }

    public void Advance(long bytes) => Interlocked.Add(ref _bytesDone, bytes);

    public double Fraction => BytesTotal > 0 ? Math.Clamp((double)BytesDone / BytesTotal, 0, 1) : 0;

    public double MBps
    {
        get
        {
            double secs = (DateTime.UtcNow - PhaseStartUtc).TotalSeconds;
            return secs > 0.5 ? BytesDone / secs / (1024 * 1024) : 0;
        }
    }
}
