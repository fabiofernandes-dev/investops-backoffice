FROM node:22-alpine AS build
WORKDIR /app
# O lockfile v3 (gerado com npm 11) aninha versões distintas de deps dev-only
# (pkijs quer @noble/hashes 1.4.0; jsdom/@exodus/bytes quer >=1.8). O npm 10.9.8
# do base rejeita esse aninhamento no `npm ci` — alinhamos o npm ao lockfile.
RUN npm install -g npm@11
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/investops-web/browser /usr/share/nginx/html
