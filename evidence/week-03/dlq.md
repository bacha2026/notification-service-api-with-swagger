# Week 3 RabbitMQ DLQ screenshot evidence

- **Status:** PASS
- **Captured:** 2026-07-20 12:53 Asia/Manila
- **Screenshot:** [dlq.png](dlq.png)

The RabbitMQ Management page visibly records:

| Field | Visible value |
| --- | --- |
| RabbitMQ version | `4.3.2` |
| Queue | `nsa.notifications.bulk.dlq` |
| Queue type | `quorum` |
| Durable | `true` |
| State | `running` |
| Ready | `5` |
| Unacknowledged | `0` |
| Total | `5` |

The image is direct UI evidence that the named durable quorum DLQ existed and held five ready messages at capture time. The screenshot is an aggregate queue view; individual-message identity is established separately in [poison-attempts.md](poison-attempts.md), [malformed-message.md](malformed-message.md), and [unsupported-message.md](unsupported-message.md).
