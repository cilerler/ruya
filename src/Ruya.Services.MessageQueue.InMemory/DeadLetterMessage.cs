using System;

namespace Ruya.Services.MessageQueue.InMemory;

internal sealed record DeadLetterMessage(
    string Topic,
    string MessageId,
    byte[] SerializedMessage,
    string Reason,
    int AttemptCount,
    DateTimeOffset Timestamp);
