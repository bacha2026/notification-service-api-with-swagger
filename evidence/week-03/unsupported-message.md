# Week 3 unsupported-schema evidence

- **Status:** PASS for valid JSON with unsupported schema version `99`
- **Method:** `scripts/Invoke-Week3MalformedMessageProof.ps1 -MessageCase UnsupportedSchema`

The Management API routed a syntactically valid command with the expected AMQP type but schema version `99`. The worker rejected it without application retry.

| Field | Observed value |
| --- | --- |
| Publish routed | `true` |
| Message ID | `f00e7814-5db4-47f9-ac6c-8a5248bd52ac` |
| Correlation ID | `week3-unsupportedschema-4a30a1c9ec71431a9e7283be3febcbb2` |
| DLQ ready count before | `3` |
| DLQ ready count after | `4` |
| DLQ unacknowledged | `0` |

This proves the unsupported-schema branch. This original run did not submit a wrong AMQP type; that distinct path was later captured in [the final rerun](final-rerun.md).
