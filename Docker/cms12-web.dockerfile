# Web host for the CMS 12 stack (docker-compose.cms12.yml). Mirrors Docker/web.dockerfile, for the
# self-contained CMS 12 dev host instead of the CMS 13 one and its Alloy submodule.
FROM mcr.microsoft.com/dotnet/sdk:10.0

# Create the runtime user *before* restoring. Restoring as root would leave the
# NuGet cache in /root/.nuget, which appuser cannot read, so every container
# start would restore again from scratch.
RUN useradd --create-home appuser
USER appuser

# Created here, owned by appuser, so that the named volume docker-compose.cms12.yml mounts over it is
# initialised with appuser's ownership. A volume mounted onto a path that does not exist in the image
# is created owned by root, and the app - which runs as appuser - then cannot write its DataProtection
# keys at all: every request needing one fails with UnauthorizedAccessException.
RUN mkdir -p /home/appuser/.aspnet/DataProtection-Keys

WORKDIR /src

# Copy only the manifests, so the restore layer is cached independently of the
# source. The source itself arrives at runtime via the bind mount in
# docker-compose.cms12.yml, which is why nothing is built into the image here.
COPY --chown=appuser:appuser ./NuGet.config ./Directory.Build.props ./
COPY --chown=appuser:appuser ./src/Package.props ./src/
COPY --chown=appuser:appuser ./src/OptiPowerTools.ScheduledJobsInsights.Cms12/OptiPowerTools.ScheduledJobsInsights.Cms12.csproj ./src/OptiPowerTools.ScheduledJobsInsights.Cms12/
COPY --chown=appuser:appuser ./src/OptiPowerTools.ScheduledJobsInsights.Cms12.Web/OptiPowerTools.ScheduledJobsInsights.Cms12.Web.csproj ./src/OptiPowerTools.ScheduledJobsInsights.Cms12.Web/

RUN dotnet restore src/OptiPowerTools.ScheduledJobsInsights.Cms12.Web/OptiPowerTools.ScheduledJobsInsights.Cms12.Web.csproj

# The content root is the working directory, so App_Data (the Alloy content) resolves under the
# web project.
WORKDIR /src/src/OptiPowerTools.ScheduledJobsInsights.Cms12.Web

EXPOSE 80

ENTRYPOINT ["dotnet", "run", "--no-launch-profile"]
