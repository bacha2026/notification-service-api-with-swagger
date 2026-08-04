# Week 3 malformed-message evidence

- **Status:** PASS for malformed JSON with the expected AMQP message type
- **Method:** `scripts/Invoke-Week3MalformedMessageProof.ps1`

The RabbitMQ Management API published deliberately malformed JSON using the valid routing key and type `nsa.notifications.bulk-requested.v1`.

| Field | Observed value |
| --- | --- |
| Publish routed | `true` |
| Message ID | `5cda0c7d-8ea7-4e30-9d93-bc4ef0bbd0a0` |
| Correlation ID | `week3-malformed-3d0bfc43459b451084545d7e30506127` |
| DLQ ready count before | `2` |
| DLQ ready count after | `3` |

The matching-type malformed delivery was rejected without requeue and increased `nsa.notifications.bulk.dlq` by one.

The separate [unsupported-schema record](unsupported-message.md) verifies the valid-JSON rejection branch.
