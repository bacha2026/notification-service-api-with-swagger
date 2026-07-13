# Week 2 error and version verification

**Local result:** PASS for the automated implementation checks on 2026-07-13. See the raw [12-probe live result](live-contract-smoke.json) and [API contract tests](../../tests/NSA.Tests/ApiContractTests.cs). External review remains pending.

## Error matrix

| Scenario | Actual status | Actual contract | Evidence |
| --- | ---: | --- | --- |
| Model validation / missing fields | 400 | `application/problem+json`; validation fields + trace ID | Live probe; `ApiContractTests` |
| Typed caller-correctable validation | 400 | Safe Problem Details detail + trace ID | `ApiExceptionContractTests.Typed_request_validation_exception...` |
| Generic `ArgumentException` / unexpected failure | 500 | Sanitized Problem Details; no exception detail | `ApiExceptionContractTests.Generic_argument_exception...` |
| Malformed JSON | 400 | Problem Details + trace ID | Live probe |
| Unsupported request media type | 415 | Problem Details + trace ID | Live probe |
| Missing entity | 404 | Problem Details + trace ID | Live v1.0 probe and integration tests |
| Unknown route | 404 | Problem Details + trace ID | Live probe |
| Unsupported API version | 404 | Problem Details + trace ID | Live `/api/v9/products` probe; any 4xx satisfies the plan |
| Unsupported HTTP method | 405 | Problem Details + trace ID | Live PATCH probe |
| Queue/downstream unavailable | 503 | Sanitized Problem Details + `Retry-After: 30` | `ApiExceptionContractTests.Temporary_capacity_exception...` and bulk capacity tests |
| Unexpected exception | 500 | Sanitized Problem Details + server-side error log | `ApiExceptionContractTests.Unexpected_exception...` |

The exception boundary discloses messages only from `RequestValidationException`; generic argument/programming failures are treated as sanitized 500 responses.

## Version matrix

| Check | Actual result | Evidence |
| --- | --- | --- |
| `/api/v1/...` | 200; `Deprecation: true`; `Sunset: Thu, 31 Dec 2026 23:59:59 GMT` | Live probe |
| `/api/v1.0/...` | Same v1 retirement policy, including on a 404 | Live probe |
| `/api/v2/...` | 200; no deprecation/sunset headers | Live probe |
| Unversioned compatibility route | 200; resolves to the documented default behavior | Live probe |
| v1/v2 OpenAPI JSON | Both 200; concrete versioned path sets | Live probe + `ApiContractTests` |
| Version-preserving `Location` | v1/v1.0/v2 create and bulk links retain the requested version | `ApiContractTests` |
| Model schemas | Required fields/constraints and `ValidationProblemDetails` are present | `ApiContractTests` and `ProblemDetailsOperationFilterTests` |

Re-run the live checks with:

```powershell
.\scripts\Invoke-Week12Smoke.ps1 -BaseUrl http://127.0.0.1:5099
```
