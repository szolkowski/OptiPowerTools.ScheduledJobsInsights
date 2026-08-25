FROM mcr.microsoft.com/dotnet/sdk:10.0

# Create the runtime user *before* restoring. Restoring as root would leave the
# NuGet cache in /root/.nuget, which appuser cannot read, so every container
# start would restore again from scratch.
RUN useradd --create-home appuser
USER appuser

# Created here, owned by appuser, so that the named volume docker-compose.yml mounts over it is
# initialised with appuser's ownership. A volume mounted onto a path that does not exist in the image
# is created owned by root, and the app - which runs as appuser - then cannot write its DataProtection
# keys at all: every request needing one fails with UnauthorizedAccessException.
RUN mkdir -p /home/appuser/.aspnet/DataProtection-Keys

WORKDIR /src

# Copy only the manifests, so the restore layer is cached independently of the
# source. The source itself arrives at runtime via the bind mount in
# docker-compose.yml, which is why nothing is built into the image here.
COPY --chown=appuser:appuser ./NuGet.config ./Directory.Build.props ./
COPY --chown=appuser:appuser ./sub/MyOptiAlloySite/MyOptiAlloySite/Directory.Build.props ./sub/MyOptiAlloySite/MyOptiAlloySite/
COPY --chown=appuser:appuser ./sub/MyOptiAlloySite/MyOptiAlloySite/nuget.config ./sub/MyOptiAlloySite/MyOptiAlloySite/
COPY --chown=appuser:appuser ./sub/MyOptiAlloySite/MyOptiAlloySite/MyOptiAlloySite.csproj ./sub/MyOptiAlloySite/MyOptiAlloySite/
COPY --chown=appuser:appuser ./src/OptiPowerTools.ScheduledJobsInsights/OptiPowerTools.ScheduledJobsInsights.csproj ./src/OptiPowerTools.ScheduledJobsInsights/
COPY --chown=appuser:appuser ./src/OptiPowerTools.ScheduledJobsInsights.Web/OptiPowerTools.ScheduledJobsInsights.Web.csproj ./src/OptiPowerTools.ScheduledJobsInsights.Web/

RUN dotnet restore src/OptiPowerTools.ScheduledJobsInsights.Web/OptiPowerTools.ScheduledJobsInsights.Web.csproj

# Program.cs resolves the Alloy content root as ../../sub/MyOptiAlloySite/MyOptiAlloySite
# relative to the working directory, so this must be the web project directory.
WORKDIR /src/src/OptiPowerTools.ScheduledJobsInsights.Web

EXPOSE 80

ENTRYPOINT ["dotnet", "run", "--no-launch-profile"]
