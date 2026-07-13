# Week 2 bulk-job lifecycle evidence

## Contract confirmed

The Week 2 bulk operation is an in-process **record-processing** job:

- `POST /api/v2/notifications/bulk` validates at most 100 items, performs concurrency-safe bounded admission, and returns `202 Accepted` with a version-preserving status URL.
- The hosted worker persists every item. Email items pass through `INotificationDispatcher`; other channels currently stop at record persistence.
- `SentAtUtc` is set only when an enabled provider returns `AcceptedByProvider`. Disabled mode returns `NotAttempted`, makes no network call, and leaves the intent pending without failing record processing.
- Provider exceptions leave an unsent intent and count the bulk item as failed. Public job errors are sanitized.
- Terminal job snapshots retain total/progress/status/timestamps but release recipient, subject, and body payload references.

## Automated proof

The following are covered by [BulkNotificationJobServiceTests.cs](../../tests/NSA.Tests/BulkNotificationJobServiceTests.cs), [BulkNotificationWorkerTests.cs](../../tests/NSA.Tests/BulkNotificationWorkerTests.cs), [NotificationDispatcherTests.cs](../../tests/NSA.Tests/NotificationDispatcherTests.cs), and [DisabledEmailOrderWorkflowTests.cs](../../tests/NSA.Tests/DisabledEmailOrderWorkflowTests.cs):

- empty/null/oversized validation;
- queue and tracked-job capacity with safe 503 behavior;
- 128 concurrent admission attempts never exceeding the configured eight-job limit;
- no orphaned status entry after a full-channel rejection;
- queued → processing → completed/completed-with-errors transitions;
- graceful shutdown → cancelled;
- `ProcessedCount = SucceededCount + FailedCount`;
- terminal payload release with stable retained totals;
- deterministic retention expiry using `TimeProvider`;
- persistence before provider send, pending intent on failure or disabled mode, and sent timestamp after provider acceptance.

## Submission latency

On 2026-07-13, 25 measured one-item requests after five warm-ups produced:

| Metric | Result |
| --- | ---: |
| p50 | 1.047 ms |
| p95 | 2.004 ms |
| Maximum | 4.38 ms |
| Acceptance | **PASS** (`2.004 ms < 100 ms`) |

See [raw latency data](bulk-latency.json) and rerun with `scripts/Measure-BulkNotificationLatency.ps1` while the API is listening on port 5099.

## Accepted Week 2 limits

The queue and status store are process-local and are lost on restart. There is no broker, DLQ, multi-instance coordination, atomic database/provider transaction, or automatic replay of pending intents. RabbitMQ/separate worker is Week 3; durable Outbox/Inbox and idempotent replay are Week 4.
