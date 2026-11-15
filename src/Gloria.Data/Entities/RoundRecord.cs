using Gloria.Core.Ingestion;

namespace Gloria.Data.Entities;

/// <summary>One live round of a demo.</summary>
public sealed class RoundRecord
{
    public int DemoId { get; set; }

    public int Number { get; set; }

    public int StartTick { get; set; }

    public int FreezeEndTick { get; set; }

    public int EndTick { get; set; }

    public Team Winner { get; set; }

    public DemoRecord? Demo { get; set; }
}
