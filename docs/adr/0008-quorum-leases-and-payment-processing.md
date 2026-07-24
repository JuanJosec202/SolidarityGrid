# Quorum leases and payment processing

## Context

Durable replication leaves a payment in `Replicated` on at least two independent
SQLite databases. The happy path now needs one temporary owner to perform the
simulated business effect without introducing a global coordinator.

## Decision

Coordinate claim, processing start, lease renewal and completion directly over
the existing gRPC mesh. Every confirmed mutation is persisted on at least one
peer before it is applied locally.

## Per-payment ownership

Ownership belongs to one payment, not to the cluster. Every node runs the same
worker and may discover the payment. There is no permanent leader role.

## Quorum intersection

The owner requires one remote acceptance plus its local acceptance. Any two
2-of-3 quorums share at least one node, whose durable active lease rejects an
incompatible claim.

Simultaneous remote-first claims can otherwise cross before either candidate
mutates locally. Candidates therefore wait in a deterministic per-payment
order derived from PaymentId and NodeId. The short stagger separates first
rounds without assigning ownership permanently; another candidate proceeds
when the preferred one cannot form quorum.

## Remote-first mutations

Claim, processing start, renewal and completion are sent to both peers in
parallel. One durable remote acknowledgement is required before applying the
identical mutation locally.

## Term

The first proposal is the stored term plus one. Stale responses expose their
current term, allowing one bounded second round with `max(currentTerm) + 1`.
There are never more than two rounds.

## Lease

The default lease lasts 4000 ms. It is the durable exclusion boundary stored in
the existing payment row. Losing renewal quorum stops simulated processing and
allows the lease to expire.

## Processing

After claim and start reach quorum, the owner waits for a fixed 8000 ms. No
external charge or side effect is performed.

## Lease renewal

The owner renews every 1000 ms using one timestamp and expiration across remote
and local copies. Each renewal requires one remote durable acknowledgement.

## Completion

At the end of the delay, `CompletePayment` must succeed durably on at least one
peer before `Payment.Complete` is persisted locally. Exact repeats from the
same owner and term are idempotent.

## Why no global leader

A leader would broaden failure handling and election concerns beyond the
payment happy path. Per-payment ownership uses the state already persisted with
the payment.

## Why no vote table

The payment row already stores the accepted owner, monotonic term and lease.
Another table would duplicate coordination state and add cleanup and
transactional consistency concerns to the PoC.

## Why fixed eight-second processing

Eight seconds lies within the required five-to-ten-second interval and makes
the demo deterministic while leaving enough time to observe renewals.

## Why sequential processing per node

One scoped worker processes discovered payments one by one. This avoids an
in-memory queue, overlapping cycles and premature local concurrency controls.

## At-least-once and idempotent completion

The design provides at-least-once processing with an idempotent durable
transition to `Completed`. It does not claim absolute exactly-once behavior for
future external effects.

## Current limitations

Expired `Claimed` or `Processing` payments are not scanned. There is no
automatic takeover, abandoned-payment recovery or historical replica
reconciliation.

## Alternatives considered

- A permanent cluster leader was rejected because ownership is payment-scoped.
- Raft and Paxos were rejected as disproportionate for this proof of concept.
- A vote or acknowledgement table was rejected because the existing payment
  row is sufficient for this slice.
- Local-first mutation was rejected because it can leave the only confirmed
  state on the node that is about to fail.
- Random processing time was rejected because it makes the demo less
  repeatable.

## Consequences

The happy path has one observable owner, renewable exclusion, bounded claim
rounds and remote-first completion. Processing is deliberately sequential and
availability after owner failure remains deferred.

## Status

Accepted.
