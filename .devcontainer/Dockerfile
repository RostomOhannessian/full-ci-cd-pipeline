# The base of the Dev Container (plan section 12, ADR-0025). It is the Microsoft .NET Dev Container image, which carries the .NET SDK that global.json
# pins (10.0.401), Git, and a non-root user, pinned by tag and digest so a rebuild gives the same tools. Dependabot (docker ecosystem, /.devcontainer)
# proposes updates to both. Everything else comes from the features in devcontainer.json, whose versions devcontainer-lock.json pins, and from
# the tools that "dev bootstrap" installs from tools/versions.yaml.
FROM mcr.microsoft.com/devcontainers/dotnet:10.0@sha256:694076b55f447d4b9f5b49bc6616c2e904d63e1a61e43680b2bbea46011b6783

ENV DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    POWERSHELL_TELEMETRY_OPTOUT=1
