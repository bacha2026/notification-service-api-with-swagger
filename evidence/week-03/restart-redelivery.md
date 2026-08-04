# Week 3 worker restart and redelivery evidence

- **Status:** PASS
- **Method:** `scripts/Invoke-Week3RestartProof.ps1`

The helper first confirmed one registered consumer, paused that worker, and submitted a job. RabbitMQ dispatched the command to the paused consumer but could not receive an acknowledgement.

| Phase | Job status | Queue unacknowledged | Consumers | Processed / succeeded |
| --- | --- | ---: | ---: | --- |
| Worker paused, delivery outstanding | `Queued` | `1` | `1` | Not captured in this phase |
| Paused worker force-killed and replacement started | `Completed` | `0` | Replacement running | `1 / 1` |

Identifiers remained stable:

- Job ID: `0cf212a7-c34c-4f16-b2c1-60c7beee7d6b`
- Correlation ID before and after restart: `0HNN639K38K46:00000001`

The observed unacknowledged delivery before the force-kill and successful completion after worker replacement demonstrate RabbitMQ redelivery without loss of the persisted job.
