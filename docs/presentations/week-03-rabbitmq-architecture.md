# Week 3 architecture presentation: From fast-but-ephemeral to durable-at-least-once

**Format:** ADR review  
**Duration:** 15 minutes, including 2 minutes 30 seconds for questions  
**Status:** Technical demo evidence verified; live delivery and reviewer feedback are pending  
**Decision record:** [ADR 004](../adr/004-rabbitmq-worker-and-dlq.md)

## Presentation objective

Obtain agreement to replace the Week 2 process-local bulk queue with SQL-persisted jobs, RabbitMQ, and a separately deployable Worker Service, while explicitly accepting at-least-once delivery and the remaining Week 3 consistency gaps.

## Run of show

| Time | Slide | Purpose |
| --- | --- | --- |
| 00:00–00:40 | 1. Decision and ask | State the decision first |
| 00:40–01:50 | 2. Context: Week 2 baseline | Establish evidence and the problem |
| 01:50–03:10 | 3. Drivers and constraints | Define what success means |
| 03:10–05:00 | 4. Options and tradeoffs | Show why the choice wins |
| 05:00–07:10 | 5. Chosen topology | Explain processes, data, and broker names |
| 07:10–09:10 | 6. Delivery and failure semantics | Explain ACK, retry, DLQ, and guarantees |
| 09:10–11:30 | 7. Technical evidence | Report restart, retry, DLQ, and negative-path results |
| 11:30–12:30 | 8. Consequences and Week 4 boundary | Make the costs and remaining gaps explicit |
| 12:30–15:00 | 9. Approval and Q&A | Confirm or amend the decision |

The time boxes total exactly 15 minutes. Captured evidence is the primary proof; if an optional live replay is slow, return to the recorded artifacts rather than consuming the Q&A window.

---

## Slide 1 — Decision and ask

**Time:** 00:00–00:40

### On slide

> Persist bulk jobs in SQL, dispatch a small versioned command through RabbitMQ, and process it in a separate Worker Service.

- Manual ACK after durable SQL progress
- Three bounded attempts, then DLQ
- At-least-once, not exactly-once

**Ask:** Accept ADR 004 as the Week 3 bulk-processing architecture.

### Speaker notes

“The decision is to move bulk execution out of the API. The API will persist the job and items, RabbitMQ will carry a small job-reference command, and a separate worker will process it. We acknowledge only after durable progress. Failed processing gets three attempts and then a named DLQ. I am asking for acceptance of this topology with at-least-once semantics; I am not claiming exactly-once delivery.”

### Transition

“The reason for changing is not submission speed—the Week 2 path is already fast. The reason is what happens when a process fails.”

---

## Slide 2 — Context: Week 2 baseline

**Time:** 00:40–01:50

### On slide

```text
POST bulk → API → bounded Channel → hosted worker → SQL / IEmailSender
                    └── process-local job dictionary ──→ GET status
```

- `202 Accepted` plus job ID and status URL
- Recorded submission p95: **2.004 ms**
- Queue and status disappear on API restart
- One deployable; no cross-instance state or DLQ

### Speaker notes

“Week 2 established the asynchronous HTTP contract. The client gets a 202, a job ID, and a status URL, and the measured p95 submission latency was 2.004 milliseconds. The compromise is that both the bounded channel and status dictionary live inside the API process. Restarting the API can lose queued work and its status. A second API replica would have a different dictionary. The hosted worker also cannot be deployed independently, and poison work has nowhere to go.”

“ADR 003 documented those limits and explicitly deferred the durable broker and separate worker to Week 3. This decision supersedes only that queue portion; the `IEmailSender` and provider-resilience decisions remain.”

### Transition

“So the decision drivers focus on recovery, isolation, and observability while preserving the public contract.”

---

## Slide 3 — Drivers and constraints

**Time:** 01:50–03:10

### On slide

The selected design must:

1. Preserve fast `202` submission and status polling.
2. Keep status available across API/worker restarts.
3. Allow independent API and worker deployment/scaling.
4. Redeliver unacknowledged work and quarantine poison work.
5. Correlate HTTP → broker → worker → SQL → DLQ.
6. Minimize PII in broker messages and logs.
7. Run on Docker Desktop.

**Constraint:** Outbox, Inbox, and idempotency arrive in Week 4.

### Speaker notes

“The API contract should not change just because execution moves out of process. SQL becomes the shared source for status. RabbitMQ must redeliver work that was not acknowledged, and failures must be bounded instead of looping forever. Stable job, message, and correlation IDs let us trace one request end to end.”

“The broker command holds only identifiers, not recipient email, subject, or body. The worker loads those items from SQL. That reduces message size and PII duplication, at the deliberate cost of a shared database boundary.”

