# Sylin.Agyo.Web.GraphQl

GraphQL-over-entities for Koan-built applications: a controller-hosted GraphQL endpoint and
auto-generated schema over `IEntity<>` types. Reuses the public Koan.Web read-path hook pipeline
(WEB-0068) so authorization and visibility predicates apply on the GraphQL surface exactly as on REST.

Opt-in helper kept off the Koan release train (AGYO-0001) so its HotChocolate CVE cadence is owned
here, independently of Koan's cadence.

- Target framework: net10.0
- License: Apache-2.0

## Install

```powershell
dotnet add package Sylin.Agyo.Web.GraphQl
```

## Configuration

Bind under the `Agyo:Web:GraphQl` configuration section:

| Key       | Default     | Description                               |
|-----------|-------------|-------------------------------------------|
| `Enabled` | `true`      | Enable/disable the GraphQL endpoints.     |
| `Path`    | `/graphql`  | Request path for the GraphQL endpoint.    |
| `Debug`   | `false`     | Emit debug diagnostics in the response.   |

Debug diagnostics can also be toggled per-request with the `X-Agyo-Debug` header.

## Known rough spots

Filed against this package for a later pass (do not block on them):

- All entity fields are mapped as GraphQL `StringType`.
- Upsert input binding is string-only.

## Links

- Agyo Tools: https://github.com/sylin-org/agyo-tools
