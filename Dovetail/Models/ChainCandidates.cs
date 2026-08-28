namespace Dovetail;

internal readonly record struct ChainCandidates(
    SegmentModel Sink,
    SegmentModel? Origin
);
