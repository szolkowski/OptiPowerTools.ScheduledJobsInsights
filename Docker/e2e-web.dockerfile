# Web host for the end-to-end stacks (docker-compose.e2e.yml), for either CMS version.
#
# Unlike the dev images, which bind-mount the repository and build at startup, this one bakes the
# current working tree in and builds it here. An e2e run therefore tests one fixed snapshot of the
# code, and shares no bin/obj with the dev containers - both bind-mount the repository, and the Alloy
# submodule's own Directory.Build.props gives every container the same obj/container path, so two
# containers building it at once would overwrite each other.
FROM mcr.microsoft.com/dotnet/sdk:10.0

# Not root, like the dev images: created before anything is restored, so the NuGet cache lands in its
# home. Port 80 still binds - Docker lets unprivileged processes in a container use low ports.
RUN useradd --create-home appuser
USER appuser

# The dev host's project directory, relative to the repository root.
ARG WEB_PROJECT_DIR

WORKDIR /src

# What the build reads, named rather than `COPY . .`, so nothing else in the working tree - a local
# .env, editor settings, test artifacts - can end up in the image whatever .dockerignore misses.
# .editorconfig is build input too: it sets analyzer severities, and TreatWarningsAsErrors is on.
COPY --chown=appuser:appuser NuGet.config Directory.Build.props .editorconfig ./
COPY --chown=appuser:appuser src ./src
# The CMS 13 host is the Alloy submodule's site; the CMS 12 host does not need it, and an
# uninitialised submodule is an empty directory, which copies as one.
COPY --chown=appuser:appuser sub ./sub

# SourceLink needs the git history, which .dockerignore leaves out; without these it warns, and
# TreatWarningsAsErrors turns the warning into a failed build.
RUN dotnet build "${WEB_PROJECT_DIR}" \
        -p:EnableSourceLink=false \
        -p:EnableSourceControlManagerQueries=false

# The content root is the working directory: the CMS 13 host resolves the Alloy submodule relative to
# it, and both hosts find App_Data from it.
WORKDIR /src/${WEB_PROJECT_DIR}

EXPOSE 80

ENTRYPOINT ["dotnet", "run", "--no-build", "--no-launch-profile"]
