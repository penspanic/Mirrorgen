// Vendored snapshot representing the surface OFF networking DTOs use.
// Kept as a small but representative sample: enum / record positional /
// nullable / collection / dictionary / nested type / special types.
// Dual-attribute: [TsExport] for TsGen, [Transpile] for Mirrorgen — the
// equivalence check ensures both emit the same .ts (modulo header comment
// and indentation, which the test normalises away).

using System;
using System.Collections.Generic;
using Mirrorgen.Attributes;

namespace Sample.Dtos;

// Local stub so TsGen recognises the attribute by name during fixture emit.
// OFF defines this same shape in OpenFieldFramework.Common.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface)]
internal sealed class TsExportAttribute : Attribute { }

[TsExport, Transpile]
public enum ConnectionState
{
    Connecting,
    Connected,
    Disconnected,
}

[TsExport, Transpile]
public sealed record JoinCellRequest(string CellId, string PlayerId);

[TsExport, Transpile]
public sealed record JoinCellResponse(
    string CellId,
    ConnectionState State,
    int? PlayerCount,
    string? Reason);

[TsExport, Transpile]
public sealed record CellSnapshot(
    Guid Id,
    DateTime Captured,
    IReadOnlyList<string> Members,
    IReadOnlyDictionary<string, int> Counts);

[TsExport, Transpile]
public sealed record OperatorProfile(
    string Name,
    JoinCellResponse LastJoin);

[TsExport, Transpile]
public sealed class HostBanner
{
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    public List<ConnectionState> StateHistory { get; init; } = new();
}
