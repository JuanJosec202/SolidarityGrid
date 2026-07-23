# ADR 0001: Distributed mesh foundation

- Estado: Accepted
- Fecha: 2026-07-23

## Contexto

SolidarityGrid es una prueba de concepto de procesamiento resiliente de pagos.
Debe ejecutar tres nodos idénticos, permitir comunicación directa entre ellos y
evolucionar hacia tolerancia a fallos sin depender de un broker ni de una base
de datos central coordinadora.

El primer slice necesita una base compilable, testeable y operable antes de
introducir el dominio de pagos o mecanismos distribuidos.

## Decisión

- Usar .NET 8 y ASP.NET Core Minimal APIs.
- Organizar la solución con Clean Architecture pragmática: Domain, Application,
  Infrastructure, Contracts y Node.
- Ejecutar tres instancias idénticas mediante Docker Compose y una sola imagen.
- Mantener a Node como composition root.
- Preparar una futura comunicación P2P con gRPC.
- Preparar persistencia local futura con SQLite independiente por nodo.
- No usar brokers externos.
- Diseñar una coordinación futura con quórum de 2 de 3, sin implementar Raft
  completo.
- No usar una base de datos central como coordinador.
- Automatizar restore, formato, build, pruebas y validación de Compose desde el
  inicio.

gRPC, SQLite y el quórum son decisiones de dirección arquitectónica; no se
implementan ni se agregan como dependencias en este slice.

## Alternativas consideradas

### Broker externo

RabbitMQ, Kafka y servicios similares simplificarían parte de la mensajería,
pero ocultarían el problema de coordinación directa que evalúa la prueba.

### Base de datos central

Una base compartida facilitaría la exclusión mutua, pero introduciría un
coordinador central y un punto único de fallo contrario al objetivo.

### Consenso Raft completo

Raft ofrece garantías formales, aunque su complejidad no es proporcional a esta
prueba de concepto de tres nodos. Se priorizará un protocolo acotado y
observable.

### Solución monolítica de un solo proyecto

Reduciría archivos inicialmente, pero debilitaría las reglas de dependencia que
deben sostener los siguientes slices.

## Consecuencias

- Cada nodo se configura de forma independiente, aunque ejecuta el mismo
  artefacto.
- Las fronteras de proyecto se validan con pruebas de arquitectura.
- Los mecanismos distribuidos deberán respetar los puertos de Application.
- Compose sirve como entorno reproducible de desarrollo y demostración.
- Existirá más estructura inicial, compensada por menor acoplamiento futuro.
- Las garantías de replicación, quórum y failover quedan explícitamente fuera de
  este slice.
