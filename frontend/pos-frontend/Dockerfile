FROM node:24-bookworm-slim@sha256:0e0ff40c39bc087845bfb27465a0df4ea419520094bc35842ff83dd8cbe6f9b6 AS build
WORKDIR /src
COPY frontend/pos-frontend/package*.json ./
RUN npm ci
COPY frontend/pos-frontend/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:stable-alpine@sha256:ed04ec1ff34502c339ee5c3ae3f855442398edc1d05591e2b98981dcbbd20b1e
COPY --from=build /src/dist/pos-frontend/browser/ /usr/share/nginx/html/
COPY deploy/nginx/nginx.conf /etc/nginx/nginx.conf
COPY deploy/nginx/default.conf.template /etc/nginx/default.conf.template
COPY scripts/ops/web-entrypoint.sh /web-entrypoint.sh
ENV HFPOS_HTTPS_PORT=443
USER 101:101
EXPOSE 8080 8443
ENTRYPOINT ["sh", "/web-entrypoint.sh"]
