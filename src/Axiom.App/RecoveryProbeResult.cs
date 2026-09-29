namespace Axiom.App;

internal sealed record RecoveryProbeResult(string Machine
                                        , string Runtime
                                        , string Architecture
                                        , IReadOnlyList<RecoveryProbeBufferResult> Buffers)
{
    public long MaximumObservedLossMilliseconds => Buffers.Max(buffer => buffer.ObservedLossMilliseconds);
}
