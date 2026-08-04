# Week 3 unavailable-broker evidence

- **Status:** PASS
- **Method:** `scripts/Invoke-Week3PublishFailureProof.ps1`

The helper stopped RabbitMQ, submitted a valid bulk request, inspected SQL, restarted RabbitMQ, and waited for the worker consumer to recover.

| Field | Observed value |
| --- | --- |
| HTTP result | `503 Service Unavailable` |
| Problem title | `A required service is temporarily unavailable.` |
| Persisted job ID | `70227fd0-3cf2-4bfe-b5f5-530be09d9862` |
| Persisted status | `PublishFailed` |
| Persisted safe error | `The persisted job could not be published to the message broker.` |
| Recovered main-queue consumers | `1` |

The client response contained a standard problem document and trace ID, not broker exception details or credentials. The SQL state retained a safe operational classification.

Publisher-confirm loss can be ambiguous if RabbitMQ accepted a command immediately before the connection failed. The job-status concurrency token prevents the API from overwriting concurrent worker progress, and an arriving command is allowed to recover a `PublishFailed` job. Transactional Outbox reconciliation remains Week 4 work.
