# SolidarityGrid

## Objetivo

SolidarityGrid será una red P2P de procesadores de pagos resilientes. El Slice 0
estableció su fundación técnica y el Slice 1 incorporó el dominio del pago, su
máquina de estados y las reglas de idempotencia. El Slice 2 aporta persistencia
SQLite durable e independiente por nodo. El Slice 3 expone una API pública
idempotente para crear y consultar pagos locales. El Slice 4 agrega transporte
mesh directo mediante gRPC. El Slice 5 incorpora el detector periódico de fallos
y el Slice 6 replica cada pago de forma durable hasta alcanzar quorum 2 de 3.
El Slice 7 agrega ownership por pago, leases renovables y procesamiento normal
coordinado mediante ese quorum. El Slice 8 recupera automáticamente pagos
abandonados combinando el detector de fallos, leases vencidas, quorum y fencing.

## Slices completados

- Slice 0: fundación .NET 8, nodos idénticos, Docker y automatización.
- Slice 1: agregado `Payment`, value objects, ownership, lease, term, eventos de
  dominio y política de idempotencia.
- Slice 2: SQLite local por nodo, migrations, rehidratación, idempotencia durable
  y concurrencia optimista.
- Slice 3: `POST /pay`, consulta por ID, replay durable y contratos Problem
  Details.
- Slice 4: contrato protobuf versionado, `Probe` gRPC, identidad de proceso,
  canales HTTP/2 reutilizables y diagnóstico de conectividad.
- Slice 5: heartbeats, estados de salud, recuperación y detección de reinicios.
- Slice 6: replicación gRPC durable e idempotente con quorum 2 de 3.
- Slice 7: ownership por pago, lease temporal y procesamiento coordinado de
  ocho segundos hasta `Completed`.
- Slice 8: takeover automático de pagos abandonados con term superior,
  procesamiento al menos una vez y completion idempotente.

## Requisitos

- .NET 8 SDK.
- Docker.
- Docker Compose.

## Ejecución local

```bash
dotnet run --project src/SolidarityGrid.Node
```

## Ejecución con tres nodos

```bash
docker compose up --build
```

Docker Compose monta `node-a-data`, `node-b-data` y `node-c-data` en `/data`.
Aunque los tres contenedores usan el mismo nombre interno de archivo, sus
volúmenes son independientes.

## Endpoints iniciales

- node-a: <http://localhost:5101>
- node-b: <http://localhost:5102>
- node-c: <http://localhost:5103>

Cada nodo expone `/`, `/node`, `/health/live`, `/health/ready`, `POST /pay` y
`GET /payments/{paymentId}` en su puerto público HTTP/1.1. También expone
`GET /mesh/peers` para ejecutar un probe bajo demanda.

## Comunicación mesh

Los nodos se comunican directamente mediante gRPC sobre HTTP/2, sin broker ni
service mesh externo. Kestrel separa la API pública en el puerto interno `8080`
del transporte gRPC en `8081`. Docker publica únicamente `8080`; `8081` queda
expuesto solo dentro de la red de Compose.

Infrastructure mantiene un `GrpcChannel` reutilizable por URI de peer. Cada
probe tiene deadline, respeta cancelación y consulta los dos peers en paralelo,
sin retries automáticos.

## Probe

```http
GET /mesh/peers
```

La respuesta siempre contiene un resultado por peer. Una caída se representa
con `isReachable=false` y un código neutral como
`MESH_PEER_UNREACHABLE`, `MESH_DEADLINE_EXCEEDED`,
`MESH_PROTOCOL_MISMATCH` o `MESH_IDENTITY_MISMATCH`; no convierte la respuesta
completa en 500 ni afecta readiness.

## Heartbeats

Cada nodo ejecuta el RPC `Probe` periódicamente contra sus peers, sin retries y
sin broker. El intervalo predeterminado es 1000 ms. El ciclo siguiente comienza
solo cuando termina el anterior.

## Estados del detector

- `Unknown`: aún no existe evidencia suficiente.
- `Alive`: existe una observación válida reciente y no se superó el umbral de
  sospecha.
