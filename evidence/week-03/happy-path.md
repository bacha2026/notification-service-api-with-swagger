# Week 3 happy-path evidence

- **Status:** PASS
- **Observed:** 2026-07-20 04:50:45 UTC
- **Endpoint:** `POST /api/v2/notifications/bulk`

| Field | Observed value |
| --- | --- |
| HTTP result | `202 Accepted` |
| Job ID | `1ebac28b-2b65-473b-85b8-79d24a28e815` |
| Location | `/api/v2/notifications/bulk/1ebac28b-2b65-473b-85b8-79d24a28e815` |
| Correlation header / status | `0HNN639K38K43:00000001` / same value |
| Final persisted status | `Completed` |
| Processed / succeeded | `1 / 1` |

The API returned its asynchronous contract, and the SQL-backed status endpoint later returned the same job and correlation ID with one successfully processed item. In this implementation, `202` is returned only after the job/items are saved and the confirmation-tracked RabbitMQ publish completes.

This record proves the live one-item path. It is not a build/test or full Compose-startup transcript.
