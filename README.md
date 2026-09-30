# swiftbets-risk

[![ci](https://github.com/remonenaidoo/swiftbets-risk/actions/workflows/ci.yml/badge.svg)](https://github.com/remonenaidoo/swiftbets-risk/actions/workflows/ci.yml)

Real-time, advisory risk for SwiftBets on Akka.NET: one actor per fixture holding liability per outcome, sharding-ready addressing, and pattern detection on the placed stream (stake clustering, late steam, correlated accas). Placement keeps the hard limits; this service watches and alerts.

## Hosts

- `SwiftBets.Risk.Worker`: the actor system host.

## Data and events

- **Owns:** Actor snapshots in Postgres (Phase 8).
- **Events:** Consumes `placement.coupon-placed`, `offer.price-changed`, `settlement.coupon-settled`; produces `risk.liability-changed`, `risk.risk-alert`.

## Layout

Clean Architecture, enforced by project references and `*.ArchitectureTests`:

```
src/*.Domain          pure domain, no references
src/*.Application     use cases and ports; depends on Domain and contracts only
src/*.Infrastructure  adapters (Dapper + embedded .sql, Kafka, Redis); implements Application ports
src/*.Api | *.Worker  composition root: observability, error envelope, health, metrics
src/*.Migrator        DbUp scripts under Migrations/, run once before the host starts
```

Every host exposes `/health/live`, `/health/ready` (checks its real dependencies), `/metrics` (Prometheus), logs compact JSON with correlation ids, and exports traces over OTLP.

## Build and test

```bash
../swiftbets-platform/scripts/fetch-shared-packages.sh .   # or pack-local.sh for unreleased shared changes
dotnet test SwiftBets.Risk.slnx
```

Integration tests use Testcontainers and need Docker. The whole platform runs from `swiftbets-platform` with `make up`.

## Images

Multi-arch (amd64 + arm64), non-root, chiseled runtime:

- `ghcr.io/remonenaidoo/swiftbets-risk`

## License

MIT
