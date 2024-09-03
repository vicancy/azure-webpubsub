# Use a base image with Node.js installed
FROM node:20-slim AS node_base

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS dotnet_base

COPY --from=node_base /usr/share/dotnet/dotnet /usr/share/dotnet/dotnet

ENV Endpoint=
ENV Hub=
ENV UpstreamUrl=
ENV TenantId=

COPY home /home

# Install the npm tool globally
RUN npm install -g @azure/web-pubsub-tunnel-tool

# Set the working directory inside the container
WORKDIR /app

ENTRYPOINT ["bash", "-c", "awps-tunnel run --hub $hub --webviewHost 0.0.0.0 -u $UpstreamUrl --endpoint $Endpoint"]