- `Suspected`: los fallos transitorios superaron 3000 ms.
- `Unreachable`: los fallos superaron 5000 ms o existe un error inmediato de
  identidad, protocolo o permisos.

Una falla aislada conserva `Alive`, pero incrementa sus contadores y expone el
último error. `Alive` no implica que la última llamada haya sido exitosa.

## Estado cacheado

`GET /mesh/peers` ejecuta probes activos bajo demanda. `GET /mesh/status` no
genera tráfico gRPC: devuelve los snapshots cacheados por el detector periódico.

El estado es local y efímero. Al reiniciar el monitor comienza otra vez en
`Unknown`. Un cambio de `InstanceId` remoto incrementa `RestartCount`, pero ese
contador no se persiste.

La salud de los peers no participa en `/health/ready`; un nodo con SQLite local
disponible continúa ready aunque uno o dos peers estén `Unreachable`.

## Identidad mesh

`NodeId` es estable y proviene de configuración. `InstanceId` se genera una vez
por proceso, permanece estable durante esa ejecución y cambia cuando el
contenedor reinicia. El cliente valida NodeId, InstanceId y versión de protocolo
de cada respuesta.

## Seguridad mesh

El PoC confía en la red interna de Docker. NodeId no es autenticación
criptográfica. En producción se requeriría mTLS o identidad de workload para
impedir la suplantación de NodeId.

## API pública

`POST /pay` guarda primero el pago localmente y luego intenta replicarlo.
`GET /payments/{paymentId}` consulta el recurso en el mismo nodo. Un identificador
que no cumple la constraint `:guid` no coincide con la ruta y retorna 404.

## Replicación

Cada pago se envía directamente por gRPC a los dos peers en paralelo, sin broker
y sin retries automáticos. El receptor responde solo después de guardar la misma
identidad, clave, amount, currency y timestamps en su SQLite local. Las réplicas
son idempotentes y quedan al menos en estado `Replicated`.

## Quorum

```text
node local + un peer con confirmación durable = quorum 2 de 3
```

No es necesario que los tres nodos confirmen. El estado del failure detector es
diagnóstico: siempre se intenta la llamada gRPC y el quorum utiliza el resultado
real del transporte.

`POST /pay` retorna `202 Accepted` para una creación solo cuando existe una
segunda copia durable. Sin confirmación remota retorna `503 Service Unavailable`;
el pago permanece localmente en `Received` y puede reintentarse con la misma
`Idempotency-Key`. Un replay que alcanza quorum retorna `200 OK`, conserva el
PaymentId y muestra estado `Replicated`, versión 2.

## Ownership

El ownership se adquiere por pago; no existe un líder global. Un candidato
primero obtiene una claim durable en al menos un peer y después aplica la misma
claim localmente, formando quorum 2 de 3.

Cada claim usa un término monotónico y una lease temporal. Para evitar claims
cruzadas sin una tabla de votos, los candidatos aplican un backoff determinista
por PaymentId y NodeId antes de competir. El orden cambia por pago y solo separa
la primera ronda; no asigna ownership ni crea un servicio de liderazgo
permanente.

## Procesamiento

Cada nodo ejecuta un worker local secuencial. El worker consulta pagos
`Replicated` para el flujo inicial y pagos `Claimed` o `Processing` con lease
vencida para recovery. Intenta adquirir ownership y, si gana, propaga
`StartPaymentProcessing` antes de aplicar `StartProcessing` localmente.

El efecto simulado dura exactamente 8000 ms. Durante ese intervalo la lease de
4000 ms se renueva cada 1000 ms, siempre primero en al menos un peer y luego
localmente. Si una renovación pierde quorum, el nodo deja de procesar y permite
que la lease expire.

## Completion

Al terminar el delay, el owner confirma `CompletePayment` durablemente en al
menos un peer y solo entonces completa su copia local. Los replays con el mismo
owner y term son idempotentes.

## Automatic takeover

Un pago remoto abandonado solo es candidato cuando se cumplen simultáneamente:

- su estado es `Claimed` o `Processing`;
- su lease durable ya venció;
- su owner remoto aparece `Unreachable`.

