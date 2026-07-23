# Durable payment replication

## Context

A payment previously existed only on the node receiving `POST /pay`. Future
processing needs evidence that the payment survives one node loss.

## Decision

Persist locally first, replicate directly to both peers, and mark the local
payment `Replicated` only after at least one remote durable acknowledgement.

## ReplicatePayment RPC

The mesh service now contains exactly `Probe` and `ReplicatePayment`.
`ReplicatePayment` carries neutral payment identity and timestamps; it does not
carry owner, term, lease, attempt, votes, or processing commands.

## Local persistence first

The receiving node saves `Received` before network calls. A failed quorum never
removes that durable local copy.

## Remote durable acknowledgement

`stored=true` is returned only after SQLite `SaveChangesAsync` completes and the
remote payment is at least `Replicated`.

## Quorum 2 of 3

```text
node-a local
   +
node-b durable ack
   =
quorum 2/3
```

## Why one remote acknowledgement is sufficient

The local durable copy plus one remote durable copy tolerate one unavailable
node while keeping the PoC's fixed three-node topology.

## Idempotent replicas

An exact replay returns success without adding a row, incrementing Version, or
changing UpdatedAtUtc after the first transition.

## Same payment identity

PaymentId, IdempotencyKey, Money and CreatedAtUtc are preserved. A different
payload or the same key with a different ID is rejected as a conflict.

## Shared replication timestamp

The coordinator selects one ReplicatedAtUtc and sends it to peers before using
the same value for the local `MarkReplicated` transition.

## HTTP behavior without quorum

Without a remote acknowledgement, `POST /pay` returns 503 Problem Details with
`PAYMENT_REPLICATION_QUORUM_UNAVAILABLE`, `Retry-After: 1`, and a Location for
the local `Received` resource. Replaying the same key retries replication.

## Why health status is not authoritative

Failure detection is observational and may confuse partitions with failure.
Replication always attempts both RPCs and counts actual durable responses.

## Why no retry worker yet

Explicit client replay is enough for this slice. Background reconciliation,
outbox delivery and recovery workers would introduce additional semantics.

## Why no new tables

The existing payments table already provides durable identity, idempotency and
state. Replica acknowledgements are not persisted.

## Current limitations

There is no historical reconciliation for a returning node, processing,
coordinated owner, leases, votes, takeover or cross-node conflict resolution.
Simultaneous creation of the same key on different nodes before replication is
outside the primary PoC scenario.

## Alternatives considered

A central database and external broker were rejected because they would become
coordination authorities. Requiring all three nodes was rejected because it
would prevent progress during one-node failure. Raft, CRDTs and a generic
consensus engine were rejected as unnecessary for this slice.

## Consequences

Accepted payments have at least two durable copies. A node that missed a
replication remains stale until a later reconciliation slice. Replication quorum
is a durability signal, not ownership consensus.

## Status

Accepted.
