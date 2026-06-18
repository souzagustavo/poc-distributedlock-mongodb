# Distributed Lock POC — MongoDB + .NET Aspire

POC de lock distribuído usando [`DistributedLock.MongoDB`](https://github.com/madelson/DistributedLock/tree/master/src/DistributedLock.MongoDB) com .NET 10 e Aspire para orquestração do MongoDB.

## Stack

| Componente | Versão |
|---|---|
| .NET | **10.0** |
| Aspire | **13.4.5** |
| DistributedLock.MongoDB | 1.0.1 |
| MongoDB.Driver | 3.4.0 |
| Scalar.AspNetCore | 2.11.3 |
| OpenTelemetry | 1.15.x |
| FluentAssertions | 8.10.0 |

> Todas as versões são gerenciadas em `Directory.Packages.props` (Central Package Management).

## Estrutura

```
DistributedLockPoc/
├── src/
│   ├── DistributedLockPoc.AppHost/      # Aspire orchestrator (MongoDB + API)
│   ├── DistributedLockPoc.Api/          # Minimal API com os endpoints
│   └── DistributedLockPoc.ServiceDefaults/  # OpenTelemetry, health checks
├── tests/
│   └── DistributedLockPoc.IntegrationTests/ # Testes integrados via Aspire.Hosting.Testing
└── k6/
    └── load-test.js                     # Cenários de carga com k6
```

## Caso de uso

Um contador compartilhado (`Counter`) é incrementado por múltiplas instâncias da API em paralelo.  
Sem lock → race condition (lost updates).  
Com `MongoDistributedLock` → incrementos serializados, valor final sempre correto.

## Como rodar

### Pré-requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10)
- [Docker](https://www.docker.com/get-started) (para o MongoDB via Aspire)
- [k6](https://k6.io/docs/getting-started/installation/) (para testes de carga)

### 1. Rodar a aplicação

```bash
cd src/DistributedLockPoc.AppHost
dotnet run
```

O Aspire sobe automaticamente:
- **MongoDB** (container Docker)
- **Mongo Express** (UI web do MongoDB)
- **API** com Scalar UI em `http://localhost:{porta}/scalar`
- **Aspire Dashboard** em `http://localhost:15000`

### 2. Testar manualmente

```bash
# Incrementar com lock (seguro)
curl -X POST http://localhost:5000/counters/meu-contador/increment

# Incrementar sem lock (race condition)
curl -X POST http://localhost:5000/counters/meu-contador/increment-unsafe

# Ver valor atual
curl http://localhost:5000/counters/meu-contador

# Resetar
curl -X DELETE http://localhost:5000/counters/meu-contador
```

### 3. Testes integrados

```bash
cd tests/DistributedLockPoc.IntegrationTests
dotnet test --logger "console;verbosity=detailed"
```

Os testes usam `Aspire.Hosting.Testing` para subir o stack completo (MongoDB + API) automaticamente — sem mocks.

#### Cenários cobertos

| Teste | O que valida |
|---|---|
| `IncrementWithLock_SingleRequest` | Happy path básico |
| `IncrementWithLock_Sequential` | 10 incrementos sequenciais = valor 10 |
| `IncrementWithLock_Concurrent_NoLostUpdates` | 30 requests paralelos → sem lost updates |
| `IncrementWithoutLock_Concurrent_LikelyLosesUpdates` | Documenta a race condition |
| `IncrementWithLock_HighConcurrency` | 50 VUs simultâneos, zero falhas |
| `GetCounter_NotFound_Returns404` | 404 para counter inexistente |
| `ResetCounter` | Delete + re-increment = 1 |

### 4. Teste de carga com k6

```bash
# Com a API rodando (passo 1)
k6 run k6/load-test.js

# Apontar para URL customizada
k6 run --env BASE_URL=http://localhost:5123 k6/load-test.js

# Com dashboard em tempo real
k6 run --out dashboard k6/load-test.js
```

#### Cenários do k6

| Cenário | VUs | Duração | Endpoint |
|---|---|---|---|
| `locked_ramp` | 1 → 100 | 90s | `/increment` (com lock) |
| `locked_spike` | 200 | 20s | `/increment` (com lock) |
| `race_demo` | 50 | 30s | `/increment-unsafe` (sem lock) |

#### Thresholds configurados

- `p(95)` do tempo de resposta do endpoint com lock < 2s (inclui espera pelo lock)
- Taxa de sucesso > 99%
- HTTP errors < 1%

#### Interpretando os resultados

Ao final do teste, o teardown exibe:

```
📊 Final Results
🔒 Locked  counter value : 12500   ← deve ser igual ao total de requests
⚠️  Unsafe  counter value : 11873   ← tipicamente MENOR (lost updates)
```

A diferença no contador unsafe (`12500 - 11873 = 627`) representa as atualizações perdidas por race condition.

## Como o lock funciona

```csharp
var lockName = $"counter:{counterName}";

 var @lock = providerLock.CreateLock(lockName);
// ↑ cria lock

await using var handle = await @lock.AcquireAsync(cancellationToken: ct);
// ↑ bloqueia até conseguir o lock

// Zona crítica: leitura + incremento + escrita atômica
counter.Value++;
await _counters.ReplaceOneAsync(...);

```

O `MongoDistributedLock` implementa o lock via documentos na coleção `distributed_locks`. O primeiro a inserir/atualizar o documento "vence" o lock; os demais aguardam com polling configurável até o lock ser liberado (ou o timeout expirar).

## Observabilidade

Com o Aspire Dashboard (`http://localhost:15000`) você consegue ver em tempo real:
- Traces distribuídos de cada request
- Métricas de throughput e latência
- Logs estruturados com o ciclo de vida de cada lock
