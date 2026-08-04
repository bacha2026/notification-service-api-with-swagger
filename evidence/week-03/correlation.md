# Week 3 correlation evidence

- **Status:** PASS for the recorded live paths

| Scenario | Job ID | Message ID | Correlation ID | Correlated observations |
| --- | --- | --- | --- | --- |
| Happy path | `1ebac28b-2b65-473b-85b8-79d24a28e815` | Not captured | `0HNN639K38K43:00000001` | HTTP header/location and completed SQL-backed status |
| Worker redelivery | `bedc3d6d-6907-4355-9e29-5cfac1a4e97f` | Not captured | `0HNN68RP41LI1:00000001` | Queued status, one unacknowledged delivery, and completion after restart on the final images |
| Broker recovery | `8c85df79-e089-406e-9d38-fa1f08ae1e2f` | Not captured | `0HNN639K38K4C:00000001` | Post-restart consumer and completed status |
| Bounded poison path | `465f1986-38a5-4da1-9c79-2b47882dac4d` | `162a15a1-1563-4328-871a-8da444d373f0` | `0HNN68RP41LJB:00000001` | API/SQL job, three attempt logs, and final DLQ headers on the final images |
| Malformed JSON | Not applicable | `69386b4b-1388-4873-8e9a-13eaf313afe8` | `week3-malformedjson-5b4b43830d60493bb36e4920967d383e` | Management publication and DLQ increase |
| Unsupported schema | Not applicable | `12a15120-5679-4c6f-8ec8-9cd2316cd53f` | `week3-unsupportedschema-3aac43508caa41589d45bf0050b51c68` | Valid JSON rejection and DLQ increase |
| Wrong AMQP type | Not applicable | `ffb7b1a8-602f-4267-95a5-a3c63920984b` | `week3-wrongmessagetype-a9e4f370441945409b2a5c9c6024f233` | Valid command body with unsupported type and DLQ increase |
| In-flight broker restart | `e7716477-ae09-4419-8250-8301a450f6bc` | Not captured | `0HNN68RP41LIS:00000001` | One unacknowledged command before restart and the same SQL job completed after broker recovery on the final images |

The poison record is the strongest end-to-end identifier chain because it includes job, message, and correlation IDs plus retry logs and final DLQ metadata. Missing message IDs in other rows are recorded as not captured rather than inferred. The two final-rerun rows are linked to their exact observations in [final-rerun.md](final-rerun.md).