`Alive`, `Suspected`, `Unknown`, una lease activa o un owner local impiden el
takeover. El heartbeat funciona como señal de recuperación; la lease es la
protección de consistencia. El nodo ganador adquiere quorum 2 de 3 con un term
estrictamente superior y reutiliza el mismo orquestador de procesamiento.

## Fencing

`Payment.Term` es el fencing token. Tras pasar de term 1 a term 2, una renovación
o completion atrasada con term 1 se rechaza y no puede reducir owner, term,
attempt ni version. Los RPCs existentes son suficientes; no existe un endpoint
manual de takeover.

## At-least-once

El trabajo simulado comienza nuevamente desde cero tras el takeover. Por eso
`Attempt` pasa de 1 a 2, mientras la completion continúa siendo durable en
quorum e idempotente. La PoC no afirma exactly-once absoluto. Un proveedor de
pagos externo real también debería aceptar una clave de idempotencia.

Flujo de recuperación:

```text
Processing term 1
→ owner killed
→ Unreachable
→ lease expired
→ takeover term 2
→ Processing attempt 2
→ Completed
```

## Garantía

La PoC ofrece procesamiento al menos una vez con una transición idempotente a
`Completed`. No afirma exactly-once absoluto frente a efectos externos.

## Idempotency-Key

El header `Idempotency-Key` es obligatorio, opaco y case-sensitive. Repetir la
misma clave con el mismo amount y currency retorna el pago existente sin
modificarlo. Reutilizarla con otro payload retorna conflicto. La garantía local
proviene del índice único de SQLite, no solamente de una consulta previa.

## Ejemplos

Curl:

```bash
curl -i -X POST http://localhost:5101/pay \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: PAY-2026-0001" \
  -d '{"amount":150000,"currency":"COP"}'
```

PowerShell:

```powershell
$headers = @{ "Idempotency-Key" = "PAY-2026-0001" }
$body = @{ amount = 150000; currency = "COP" } | ConvertTo-Json
Invoke-RestMethod -Uri http://localhost:5101/pay `
    -Method Post -Headers $headers -ContentType application/json -Body $body
```

También puede ejecutarse la colección [SolidarityGrid.http](SolidarityGrid.http).

## Respuestas

- `202 Accepted`: pago creado.
- `200 OK`: replay compatible o consulta encontrada.
- `400 Bad Request`: header, payload o JSON inválido.
- `404 Not Found`: pago ausente en el nodo consultado.
- `409 Conflict`: clave reutilizada con otro payload.
- `503 Service Unavailable`: no existe una segunda copia durable; puede
  reintentarse con la misma clave.

Todos los errores del endpoint incluyen Problem Details con `code` y
`correlationId`.

## Script demo

Con los contenedores activos:

```powershell
.\scripts\demo-api.ps1
```

```bash
./scripts/demo-api.sh
```

Para demostrar conectividad mesh, aislamiento de una caída y cambio de
`InstanceId` al reiniciar `node-b`:

```powershell
.\scripts\demo-mesh.ps1
```

```bash
./scripts/demo-mesh.sh
```

Para demostrar la línea temporal `Alive → Suspected → Unreachable → Alive`:

```powershell
.\scripts\demo-heartbeats.ps1
```

```bash
./scripts/demo-heartbeats.sh
```

Para demostrar replicación normal, quorum con un peer caído, 503 sin quorum y
recuperación mediante replay:

```powershell
.\scripts\demo-replication.ps1
```

```bash
./scripts/demo-replication.sh
```

Para demostrar ownership exclusivo, renovaciones de lease, procesamiento de
ocho segundos y una única completion:

```powershell
.\scripts\demo-processing.ps1
```

```bash
./scripts/demo-processing.sh
```

Para demostrar el takeover completo mediante una caída abrupta con
`docker kill`, term superior, attempt 2, completion única y aceptación de otro
pago por los sobrevivientes:

```powershell
.\scripts\demo-failover.ps1
```

```bash
./scripts/demo-failover.sh
```

## Persistencia local por nodo

Cada nodo escribe en su propio archivo SQLite; no existe una base central ni un
volumen compartido. Las conexiones activan WAL, foreign keys y un busy timeout
configurable. En ejecución local, la ruta predeterminada es
`src/SolidarityGrid.Node/data/solidarity-grid.db`; Docker usa
`/data/solidarity-grid.db` dentro del volumen privado de cada nodo.

## Idempotencia durable

`payments.idempotency_key` posee un índice único con collation `BINARY`. Las
claves son case-sensitive, por lo que `PAY-1` y `pay-1` pueden coexistir. Una
violación de unicidad se traduce a
`DuplicatePaymentIdempotencyKeyException`, sin filtrar tipos SQLite hacia
Application.

## Concurrencia optimista

`Payment.Version` es el único concurrency token y lo incrementa el dominio. Si
dos contextos cargan la misma versión, conserva el primer cambio confirmado y
el segundo recibe `PaymentConcurrencyException`.

## Migrations

La herramienta EF Core está fijada en el manifiesto local. Para restaurarla y
listar las migrations sin crear una base dentro del repositorio:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations list --project .\src\SolidarityGrid.Infrastructure --startup-project .\src\SolidarityGrid.Node --context SolidarityGridDbContext
```

