# Automatic payment takeover

## Context

SolidarityGrid ya procesa pagos bajo ownership por pago, lease durable, term
monotónico y quorum 2 de 3. Si el owner desaparece durante los ocho segundos de
trabajo, su última lease permanece en las réplicas, pero el pago necesita ser
recuperado sin introducir un coordinador central ni permitir que el owner
obsoleto complete después.

## Decision

El worker de procesamiento existente también consultará pagos `Claimed` o
`Processing` cuya lease haya vencido. Application evaluará su elegibilidad y
reutilizará `PaymentOwnershipCoordinator` y
`PaymentProcessingOrchestrator`. No se agregan estados, RPCs, tablas ni
migrations.

## Recovery eligibility

Un pago solo es elegible si está `Claimed` o `Processing`, tiene owner remoto,
su lease venció y el snapshot de ese owner existe con estado `Unreachable`.
`PaymentRecoveryPolicy` es pura: no consulta persistencia, reloj ni transporte.

## Unreachable signal

El detector de fallos aporta la señal de que un peer remoto dejó de ser
operable. `Alive`, `Suspected` y `Unknown` no habilitan recuperación.

## Expired lease

La consulta durable filtra por estado y `lease_expires_at_utc <= utcNow`.
El repositorio no conoce la salud del mesh.

## Why both conditions are required

El fallo de heartbeat por sí solo puede ser transitorio. La lease vencida por sí
sola no justifica desplazar a un owner que continúa `Alive`. Combinar ambas
señales reduce takeovers prematuros: health dispara recovery y la lease protege
la consistencia.

## Higher term

La primera propuesta es `payment.Term + 1`. Si un peer conoce un term superior,
la segunda y última ronda propone `max(currentTerm) + 1`.

## Fencing

`Payment.Term` funciona como fencing token. Después de un takeover, las
operaciones `StartProcessing`, `RenewLease` y `Complete` del owner anterior o
del term anterior se rechazan sin reducir owner, term, attempt ni version.

## Quorum intersection

El candidato aplica la claim primero en al menos un peer y después localmente.
Todo quorum 2 de 3 intersecta con otro quorum 2 de 3, de modo que una lease
activa con term superior bloquea al competidor tardío.

## Concurrent survivors

Los sobrevivientes pueden descubrir la misma fila. El backoff determinista
existente por PaymentId y NodeId ordena la contención; quorum, lease, term y
concurrencia optimista deciden el ganador. Para recovery, cada rango se separa
por una duración de lease, evitando claims cruzadas aunque los ciclos de
polling comiencen desfasados. No existe lock distribuido.

## Reusing the processing orchestrator

El ganador ejecuta el mismo flujo remoto-primero:
`StartPaymentProcessing`, renovaciones durante el delay y `CompletePayment`.
No existe un orquestador separado para recovery.

## Attempt semantics

`Claim` conserva `Attempt`. El siguiente `StartProcessing` lo incrementa de 1
a 2, haciendo observable que el trabajo simulado comenzó nuevamente.

## Abrupt process failure

La cancelación del host no completa ni compensa el pago. La última lease durable
queda intacta para que los sobrevivientes esperen su expiración.

## Docker kill

La demo usa `docker kill` sobre el contenedor owner detectado dinámicamente. El
owner permanece detenido durante la comprobación principal.

## At-least-once processing

La garantía es procesamiento al menos una vez con fencing por term. No se afirma
exactly-once absoluto ni se modela un efecto financiero externo.

## Idempotent completion

La completion requiere una aplicación remota durable antes de la local. Un
replay válido del mismo owner y term no crea otra transición de dominio.

## Stale owner rejection

Las operaciones tardías con term 1 se rechazan después de adquirir term 2. Los
logs técnicos usan `StaleOwnerOperationRejected`; solo el owner ganador emite
el evento principal `PaymentCompleted`.

## Current convergence limitation

La réplica del owner caído puede permanecer desactualizada. La PoC garantiza
completion durable en quorum, no convergencia histórica inmediata de las tres
copias. Tampoco recupera automáticamente una fila cuyo owner sea el propio nodo
local después de un reinicio.

## Network partitions

Una partición puede parecer una caída. Exigir lease vencida y quorum con term
superior impide que dos owners con fencing válido completen, aunque no elimina
la necesidad futura de reconciliación.

## Alternatives considered

- Un recovery worker separado: descartado porque duplicaría scopes, polling y
  orquestación.
- Un líder global o base compartida: descartados por introducir un coordinador
  central y otro punto de fallo.
- Una tabla de votos o leases: descartada porque las columnas actuales y la
  intersección de quorum son suficientes para la PoC.
- Raft, Paxos, Redis o un broker: descartados por exceder el alcance.
- Takeover solo por heartbeat o solo por lease: descartados por reaccionar ante
  señales incompletas.

## Consequences

Los pagos abandonados pueden completarse en dos sobrevivientes con owner nuevo,
term superior y `Attempt = 2`. El trabajo puede repetirse y una copia caída
puede quedar atrasada. La implementación conserva seis RPCs, una tabla
`payments`, un worker de procesamiento y SQLite independiente por nodo.

## Status

Accepted.
