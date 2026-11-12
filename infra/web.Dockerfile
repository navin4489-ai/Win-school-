FROM node:20-alpine AS build
WORKDIR /app
COPY frontend/admin-portal/package*.json ./
RUN npm install
COPY frontend/admin-portal/ ./
RUN npm run build

FROM nginx:1.27-alpine
COPY infra/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/admin-portal/browser /usr/share/nginx/html
EXPOSE 80
