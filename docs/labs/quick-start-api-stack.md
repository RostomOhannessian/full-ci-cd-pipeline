---
title: "Lab 1: Quick start: the API-only stack"
description: "Start SQL Server, Valkey, Keycloak, and the telemetry stack with one command, prove that each one answers as the plan describes, read a real token, and break the stack on purpose."
audience: [learners]
tools: [dev-command, powershell, docker-compose, sql-server, valkey, keycloak, otel-lgtm, dev-containers]
introduced: "phase-1"
prerequisites: ["A Dev Container, or Git, Docker, and the .NET SDK 10.0.401 (see the native setup guide)", "About 4 GB of memory and 2 CPUs for Docker", "About 3 GB of disk space for images"]
estimated-time: "30 minutes, plus the first image pull"
last-verified: 2026-10-10
owner: "@RostomOhannessian"
---

# Lab 1: Quick start: the API-only stack

This lab starts the services that the product catalog API depends on, and proves that each one behaves as [plan section 8](../plans/implementation-plan.md) says it does. The API itself arrives with WP1.10, so the
calls that read and write products are added to this lab then. What you see here is the stable base that the API stands on.

The commands below start with `./dev`. On Windows, `./dev` in PowerShell and `.\dev` in cmd.exe both run `dev.cmd`.

## Objectives

By the end you can:

- Start and stop the API-only stack with the `dev` command, and say which ports it uses and why they are safe.
- Read the claims of an access token that Keycloak issued, and explain each one.
- Explain why the API must check a user's role as well as the scopes in a token.
- Diagnose a stack that is not healthy, and recover it.

## Prerequisites and setup

- A Dev Container (open the repository in VS Code or Codespaces), or a native setup with Git, Docker, and the .NET SDK, as [the native setup guide](../tutorials/native-setup.md) describes.
- Docker running with at least 4 GB of memory. `dev doctor` checks it for you.
- Start from the repository root. The first run downloads about 3 GB of images, so allow a few minutes.

Check the machine first. Doctor changes nothing, and it prints a fix beside every check that does not pass.

```bash illustrative
./dev doctor
```

**You should see:** a line for each check that starts with `[ok]`, `[warn]`, or `[fail]`, and a last line such as `doctor: 12 ok, 0 warning(s), 0 failure(s)`. Fix every `[fail]` before you go on. A `[warn]` for the Git identity is safe to ignore in this lab.

## Steps

### 1. Start the stack

One command creates `.env` with random local secrets, and starts the four services. It waits until every service reports healthy. This is the script that the command runs:

