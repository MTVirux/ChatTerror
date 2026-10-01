using System;
using System.Collections.Generic;
using System.Threading;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Logic;

public interface IGameGate
{
    bool IsLoggedIn { get; }
    bool IsBusy { get; }
    string? Sanitize(string line);
}

public sealed record SendRequest(string DeviceId, SendChatPayload Payload);

public sealed class SendQueue
{
    public const int MaxQueue = 20;
    public const long BusyTimeoutMs = 10_000;

    private readonly Func<RelaySettings> settings;
    private readonly IGameGate gate;
    private readonly Func<long> nowMs;
    private readonly Lock sync = new();
    private readonly Queue<(SendRequest Req, long EnqueuedAt)> queue = new();
    private readonly List<(SendRequest Req, SendResultPayload Result)> overflow = new();
    private long? lastSentAt;

    public SendQueue(Func<RelaySettings> settings, IGameGate gate, Func<long> nowMs)
    {
        this.settings = settings;
        this.gate = gate;
        this.nowMs = nowMs;
    }

    public void Enqueue(SendRequest r)
    {
        lock (sync)
        {
            if (queue.Count >= MaxQueue)
                overflow.Add((r, Fail(r, SendErrors.Busy)));
            else
                queue.Enqueue((r, nowMs()));
        }
    }

    public (string? Line, IReadOnlyList<(SendRequest Req, SendResultPayload Result)> Results) Tick()
    {
        lock (sync)
        {
            var results = new List<(SendRequest Req, SendResultPayload Result)>(overflow);
            overflow.Clear();

            var s = settings();
            var now = nowMs();
            while (queue.TryPeek(out var head))
            {
                var req = head.Req;
                var error = SendValidator.Validate(req.Payload, s);
                if (error is null && s.RequireLoggedIn && !gate.IsLoggedIn)
                    error = SendErrors.NotLoggedIn;

                if (error is null && gate.IsBusy)
                {
                    if (now - head.EnqueuedAt < BusyTimeoutMs)
                        break;
                    error = SendErrors.Busy;
                }

                string? line = null;
                if (error is null)
                {
                    line = gate.Sanitize(SendValidator.BuildLine(req.Payload));
                    if (line is null)
                        error = SendErrors.InvalidText;
                }

                if (error is not null)
                {
                    queue.Dequeue();
                    results.Add((req, Fail(req, error)));
                    continue;
                }

                if (lastSentAt is { } last && now - last < s.SendDelayMs)
                    break;

                queue.Dequeue();
                lastSentAt = now;
                results.Add((req, new SendResultPayload(req.Payload.RequestId, true)));
                return (line, results);
            }

            return (null, results);
        }
    }

    private static SendResultPayload Fail(SendRequest r, string error) => new(r.Payload.RequestId, false, error);
}
