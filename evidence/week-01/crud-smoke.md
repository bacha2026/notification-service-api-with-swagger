# Week 1 CRUD and OpenAPI smoke record

**Local result:** PASS on 2026-07-13. The pinned .NET 8 suite passed 72/72 tests, the application started against local SQL Server, and the hardened live smoke script passed 12/12 probes.

## Repeatable commands

```powershell
dotnet test tests\NSA.Tests\NSA.Tests.csproj
dotnet run --no-build --project NSA.csproj --no-launch-profile --urls http://127.0.0.1:5099
.\scripts\Invoke-Week12Smoke.ps1 -BaseUrl http://127.0.0.1:5099
```

The automated suite covers:

- notification create/read/list/update/delete;
- product catalog/detail/validation;
- cart and order supporting routes;
- order creation with cart clearing and two pending email intents while the provider is disabled;
- version-preserving create and bulk status locations;
- v1/v2 OpenAPI documents, schemas, required fields, documented response codes, and error media types;
- the bulk `202` lifecycle and terminal counts.

Primary test references are [ApiContractTests.cs](../../tests/NSA.Tests/ApiContractTests.cs), [DisabledEmailOrderWorkflowTests.cs](../../tests/NSA.Tests/DisabledEmailOrderWorkflowTests.cs), and [ApiExceptionContractTests.cs](../../tests/NSA.Tests/ApiExceptionContractTests.cs).

## Visual and machine-readable evidence

- [Swagger UI screenshot](swagger.png)
- [Live probe result](../week-02/live-contract-smoke.json)
- [Editable architecture source](../../drawio/notification.drawio)
- [Readable runtime architecture PDF](../../docs/architecture/week-02-runtime-architecture.pdf)

The screenshot and live result were captured from `http://127.0.0.1:5099`; they do not establish a public deployment URL.