“The important constraint is scope: Week 3 is not the Outbox/Inbox week. Any option must be evaluated without pretending that a direct SQL write and broker publish are atomic.”

### Transition

“With those criteria, I considered three practical execution models and one deliberately heavier alternative.”

---

## Slide 4 — Options and tradeoffs

**Time:** 03:10–05:00

### On slide

| Option | Restart durability | Independent worker | Native DLQ | Complexity | Decision |
| --- | --- | --- | --- | --- | --- |
| In-memory `Channel<T>` | No | No | No | Low | Reject beyond W2 |
| SQL polling queue | Yes | Yes | Custom | Medium | Reject for W3 |
| RabbitMQ + SQL status | Yes after broker acceptance | Yes | Yes | Medium | **Select** |
| Kafka-style stream | Yes | Yes | Different model | High | Reject |

### Speaker notes

“Keeping the channel is cheapest, but it fails the restart, scaling, and DLQ drivers. SQL polling can be durable and can support a separate worker, but it makes us build leasing, polling, backoff, and dead-letter behavior on the business database.”

“RabbitMQ fits a command work queue: competing consumers, manual acknowledgement, redelivery, direct routing, and dead-lettering are native concepts, and it runs locally in Docker. It adds infrastructure and eventual consistency, which are real costs.”

“Kafka is useful for a durable event history and high-throughput streams, but that is not the immediate problem. We need one command processed by a worker, not a replayable enterprise event platform.”

“RabbitMQ therefore wins on the actual Week 3 drivers, not because a broker is automatically better.”

### Transition

“Here is the exact topology and where each piece of state lives.”

---

## Slide 5 — Chosen topology and contract

**Time:** 05:00–07:10

### On slide

```text
                              GET status
Client ──POST──> API ───────> SQL jobs + items <──── durable progress ─── Worker
                  │                                                    │
                  └─> nsa.notifications.commands.v1                    └─> IEmailSender
                         [bulk.requested.v1]
                                  │
                                  v
                       nsa.notifications.bulk.v1
                           durable quorum queue
                                  │ final reject
                                  v
                  nsa.notifications.dead-letter ──> nsa.notifications.bulk.dlq
```

Command v1:

```json
{
  "schemaVersion": 1,
  "messageId": "…",
  "jobId": "…",
  "correlationId": "…",
  "createdAtUtc": "…"
}
```

Client library: `RabbitMQ.Client` `7.2.1`

### Speaker notes

“The API first persists the bulk job and its items in SQL. It then publishes a persistent version 1 command to the durable direct exchange `nsa.notifications.commands.v1` with routing key `bulk.requested.v1`. The durable quorum queue is `nsa.notifications.bulk.v1`.”

“The command is intentionally small: schema version, logical message ID, job ID, correlation ID, and creation time. The worker uses the job ID to load the actual items. Message and correlation IDs remain stable through retry.”

“The API status endpoint reads SQL, so status remains available while the worker is stopped and is not tied to one API instance. The worker records progress in SQL and keeps outbound delivery behind `IEmailSender`.”

“The main queue dead-letters through `nsa.notifications.dead-letter` into `nsa.notifications.bulk.dlq`. The implementation uses RabbitMQ.Client 7.2.1 and its asynchronous APIs.”

### Transition

“The topology is only half the decision. The acknowledgement order defines the actual delivery guarantee.”

---

## Slide 6 — ACK, retry, DLQ, and guarantees

**Time:** 07:10–09:10

### On slide

```text
Success:       process → commit SQL progress → manual ACK
Worker crash:  no ACK → RabbitMQ redelivery
Retryable:     republish with incremented x-retry-count
Exhausted:     third failure → reject(requeue: false) → DLQ
Malformed/v?:  reject(requeue: false) → DLQ
```

| Attempt | `x-retry-count` | Next failure |
| --- | ---: | --- |
| 1 | absent / `0` | Republish as `1` |
| 2 | `1` | Republish as `2` |
| 3 | `2` | Reject without requeue |

**Guarantee:** at-least-once delivery, with known gaps—not exactly-once.

### Speaker notes

“Automatic acknowledgement is disabled. On success, the worker commits durable progress before it acknowledges. If the worker dies first, RabbitMQ redelivers. We intentionally prefer possible duplication over silent loss.”

“Retry count is application-owned. Missing means zero. The first two retryable failures republish the same logical command with counts one and two. The third failed processing attempt records a safe terminal failure and rejects without requeue, which activates the DLX/DLQ path. Malformed or unsupported schema commands are permanent failures and dead-letter immediately.”

“Publisher channels use confirms and mandatory routing, but a broker confirm is only transport evidence. The retry publish confirmation and acknowledgement are not one atomic action. Likewise, the API's SQL commit and direct RabbitMQ publish are not one transaction. A crash can leave a job without a command or create an ambiguous client outcome.”

