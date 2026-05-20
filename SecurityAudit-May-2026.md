# JadeDbClient Security Audit — May 2026

**Repository:** `KaustavCodes/JadeDbClient-DotNet`  
**Audit date:** 2026-05-20  
**Auditor:** GitHub Copilot Task Agent (AI-assisted static review)  
**Scope:** .NET database client connectivity layer (`JadeDbClient`), query builder, connection/configuration patterns, dependency posture.

---

## 1) Executive Summary

This review found **no critical remote-code-execution class issues**, but identified **high-priority SQL injection risk surfaces around dynamic SQL identifiers** in bulk APIs where `tableName` and column names are interpolated into SQL/COPY statements without strict identifier allowlisting.

The library is otherwise strong on value-parameterization and has explicit hardening in query builder expression translation, but several controls are still developer-dependent (TLS, logging hygiene, connection timeout limits).

---

## 2) Methodology (May 2026)

- Static code review of core files in:
  - `JadeDbClient/*.cs`
  - `JadeDbClient/Helpers/*.cs`
  - `JadeDbClient/Initialize/*.cs`
  - `JadeDbClient.Tests/*.cs`
- Baseline validation:
  - `dotnet restore JadeDbClient.sln`
  - `dotnet build JadeDbClient.sln --configuration Release --no-restore`
  - `dotnet test JadeDbClient.sln --configuration Release --no-build`
- Dependency checks:
  - `dotnet list JadeDbClient/JadeDbClient.csproj package --vulnerable --include-transitive`
  - `dotnet list JadeDbClient/JadeDbClient.csproj package --outdated --include-transitive`

---

## 3) Key Findings

## ✅ Strengths

1. **Parameterized query values used broadly** across SQL Server, MySQL, and PostgreSQL service methods (`ExecuteQuery*`, `ExecuteStoredProcedure*`, `ExecuteCommandAsync`).
2. **QueryBuilder value hardening** includes:
   - LIKE wildcard escaping (`ExpressionToSqlVisitor.cs`)
   - Empty `IN (...)` protection (`1=0` guard)
   - `UPDATE`/`DELETE` requires `WHERE` (`QueryBuilder.cs`).
3. **Security-focused tests exist** (`JadeDbClient.Tests/QueryBuilderSecurityTests.cs`) for identifier validation and LIKE escaping behavior.
4. **No known vulnerable packages** reported by `dotnet list package --vulnerable` at audit time.

## ⚠️ Findings Requiring Action

### F1 — SQL Injection Surface via Unvalidated Dynamic Identifiers (High)
**Severity:** High  
**CWE:** CWE-89 (SQL Injection), CWE-20 (Improper Input Validation)

**Issue:** Multiple bulk APIs directly interpolate `tableName` and/or column identifiers into SQL/COPY command text without strict allowlist validation.

**Evidence examples:**
- `JadeDbClient/MySqlDbService.cs`
  - `INSERT INTO \`{tableName}\`` (e.g., around lines ~501, ~553, ~759, ~930)
- `JadeDbClient/PostgreSqlDbService.cs`
  - `COPY {tableName} (...) FROM STDIN` (e.g., around lines ~494, ~531, ~617, ~651, ~721, ~770)
- `JadeDbClient/MsSqlDbService.cs`
  - `SqlBulkCopy.DestinationTableName = tableName` (e.g., around lines ~551, ~712)

**Impact:** If user-controlled or attacker-influenced identifier input reaches these APIs, object-name injection can occur despite value parameterization.

**Recommended remediation:**
- Introduce a **shared strict SQL identifier validator** for table/schema/column names.
- Reject unsafe characters/tokens (whitespace/control chars/comments/statement delimiters).
- Prefer API designs that accept known model metadata or trusted enum names over raw identifier strings.
- Consider separate “unsafe/advanced” APIs with explicit warnings if raw SQL identifiers must be allowed.

---

### F2 — QueryBuilder Metadata Identifier Trust Boundary (Medium)
**Severity:** Medium  
**CWE:** CWE-89, CWE-20

