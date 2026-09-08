FROM node:24.20.0-bookworm-slim AS build
WORKDIR /app
RUN npm install --global npm@11.17.0
COPY package.json package-lock.json ./
RUN npm ci
COPY angular.json tsconfig.json tsconfig.app.json ./
COPY src/ src/
COPY public/ public/
RUN npm run build -- --configuration production

FROM nginxinc/nginx-unprivileged:1.30.4-alpine3.24-slim AS final
COPY nginx.conf /etc/nginx/nginx.conf
COPY --from=build /app/dist/aigenterra-web/browser/ /usr/share/nginx/html/
COPY --from=build /app/dist/aigenterra-web/3rdpartylicenses.txt /usr/share/nginx/html/3rdpartylicenses.txt
EXPOSE 8080
