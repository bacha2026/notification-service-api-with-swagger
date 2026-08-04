# Week 3 RabbitMQ restart evidence

- **Status:** PASS for broker recovery, worker reconnection, subsequent processing, and DLQ retention
- **Restart began:** `2026-07-20T04:51:11.5549777Z`
- **Method:** `scripts/Invoke-Week3BrokerRestartProof.ps1`

After the RabbitMQ container restarted:

- the worker recovered one consumer on `nsa.notifications.bulk.v1`;
- job `8c85df79-e089-406e-9d38-fa1f08ae1e2f` reached `Completed` with `1 / 1` processed/succeeded;
- its correlation ID was `0HNN639K38K4C:00000001`; and
- the durable DLQ retained its existing message count: `4` before and `4` after restart.

This proves that the broker recovered, the worker reconnected, new work completed, and the existing DLQ entry was retained. This original script submitted its recorded job after broker recovery. The narrower in-flight case was subsequently captured in [the final rerun](final-rerun.md), with one unacknowledged command before restart and the same SQL job completing after recovery.
