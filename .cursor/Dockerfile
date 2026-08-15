# Cloud Agent base image for ACAD KROVY portable development.
#
# ACAD KROVY is a Windows-only AutoCAD plug-in, but its CAD-neutral projects
# (AcKrovy.Core, AcKrovy.Cad.Abstractions, AcKrovy.Localization,
# AcKrovy.Infrastructure) and the portable AcKrovy.Core.Tests suite build and
# run on Linux. This image provides the .NET 10 SDK and PowerShell so agents
# can restore/build those projects and run scripts/compatibility-gate.ps1
# -Portable, matching the Windows CI "Portable Compatibility Gate".
FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    POWERSHELL_TELEMETRY_OPTOUT=1

# Stable system toolchain:
#  - git/curl/ca-certificates for source and downloads
#  - dotnet-sdk-10.0 from the Ubuntu 24.04 (noble) repositories
#  - powershell from the Microsoft package feed (needed by the gate script)
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        git \
        sudo \
    && curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb \
        -o /tmp/packages-microsoft-prod.deb \
    && dpkg -i /tmp/packages-microsoft-prod.deb \
    && rm -f /tmp/packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        dotnet-sdk-10.0 \
        powershell \
    && rm -rf /var/lib/apt/lists/*

# Ensure a non-root 'ubuntu' user (uid 1000) with passwordless sudo exists.
RUN if ! id ubuntu >/dev/null 2>&1; then useradd -m -s /bin/bash -u 1000 ubuntu; fi \
    && install -d -o ubuntu -g ubuntu /home/ubuntu \
    && echo 'ubuntu ALL=(ALL) NOPASSWD:ALL' > /etc/sudoers.d/ubuntu \
    && chmod 0440 /etc/sudoers.d/ubuntu

USER ubuntu
WORKDIR /home/ubuntu