La aplicación ejecuta `Database.MigrateAsync` antes de aceptar tráfico.

## Verificación

En Windows:

```powershell
.\scripts\local-ci.ps1
```

En Linux:

```bash
./scripts/local-ci.sh
```

Use `-BuildImage` en PowerShell o `--build-image` en Bash para construir también
la imagen.

## Arquitectura

```mermaid
graph LR
    Client[Cliente futuro] --> A[node-a]
    A <--> B[node-b]
    B <--> C[node-c]
    C <--> A
```

La solución aplica Clean Architecture de forma pragmática. `SolidarityGrid.Node`
es el composition root; Domain permanece aislado y las capacidades técnicas se
ubicarán detrás de puertos de Application.

La máquina de estados implementada es:

```mermaid
stateDiagram-v2
    [*] --> Received
    Received --> Replicated
    Replicated --> Claimed
    Claimed --> Processing
    Processing --> Claimed: lease expired + higher term
    Processing --> Completed
    Claimed --> Claimed: lease expired + higher term
```

La decisión completa está documentada en
[ADR 0002: Payment lifecycle and idempotency](docs/adr/0002-payment-lifecycle-and-idempotency.md).
El contrato público está documentado en
[ADR 0004: Idempotent payment HTTP API](docs/adr/0004-idempotent-payment-http-api.md).
El transporte interno está documentado en
[ADR 0005: Direct gRPC mesh transport](docs/adr/0005-direct-grpc-mesh-transport.md).
El detector periódico está documentado en
[ADR 0006: Heartbeat failure detector](docs/adr/0006-heartbeat-failure-detector.md).
La replicación durable está documentada en
[ADR 0007: Durable payment replication](docs/adr/0007-durable-payment-replication.md).
El ownership y procesamiento están documentados en
[ADR 0008: Quorum leases and payment processing](docs/adr/0008-quorum-leases-and-payment-processing.md).
El takeover automático está documentado en
[ADR 0009: Automatic payment takeover](docs/adr/0009-automatic-payment-takeover.md).

## Limitaciones

- No existe reconciliación inmediata del nodo caído; su copia puede quedar
  desactualizada. La completion durable en quorum no implica convergencia
  histórica inmediata de las tres copias.
- Si un proceso reinicia conservando una fila donde él mismo era el owner, la
  recuperación local queda para una estrategia posterior de reconciliación.
- Una partición de red puede parecer una caída. La intersección de quorum y el
  fencing evitan dos completions válidas con términos distintos.
- No existe un efecto financiero externo real.
- Solicitudes simultáneas con la misma clave en nodos distintos antes de
  replicarse quedan fuera del escenario principal.

## Roadmap

- Reconciliación histórica y observabilidad distribuida.
- Endurecimiento del transporte e identidad entre nodos.
- Pruebas de particiones y recuperación prolongada.

No se asocian fechas a estos slices; cada uno deberá conservar los límites de
arquitectura definidos aquí.