“A crash after a provider accepts an email but before progress and ACK can also repeat the provider call. An Outbox with confirm-backed reconciliation, Inbox, and idempotent side effects belong to Week 4. This is why the guarantee is at least once, never exactly once.”

“Broker retry count is separate from the existing provider HTTP retry policy. We log those layers separately so three command attempts are not mistaken for three total network calls.”

### Transition

“The recorded technical evidence makes those semantics visible rather than leaving them as diagram labels.”

---

## Slide 7 — Technical demonstration evidence

**Time:** 09:10–11:30

### On slide

**Technical evidence: verified. Presentation delivery and reviewer feedback: pending.**

| Check | Recorded result |
| --- | --- |
| Foundation | **76 / 76 tests passed**; SQL Server, RabbitMQ, API, and worker were healthy in hardened Compose |
| Happy path | The submitted job reached `Completed` |
| Worker redelivery | Paused worker showed `unacknowledged = 1`; the same job reached `Completed` after force-kill/restart |
| Broker recovery | The consumer recovered after RabbitMQ restart; a separate in-flight run preserved one unacknowledged command and completed the same SQL job after recovery |
| Bounded poison | Job `ae6d260a-8618-4477-84ef-97362b43fe1c` ran three attempts; final DLQ headers were `x-retry-count = 2` and `x-death = rejected` |
| DLQ and negative paths | RabbitMQ UI recorded `Ready = 5`, `Total = 5`; 503 `PublishFailed`, malformed JSON, unsupported schema, wrong AMQP type, and the zero-match log review passed |

The original broker-restart run proved consumer recovery, subsequent processing, and retention of existing DLQ work. The final rerun separately captured one unacknowledged main-queue command before the broker restart and the same persisted job completing after recovery.

### Speaker notes

“The technical rerun completed with 76 of 76 tests passing and all four hardened Compose services healthy. A happy job reached `Completed`. With the worker paused, RabbitMQ showed one unacknowledged delivery; after the worker was force-killed and replaced, that same persisted job reached `Completed`.”

“RabbitMQ restart recovered the worker consumer and retained the existing DLQ entry. The deterministic poison job `ae6d260a-8618-4477-84ef-97362b43fe1c` ran exactly three attempts and finished with `x-retry-count = 2` and `x-death = rejected`. The RabbitMQ UI screenshot recorded five ready and five total DLQ messages.”

“The unavailable-broker scenario returned 503 with persisted `PublishFailed` state. Malformed JSON, unsupported schema, and wrong-AMQP-type commands reached the DLQ. A separate broker-restart run preserved an in-flight delivery, and the operational-log review found zero matches for exercised content and credentials. These are completed technical results; the live architecture presentation and reviewer decision remain pending.”

### Presenter checklist

- Present the captured technical results as completed evidence, not as a live-presentation outcome.
- Do not expose broker credentials, recipient addresses, subjects, or bodies on screen.
- Keep the restart transcripts, poison record, verification checklist, and DLQ screenshot ready.
- Distinguish the original recovery-only run from the final in-flight broker-restart proof, and keep both evidence boundaries explicit.
- Stop at 11:30 and move to consequences even if an optional live replay is still running.

### Transition

“The evidence proves the recorded bounded and observable failure paths. It does not remove the accepted consistency gaps.”

---

## Slide 8 — Consequences and Week 4 boundary

**Time:** 11:30–12:30

### On slide

**We gain**

- Restart redelivery and SQL-backed status
- Independent API/worker deployment and scaling
- Named poison quarantine and end-to-end correlation
- Small broker messages without notification payload PII

**We pay**

- RabbitMQ operations and schema/topology ownership
- Eventual consistency and more failure modes
- Duplicate-side-effect risk
- Direct SQL/publish and retry-publish/ACK gaps

**Week 4:** Outbox reconciliation + Inbox/idempotency.

### Speaker notes

“The selected design moves us from invisible process-local loss to recoverable and inspectable work. It also adds a broker, a shared schema contract, health checks, storage, and operational ownership.”

“The most important remaining risk is duplicate side effects and the SQL-to-broker dual write. Durable queues and transport-level publisher confirms do not make two systems one transaction. Week 4 closes those gaps with an Outbox relay that reconciles confirm-backed publication state, then Inbox/idempotency for duplicate delivery.”

“The decision is therefore intentionally incremental: it earns process isolation and failure visibility now without making a false exactly-once claim.”

### Transition

“My approval question is whether this is the right trade for the Week 3 boundary.”

---

## Slide 9 — Approval and Q&A

**Time:** 12:30–15:00

### On slide

> Do we accept RabbitMQ, a separate Worker Service, SQL-backed status, manual ACK, and a three-attempt DLQ policy for Week 3—while explicitly deferring atomic publication and deduplication to Week 4?

