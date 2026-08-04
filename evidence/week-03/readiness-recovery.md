# Week 3 dependency-readiness failure and recovery evidence

- **Status:** PASS for RabbitMQ and SQL Server independently
- **Recorded:** 2026-07-20, Asia/Manila
- **Method:** `scripts/Invoke-Week3ReadinessRecoveryProof.ps1`
- **Source/image boundary:** final rebuilt API `sha256:1ed8cecd579d...` and Worker `sha256:ffe9cb388a60...`

The proof deliberately stopped one dependency at a time while leaving the API and WorkerService running. It required process liveness to stay available, dependency readiness to fail, the worker readiness marker to disappear, Docker health to become unhealthy, and all gates to recover after the dependency restarted.

| Dependency | Liveness during outage | Readiness during outage | Readiness failure detected | Worker marker removed | API + worker unhealthy | Full recovery |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| RabbitMQ | 200 | 503 | 5,397.270 ms | 8,591.966 ms | 26,440.834 ms | 36,781.285 ms |
| SQL Server | 200 | 503 | 5,381.427 ms | 9,967.967 ms | 23,923.179 ms | 49,463.561 ms |

Final state after each run:

- `/health/live` returned 200;
- `/health/ready` returned 200;
- API Docker health was `healthy`;
- worker Docker health was `healthy`; and
- the worker readiness marker was restored.

The first pre-final SQL exercise exposed that the default SQL connection timeout could outlive the intended probe bound. The final configuration adds `Connect Timeout=2;ConnectRetryCount=0`, bounds RabbitMQ connection/handshake attempts to two seconds, and uses three Docker health retries. The images were rebuilt before both passing records above; the failed exploratory run is not counted as acceptance evidence.

This proves dependency-aware local readiness and recovery. It does not claim the Week 6 Nginx blue/green promotion, continuous-traffic cutover, or rollback gate.
