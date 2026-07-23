# Idempotent payment HTTP API

## Context

SolidarityGrid needs a public boundary that can accept a payment safely when a
client retries. Persistence is currently local to each node, and this slice
does not replicate or process payments.

## Decision

Expose a small Minimal API backed by Application use cases. HTTP models and
status-code mapping stay in Node; Application returns neutral typed outcomes.
New payments are persisted locally in `Received`.

## POST /pay

`POST /pay` accepts only amount and currency in its JSON body. The server
supplies the payment ID and UTC creation time.

## Idempotency-Key

The case-sensitive `Idempotency-Key` header is mandatory. It is not accepted in
the body and remains an opaque client token.

## Created response

A newly persisted payment returns `202 Accepted`, its resource location,
`Idempotency-Replayed: false`, and a payment representation identifying the
accepting node.

## Replay response

A compatible replay returns `200 OK`, the same resource location and payment
ID, and `Idempotency-Replayed: true`. Replay does not update timestamps,
version, state, or domain events.

## Conflict response

Reusing a key for another amount or currency returns `409 Conflict` with code
`IDEMPOTENCY_KEY_PAYLOAD_CONFLICT`.

## Query response

`GET /payments/{paymentId:guid}` returns the node-local payment or a `404`
Problem Details response with `PAYMENT_NOT_FOUND`.

## Durable uniqueness

The preliminary query is an optimization, not the guarantee. The local SQLite
unique index on `idempotency_key` is the authoritative guarantee.

## Concurrent requests

If two requests race, one insert wins. The loser receives the translated unique
constraint exception, loads the winner once, and returns replay or conflict
according to the stored payload. It never retries `SaveChanges`.

## Error contract

Input, not-found, idempotency, storage, and unexpected errors use Problem
Details with stable `code` and `correlationId` extensions. Internal SQLite
details and stack traces are not exposed.

## Correlation ID

The API preserves a nonblank incoming `X-Correlation-ID` or generates one and
returns it on every response.

## Separation from persistence

Endpoints depend on scoped Application use cases. They do not access
`DbContext`, SQLite connections, or concrete repositories.

## Current node-local limitation

Idempotency is local to one node. The same key sent to another node can create
an independent payment. There is no global idempotency promise until a later
replication slice.

## Alternatives considered

- Query-only idempotency was rejected because concurrent inserts can race.
- A process-wide lock was rejected because it cannot coordinate containers.
- Returning `202` for replay was rejected because replay does not create work.
- Exposing the aggregate directly was rejected to protect layer boundaries.
- A generic mediator and mapping framework were rejected as unnecessary.

## Consequences

- Clients must retain and consistently route their idempotency key.
- The API clearly distinguishes creation, replay, invalid input, missing data,
  and payload conflict.
- SQLite remains the local source of durability.
- The state remains `Received`; no processing occurs in this slice.

## Status

Accepted.