Record one outcome:

- Accept ADR 004
- Accept with named follow-up
- Revise and re-review

Feedback record: [`evidence/week-03/feedback.md`](../../evidence/week-03/feedback.md)

### Speaker notes

“The decision is not ‘RabbitMQ makes delivery perfect.’ The decision is that restart recovery, independent execution, bounded failure, and observability justify RabbitMQ's operational cost, with at-least-once semantics explicitly accepted. I would like an accept, an accept-with-follow-up, or a concrete revision request, and I will record the result and owner.”

Do not fill in the feedback outcome until the live reviewer states it.

---

## Prepared Q&A

### Why RabbitMQ instead of keeping the channel?

The channel is fast but belongs to one API process. It cannot preserve broker work across restart, coordinate replicas, isolate the worker deployment, or supply a native DLQ. Those are the Week 3 drivers.

### Why not use SQL as the queue as well as the status store?

SQL polling can work, but it requires custom leases, polling cadence, backoff, and dead-letter behavior and adds queue workload to the business database. RabbitMQ already models competing consumers, acknowledgement, redelivery, routing, and dead-lettering. SQL remains the authoritative business/status store.

### Why publish only `jobId` instead of all notification items?

The small command avoids duplicating recipient, subject, and body data into the broker and its DLQ, reduces message size, and makes SQL the authoritative job source. The accepted cost is that the worker shares database and application contracts with the API in Week 3.

### Why a quorum queue?

The queue carries commands that should survive a broker-node failure model better than a non-replicated transient queue. Quorum also fits the acknowledgement/redelivery work-queue semantics. In the local single-node Docker demonstration, the main proof is persistence across container restart; production replication still requires multiple RabbitMQ nodes.

### What happens when RabbitMQ is unavailable during submission?

The recorded unavailable-broker scenario returned HTTP 503 and persisted safe `PublishFailed` state. A crash between the SQL commit and direct publish can still leave a persisted job without a command. The Week 4 Outbox relay is the planned recovery mechanism.

### Does a durable quorum queue guarantee no messages are lost?

No. It protects a persistent message after RabbitMQ accepts it under the configured durability model. Publisher confirms can report broker acceptance/routing, but neither mechanism makes the preceding SQL write and publish atomic, guarantees the HTTP response, or replaces Outbox reconciliation.

### Why acknowledge after SQL progress?

Acknowledging first could remove the command before its outcome is durable. Committing first means a worker crash can cause redelivery, so it prefers a possible duplicate over silent loss.

### Can an email be sent more than once?

Yes. If the provider accepts a send and the worker fails before durable progress and acknowledgement, RabbitMQ can redeliver the command. Week 4 Inbox/idempotency and a verified provider deduplication contract are required before claiming duplicate-safe delivery.

### Why not requeue the same delivery directly?

Direct requeue does not provide a clear application attempt budget. Republishing with `x-retry-count` makes attempts explicit and testable. The republish/ACK handoff is still non-atomic and is documented as a Week 3 limitation.

### Does the DLQ fix failed work?

No. It quarantines and preserves the small command for inspection. An operator still needs to classify, repair, replay, or discard it under an explicit policy.

### Are broker retries the same as Polly email retries?

No. `x-retry-count` counts processing of the RabbitMQ command. Polly counts physical provider attempts within one application send. Metrics and logs must keep them separate because composing the policies can multiply outbound calls.

### How does the worker scale?

Additional worker replicas compete for commands from the main queue, while SQL holds shared status. Prefetch, concurrency, database capacity, and provider rate limits must be bounded and measured. Multiple consumers also mean callers must not assume global completion order.

### Why is the poison subject opt-in?

It is a deterministic local teaching mechanism, not business behavior. Keeping it disabled by default prevents an ordinary subject from triggering intentional failure outside a controlled evidence run.

## Claims to avoid

- “Exactly once.”
- “A durable queue makes SQL and publish atomic.”
- “A returned `202` always survives broker outage” before Week 4 reconciliation exists.
- “The DLQ automatically recovers messages.”
- “Three command attempts means three provider HTTP calls.”
- “Provider delivery is production-proven” while the provider remains disabled by default and its real contract is unverified.
- “The presentation was delivered” until the live event and reviewer feedback are recorded.

## Source material

- [ADR 004](../adr/004-rabbitmq-worker-and-dlq.md)
- [ADR 003](../adr/003-background-bulk-notifications-and-resilience.md)
- [Week 2 job lifecycle evidence](../../evidence/week-02/job-lifecycle.md)
- [Week 3 verification checklist](../../evidence/week-03/verification-checklist.md)
- [Week 3 DLQ screenshot](../../evidence/week-03/dlq.png)
- [Six-week execution plan](../plans/NSA-6-Week-Execution-Plan.html)