**Issue:** `ReflectionHelper.GetTableName()` and `GetColumnName()` return attribute values directly (`JadeDbClient/Helpers/ReflectionHelper.cs`) and those names are used in generated SQL (`JadeDbClient/Helpers/QueryBuilder.cs`) without equivalent validation.

**Impact:** In typical use this is low risk (developers control attributes at compile time), but in multi-tenant plugin/dynamic model scenarios this can become an injection boundary.

**Recommended remediation:**
- Validate or normalize table/column names resolved from attributes before SQL generation.
- Reuse same identifier validator across QueryBuilder and bulk APIs.

---

### F3 — Sensitive Data Exposure via Optional Query Logging (Medium)
**Severity:** Medium  
**CWE:** CWE-532 (Information Exposure Through Log Files)

**Issue:** When enabled, executed SQL text is written to console:
- `MsSqlDbService.cs` / `MySqlDbService.cs` / `PostgreSqlDbService.cs`
  - `Console.WriteLine(... Executed Query: {query})`

**Impact:** Query text can include literals and sensitive business data depending on caller behavior.

**Current mitigation:** Logging is disabled by default (`JadeDbServiceRegistration.JadeDbServiceOptions`).

**Recommended remediation:**
- Keep disabled in production by policy.
- Add optional redaction/sanitization hooks before log emission.
- Route through structured logging interfaces with sensitivity classification.

---

### F4 — No Explicit Command Timeout Controls (Medium)
**Severity:** Medium  
**CWE:** CWE-400 (Uncontrolled Resource Consumption)

**Issue:** Command execution paths create commands but do not set `CommandTimeout` explicitly in core query methods.

**Impact:** Long-running queries can amplify DoS/resource exhaustion risk in degraded DB/network conditions.

**Recommended remediation:**
- Add configurable per-service command timeout defaults.
- Expose timeout settings in registration options for all providers.

---

### F5 — TLS/Encryption Is User-Configured, Not Enforced by Library (Low/Policy)
**Severity:** Low  
**Category:** Hardening gap

**Issue:** Connection strings are consumed as provided from configuration. Encryption posture is not enforced centrally.

**Impact:** Misconfigured consumers may run plaintext DB sessions in production.

**Recommended remediation:**
- Add optional startup validation mode (e.g., enforce `Encrypt=True` / SSL settings per provider in production).
- Emit warnings when insecure connection string settings are detected.

---

## 4) Dependency Posture (May 2026 Snapshot)

- **Known vulnerable packages:** none detected by `dotnet list ... --vulnerable`.
- **Outdated packages:** several top-level/transitive dependencies have newer versions (including `Microsoft.Data.SqlClient`, `Npgsql`, and `Microsoft.Extensions.*` families).

**Recommendation:** perform a controlled dependency update cycle with compatibility testing, especially for security-sensitive provider packages.

---

## 5) Risk Rating Summary

| Area | Rating |
|---|---|
| SQL value parameterization | Low risk / strong |
| Dynamic identifier handling (bulk APIs) | **High risk** |
| QueryBuilder expression hardening | Low risk / strong |
| Logging confidentiality | Medium risk |
| Timeout/availability controls | Medium risk |
| Dependency known-vuln status (audit date) | Low risk |

---

## 6) Prioritized Remediation Plan

1. **Immediate (P0):** Add centralized safe identifier validation and enforce in all `tableName`/column-path APIs.
2. **Near-term (P1):** Add command timeout configuration + secure defaults.
3. **Near-term (P1):** Add production logging safeguards/redaction and policy docs.
4. **Medium-term (P2):** Add optional startup checks for secure DB transport settings.
5. **Continuous (P2):** Regular dependency refresh + vulnerability scanning in CI.

---

## 7) Final Assessment

JadeDbClient is generally well-structured for parameterized query execution and has meaningful security-focused test coverage.  
The primary security gap is **identifier-level SQL construction in bulk operations**, which should be treated as the top remediation item for 2026 hardening.

> This is an AI-assisted static analysis report and not a penetration test or formal third-party certification.
