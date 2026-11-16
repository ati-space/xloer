namespace FastShot.Core;

/// <summary>One clock for video, annotations and timestamped audio packets, including late packets.
/// Timestamps are monotonic 100 ns units. Paused intervals never enter the media timeline.</summary>
public sealed class RecordingTimeline(long origin)
{
    readonly object gate = new();
    readonly List<(long start, long? end)> pauses = [];
    public bool IsPaused { get { lock (gate) return pauses.Count > 0 && pauses[^1].end is null; } }

    public void Pause(long now)
    {
        lock (gate)
        {
            if (IsPaused) return;
            pauses.Add((Math.Max(origin, now), null));
        }
    }

    public void Resume(long now)
    {
        lock (gate)
        {
            if (!IsPaused) return;
            var last = pauses[^1];
            pauses[^1] = (last.start, Math.Max(last.start, now));
        }
    }

    public double Seconds(long timestamp)
    {
        lock (gate)
        {
            var ticks = Math.Max(0, timestamp - origin);
            foreach (var (start, end) in pauses)
            {
                if (timestamp <= start) break;
                ticks -= Math.Max(0, Math.Min(timestamp, end ?? timestamp) - start);
            }
            return Math.Max(0, ticks) / 10_000_000.0;
        }
    }

    public readonly record struct AudioSpan(int Offset, int Frames, long Destination);

    /// <summary>Splits an audio packet around pause boundaries. Indices use the source's wall clock
    /// relative to origin; destinations use the compressed media clock. Samples in a pause are dropped.</summary>
    public IReadOnlyList<AudioSpan> AudioSpans(long firstFrame, int frames, int rate)
    {
        if (frames < 0 || rate <= 0) throw new ArgumentOutOfRangeException(nameof(frames));
        lock (gate)
        {
            var result = new List<AudioSpan>();
            var finish = checked(firstFrame + frames);
            var cursor = Math.Max(0, firstFrame);
            long removed = 0;
            foreach (var (start, end) in pauses)
            {
                var from = (long)Math.Round((start - origin) * (rate / 10_000_000.0));
                var to = end is { } until ? (long)Math.Round((until - origin) * (rate / 10_000_000.0)) : long.MaxValue;
                if (cursor < from)
                {
                    var stop = Math.Min(finish, from);
                    if (stop > cursor) result.Add(new((int)(cursor - firstFrame), (int)(stop - cursor), cursor - removed));
                    cursor = stop;
                }
                if (cursor >= finish) return result;
                cursor = Math.Max(cursor, to);
                if (to == long.MaxValue) return result;
                removed += to - from;
            }
            if (cursor < finish) result.Add(new((int)(cursor - firstFrame), (int)(finish - cursor), cursor - removed));
            return result;
        }
    }
}
