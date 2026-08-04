# Training evidence index

This directory separates reproducible technical evidence from prepared or external deliverables. A source file or checklist entry is not proof that a live scenario passed. Human reviews, account-dependent publication, and unrecorded Docker runs remain explicitly pending.

Current run instructions are available as the [smoke-testing HTML](../docs/SmokeTestingGuide.html) and regenerated [PDF](../SmokeTestingGuide.pdf). The older in-process BackgroundService explanation is explicitly labeled as a historical Week 2 snapshot.

## Week 1

- [Environment, build, SQL startup, and dependency check](week-01/build.txt)
- [CRUD/OpenAPI smoke record](week-01/crud-smoke.md)
- [Swagger UI screenshot](week-01/swagger.png)
- [Skill IQ record](week-01/skill-iq-record.md) - pending engineer input
- [Demo and design-session notes](week-01/demo-notes.md) - human session pending
- [Architecture source](../drawio/notification.drawio), [real-time architecture PDF](week-01/architecture-export.pdf), [PNG](../docs/architecture/week-01-realtime-order-tracking.png), and [one-page decision](../docs/week-01-realtime-order-tracking-decision.md)

## Week 2

- [Error and version matrix](week-02/error-matrix.md)
- [Repeatable HTTP probes](week-02/versioning.http) and [captured result](week-02/live-contract-smoke.json)
- [Polly verification transcript](week-02/polly-test.txt)
- [Bulk lifecycle record](week-02/job-lifecycle.md)
- [Raw bulk latency data](week-02/bulk-latency.json)
- [EM Review #1 record](week-02/em-review-01.md) - pending external review

## Week 3

Implemented topology and verification state:

- [RabbitMQ/SQL/worker topology](week-03/topology.md)
- [Week 3 verification checklist](week-03/verification-checklist.md)
- [ADR 004 - RabbitMQ and separate Worker Service](../docs/adr/004-rabbitmq-worker-and-dlq.md)
- [Prepared 15-minute architecture presentation script](../docs/presentations/week-03-rabbitmq-architecture.md)
- [Prepared printable presentation HTML](../docs/presentations/week-03-rabbitmq-architecture.html)
- [Prepared 9-page architecture presentation PDF](week-03/presentation.pdf) - live delivery is still pending

Captured runtime evidence:

- [API/worker build and test transcript](week-03/build-test.txt)
- [Compose configuration, build, and healthy startup transcript](week-03/compose-start.txt)
- [Happy path](week-03/happy-path.md)
- [Worker stop-before-ACK redelivery](week-03/restart-redelivery.md)
- [RabbitMQ restart and recovery](week-03/broker-restart.md)
- [Unavailable-broker 503 and persisted `PublishFailed`](week-03/publish-failure.md)
- [Malformed JSON to DLQ](week-03/malformed-message.md)
- [Unsupported schema version 99 to DLQ](week-03/unsupported-message.md)
- [Wrong AMQP type and in-flight broker restart](week-03/final-rerun.md)
- [Dependency-readiness failure and recovery](week-03/readiness-recovery.md)
- [Three poison attempts and final dead-letter](week-03/poison-attempts.md), reproduced by [the poison proof helper](../scripts/Invoke-Week3PoisonProof.ps1)
- [Identifier correlation](week-03/correlation.md)
- [Zero-match operational-log content review](week-03/log-review.md)
- [RabbitMQ DLQ screenshot record](week-03/dlq.md) and [PNG](week-03/dlq.png)

Frontend and review artifacts:

- [React Hooks substitute training set](week-03/react-hooks-audit.md) - prepared locally because EM-supplied snippets were not provided
- [Gist publication record](week-03/react-gist-link.md) - external publication pending
- [Architecture presentation feedback record](week-03/feedback.md) - live delivery and reviewer feedback pending

Remaining boundaries that must not be inferred from implementation alone are external: Gist publication, work against EM-provided snippets, live presentation delivery, and reviewer feedback remain pending.
