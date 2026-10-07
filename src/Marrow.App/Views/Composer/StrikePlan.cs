namespace Marrow.App.Views.Composer;

public sealed class StrikePlan
{
    internal StrikePlan(bool withTwin)
    {
        Channels[0] = new StrikeLine(95); Channels[1] = new StrikeLine(44);
        for (var i = 0; i < Branches.Length; i++) Branches[i] = new StrikeLine(9);
        if (withTwin) TwinSlot = new StrikePlan(false);
    }

    internal bool Ready;
    internal StrikeGeometry Geometry;
    internal readonly StrikePlan? TwinSlot;
    public StrikePlan? Twin => Timing.Event == 2 ? TwinSlot : null;
    public StrikeTiming Timing { get; internal set; }
    public StrikeLine[] Channels { get; } = new StrikeLine[2];
    public StrikeLine[] Branches { get; } = new StrikeLine[8];
    public int ChannelCount { get; internal set; }
    public int BranchCount { get; internal set; }
    public int Extent { get; internal set; }
    public int LastMain => Channels[0].Count / 2 - 1;
    public double FlashX { get; internal set; }
    public double FlashY { get; internal set; }
    public int[] OrbIndices { get; } = new int[95];
    public double[] OrbSegment { get; } = new double[190];
    public int OrbCount { get; internal set; }
    public double ExitAngle { get; internal set; }
    public double[] SparkPoints { get; } = new double[8];
    public double? SparkAt { get; internal set; }
    public bool SparkBlocked { get; internal set; }
    internal StrikePlumeBuffer Plume { get; } = new();
    internal StrikeContactBuffer Contacts { get; } = new();

    // E:567-571,841-858; P:590-599. Cached with the geometry-tagged slot.
    public StrikeContactBuffer ContactEvents()
    {
        var result = Contacts; if (result.Ready) return result;
        result.Count = 0; var prev = 0; var prevFork = 0; var timing = Timing;
        for (var j = 1; j <= timing.Steps; j++)
        {
            var q = Math.Min(1, j / (double)timing.Steps) * timing.Reach; var tip = Strike.Round(q * LastMain);
            var e = result.Next(-timing.Lead + (j - 1) * timing.Lead / timing.Steps);
            e.Add(0, Math.Max(0, prev - 1), tip); prev = tip;
            if (ChannelCount > 1)
            {
                var fork = Channels[1]; var m = q * LastMain;
                var ft = m <= fork.Split ? -1 : Strike.Round(Math.Min(1, (m - fork.Split) / Math.Max(1, LastMain * timing.Reach - fork.Split)) * timing.Reach * (fork.Count / 2 - 1));
                if (ft > 0) { e.Add(1, Math.Max(0, prevFork - 1), ft); prevFork = ft; }
            }
        }
        var all = result.Next(0); all.Add(0, 0, Extent);
        if (ChannelCount > 1) all.Add(1, 0, Channels[1].Count / 2 - 1);
        for (var b = 0; b < BranchCount; b++) if (Branches[b].At <= Extent) all.Add(b + 2, 0, Branches[b].Count / 2 - 1);
        Span<int> times = stackalloc int[10]; var count = 0;
        for (var i = 0; i < timing.FlickerCount; i++) if (timing.Flicker(i).At > 0) InsertTime(times, ref count, Strike.Round(timing.Flicker(i).At * 1000));
        for (var i = 1; i <= 6; i++) InsertTime(times, ref count, i * 50);
        for (var i = 0; i < count; i++)
        {
            var e = result.Next(times[i] / 1000.0); e.Count = all.Count;
            Array.Copy(all.Segments, e.Segments, all.Count);
        }
        result.Ready = true; return result;
    }

    private static void InsertTime(Span<int> times, ref int count, int time)
    {
        var at = 0; while (at < count && times[at] < time) at++;
        if (at < count && times[at] == time) return;
        for (var i = count; i > at; i--) times[i] = times[i - 1];
        times[at] = time; count++;
    }
}
