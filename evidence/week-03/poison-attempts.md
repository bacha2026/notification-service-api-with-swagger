# Week 3 bounded poison-attempt evidence

- **Status:** PASS
- **Failure injection:** Explicit local-only `[week3-poison]` subject

| Field | Observed value |
| --- | --- |
| Job ID | `ae6d260a-8618-4477-84ef-97362b43fe1c` |
| Message ID | `660e3c74-ed6c-4000-8c3a-5f490c77611a` |
| Correlation ID | `0HNN639K38K50:00000001` |
| Total processing attempts | `3` |
| Persisted terminal status | `DeadLettered` |
| Final DLQ `x-retry-count` | `2` |
| Final `x-death` reason | `rejected` |
| DLQ ready count | `4` before, `5` after |

The command failed on the initial delivery, was republished for attempts two and three, then was rejected without requeue after the third failure. RabbitMQ routed that final delivery through the configured DLX to `nsa.notifications.bulk.dlq`.

Failure injection was enabled only for this script run. The helper then recreated the worker with the checked-in empty default and verified that it became healthy.