[!code-powershell[Set up the stack](scripts/lab-01.ps1#setup)]

```bash illustrative
./dev lab 1 setup
```

**You should see:** `The stack is healthy. Everything listens on 127.0.0.1 only`, followed by the five addresses. Nothing is reachable from another machine, and no port is below 1024, so no administrator rights were needed.

### 2. Prove that each service works

The check is the same one that the Dev Container workflow runs in CI. It does not only ask whether a container is up. It logs in to SQL Server, tries the Valkey ACL both ways, asks Keycloak for tokens, and exports an empty trace to the telemetry stack.

[!code-powershell[Check the stack](scripts/lab-01.ps1#check)]

```bash illustrative
./dev lab 1 run
```

**You should see:** five `[ok]` lines (Containers, SQL Server, Valkey ACL, Keycloak realm, and Telemetry), and `quick start: 5 ok, 0 warning(s), 0 failure(s)`. The Valkey line means that the default user is switched off, that the application user can use only its own keys and channel, and that it cannot flush the database. The command prints steps 3 and 4 below after the check.

### 3. Read a token

Keycloak issues a token to the `smoke-tests` client, which the realm file defines as a confidential client with every catalog scope. The script decodes the token without checking its signature. That is fine for reading it, and an API must never do it.

[!code-powershell[Read a token](scripts/lab-01.ps1#token)]

**You should see:** `iss = http://localhost:8180/realms/catalog`, `aud = catalog-api`, and a `scope` of `catalog.read`, `catalog.write`, and `catalog.admin` in some order. Predict before you run it: which claim would an API compare with its own address, and which one would it compare with its own name? The issuer is the first, and the audience is the second.

### 4. See which roles each person gets

The realm has three demo users. This step asks Keycloak for the token it would issue each of them when they ask for every scope, and prints the roles in it.

[!code-powershell[See the roles of each user](scripts/lab-01.ps1#roles)]

**You should see:** `reader` gets `catalog-reader`, `editor` gets `catalog-editor` and `catalog-reader`, and `admin` gets all three roles. Each higher role includes the one below it.

### 5. Look at the telemetry stack

Open `http://localhost:3000` in a browser. Sign in as `admin`, with the value of `GRAFANA_ADMIN_PASSWORD` from the `.env` file in the repository root. The password is random and exists only on this machine.

**You should see:** Grafana's home page. The stack holds no data yet, because the API arrives in WP1.10, and WP1.11 adds the dashboards.

## Break it

Predict first. You will change the SQL Server password in `.env` after the stack has started, and then start the stack again.

1. Open `.env` and change the value of `MSSQL_SA_PASSWORD` to any other value that has at least eight characters, with upper case and lower case letters and a digit.
2. Run the start command again.

```bash illustrative
./dev compose up
```

What do you expect? Write your guess down, then look at the result.

**Diagnosis:** the start command waits for about three minutes and then says that `catalog-sqlserver-1` is unhealthy. Run `./dev compose check`. It reports `sqlserver is unhealthy` and a fix. SQL Server reads `MSSQL_SA_PASSWORD` only when it creates its system databases for the first time. The data volume still holds the first password, so the health check, which logs in with the new one, fails. The other three services stay healthy, so the problem is in one container and not in the stack. Undo the change in `.env` to see the stack recover, or run `./dev compose reset`, which deletes the volume and starts clean with the new password.

## Cleanup

This stops the containers and deletes the data volume. The data is disposable by design (plan section 6).

[!code-powershell[Clean up](scripts/lab-01.ps1#cleanup)]

```bash illustrative
./dev lab 1 cleanup
```

**You should see:** the four containers, the network, and the volume removed. `.env` stays, because it holds only this machine's secrets, and the next start reuses it.

## Check your understanding

1. Why does the stack publish its ports on `127.0.0.1` and not on every interface?
2. Which two claims in a token must an API check before it trusts the token for its own use?
3. The `reader` user asks Keycloak for `catalog.admin`. What does the `scope` claim contain, and what does `realm_access.roles` contain? What should the API do with a token like this?
4. Why does `dev compose check` try to write to a Valkey key outside the catalog prefix, and expect a refusal?

<details>
<summary>Answers</summary>

1. A port on `127.0.0.1` is reachable only from this machine. The services use development credentials and an in-memory identity database, so exposing them to a network would be a hole. High ports also need no administrator rights.
2. The issuer, which must equal the one the API trusts exactly, and the audience, which must contain the API's own name (`catalog-api`). The API also checks the signature, the algorithm, and the lifetime.
3. Keycloak 26.8.0 grants a requested scope even to a user without the role that the scope maps to, so the `scope` claim lists `catalog.admin`. The `realm_access.roles` claim holds only `catalog-reader`, because roles come from what the user has. The API must require the role as well as the scope, and so refuse an admin action. [ADR-0026](../adr/0026-api-only-compose-stack-and-catalog-realm.md) records the finding.
4. A permission is proved by showing that the wrong action fails. A test that only checks that the right action works would pass on a Valkey that gives every user full access.

</details>

## Further reading

- [The dev command](../reference/tools/dev-command.md) for every command and exit code (checked 2026-10-10)
- [ADR-0026: The API-only Compose stack and the catalog realm](../adr/0026-api-only-compose-stack-and-catalog-realm.md)
- [Keycloak](../reference/tools/keycloak.md), [SQL Server](../reference/tools/sql-server.md), and [Valkey](../reference/tools/valkey.md) for each service
- [Dev Containers](../reference/tools/dev-containers.md) for the environment that CI uses to run this lab
