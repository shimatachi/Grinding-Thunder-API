# Production deployment

Grinding Thunder runs on one Linux host. Native Nginx terminates TLS and proxies to three loopback-only container ports. PostgreSQL and service-to-service traffic stay on the Docker Compose network; PostgreSQL has no host port.

## Prerequisites

- A Linux VPS with DNS A/AAAA records for `grindthunder.mydomain.com`, `api.grindthunder.mydomain.com`, and `admin.grindthunder.mydomain.com`.
- Docker Engine with the Compose plugin.
- Native Nginx and Certbot with its Nginx plugin.
- A non-root deployment account allowed to run Docker, plus root or sudo access for Nginx and Certbot.
- An off-host location for encrypted database backups.

Keep the OS, Docker, Nginx, and Certbot patched. Restrict inbound traffic to SSH, HTTP, and HTTPS. Compose publishes Web, API, and Admin only on `127.0.0.1`; never add a `ports` entry to `db`.

## Initial setup

Clone the repository on the host, then create the production environment file:

```bash
cp .env.example .env
chmod 600 .env
```

Replace every `change-me` value. Generate independent random database, JWT, and Admin passwords; the JWT signing key must contain at least 32 random characters. Set `NEXT_PUBLIC_API_URL` to the public HTTPS API origin and set `CORS_ALLOWED_ORIGINS` to the comma-separated Web and Admin HTTPS origins. Origins must not contain paths or trailing slashes.

`NEXT_PUBLIC_API_URL` is compiled into the Next.js browser bundle. Rebuild the `web` image whenever it changes.

Validate and build the deployment:

```bash
docker compose -f docker-compose.prod.yml config --quiet
docker compose -f docker-compose.prod.yml build
```

## Nginx and TLS

Copy the three examples from `deploy/nginx/` into `/etc/nginx/sites-available/`, adjust domain names or ports if `.env` differs, and enable them:

```bash
sudo ln -s /etc/nginx/sites-available/grindthunder.mydomain.com.conf /etc/nginx/sites-enabled/
sudo ln -s /etc/nginx/sites-available/api.grindthunder.mydomain.com.conf /etc/nginx/sites-enabled/
sudo ln -s /etc/nginx/sites-available/admin.grindthunder.mydomain.com.conf /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

After DNS resolves and the HTTP sites are reachable, request certificates and let Certbot add the TLS listeners and redirects:

```bash
sudo certbot --nginx \
  -d grindthunder.mydomain.com \
  -d api.grindthunder.mydomain.com \
  -d admin.grindthunder.mydomain.com
sudo certbot renew --dry-run
```

The sample proxy configuration forwards the original host, scheme, client chain, and upgrade headers. ASP.NET Core consumes those headers before authentication, HSTS, and routing. This trust model is safe only while the application ports remain bound to localhost and the host is controlled.

## First deployment, migration, and Admin bootstrap

Start PostgreSQL by itself, apply EF Core migrations explicitly, and only then start the applications:

```bash
docker compose -f docker-compose.prod.yml up -d db
docker compose -f docker-compose.prod.yml run --rm api --migrate
docker compose -f docker-compose.prod.yml up -d api admin web
docker compose -f docker-compose.prod.yml ps
```

The normal API process never applies production migrations or development seed data. `--migrate` applies pending migrations and exits; it does not start the HTTP server or bootstrap an Admin.

On the first normal API start, `ADMIN_BOOTSTRAP_USERNAME` and `ADMIN_BOOTSTRAP_PASSWORD` create one Admin only when no Admin-role account exists. The operation is idempotent and never overwrites an account. Sign in at the Admin site, create or verify a durable Admin account, then remove both bootstrap values from `.env` (or leave them empty) and recreate the API container:

```bash
docker compose -f docker-compose.prod.yml up -d --force-recreate api
```

Check the three local health endpoints from the host:

```bash
curl --fail http://127.0.0.1:3000/
curl --fail http://127.0.0.1:5034/health
curl --fail http://127.0.0.1:5035/health
```

## Routine update

Take a backup first. Fetch and inspect the intended release, build its images, stop the request-serving containers, migrate, and recreate them:

```bash
git fetch --all --prune
git checkout <release-tag-or-commit>
docker compose -f docker-compose.prod.yml build
docker compose -f docker-compose.prod.yml stop web admin api
docker compose -f docker-compose.prod.yml up -d db
docker compose -f docker-compose.prod.yml run --rm api --migrate
docker compose -f docker-compose.prod.yml up -d api admin web
docker compose -f docker-compose.prod.yml ps
```

This creates a short maintenance window and avoids running old application code against a newly migrated schema. For an application rollback, check out the previous known-good release, rebuild, and recreate the application services. Database migrations are not automatically reversible: if the release changed the schema incompatibly, restore the pre-deployment backup instead of attempting an improvised downgrade.

## Database backup

Create a compressed custom-format dump. The command reads database identity from the container and writes the dump on the host, outside the database volume:

```bash
mkdir -p backups
docker compose -f docker-compose.prod.yml exec -T db \
  sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' \
  > "backups/grinding-thunder-$(date -u +%Y%m%dT%H%M%SZ).dump"
```

Verify every backup before moving it off-host:

```bash
docker compose -f docker-compose.prod.yml exec -T db pg_restore --list \
  < backups/<backup-file>.dump >/dev/null
```

Automate the dump with cron or a systemd timer, retain multiple generations, encrypt copies containing production data, copy them off the VPS, and regularly test restoration. A named Docker volume is persistence, not a backup.

## Database recovery

Recovery replaces live data. Stop the applications, preserve a final dump when possible, recreate the database, restore, and start the services:

```bash
docker compose -f docker-compose.prod.yml stop web admin api
docker compose -f docker-compose.prod.yml exec -T db \
  sh -c 'dropdb -U "$POSTGRES_USER" --if-exists "$POSTGRES_DB" && createdb -U "$POSTGRES_USER" "$POSTGRES_DB"'
docker compose -f docker-compose.prod.yml exec -T db \
  sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --no-privileges' \
  < backups/<backup-file>.dump
docker compose -f docker-compose.prod.yml up -d api admin web
```

If restoring into a clean installation, build and start `db` first. Do not run `--migrate` before restoring a full database dump; after restoration, run it only when deploying application code newer than the backup schema.

## Operations and troubleshooting

Inspect state and recent logs:

```bash
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs --tail=200 api
docker compose -f docker-compose.prod.yml logs --tail=200 admin
docker compose -f docker-compose.prod.yml logs --tail=200 web
docker compose -f docker-compose.prod.yml logs --tail=200 db
```

Follow one service with `logs -f <service>`. Restart a service with `restart <service>`; recreate it with `up -d --force-recreate <service>` after configuration changes.

Common checks:

- An unhealthy API usually means PostgreSQL is unavailable, migrations are missing, or the JWT key is absent/invalid. Inspect `api` and `db` logs.
- Admin login or antiforgery failures after recreation can indicate that the `admin_keys` data-protection volume was removed. Keep that volume across deployments.
- Browser CORS errors require the exact public Web/Admin origins in `CORS_ALLOWED_ORIGINS`; rebuild/recreate API after changes.
- Browser requests targeting an old API hostname require a Web image rebuild because `NEXT_PUBLIC_API_URL` is a build argument.
- A `502 Bad Gateway` from Nginx means the matching container is unhealthy/not running or the configured host port differs from `.env`.
- Validate Nginx with `sudo nginx -t` and inspect its error log when proxying fails.

Use `docker compose -f docker-compose.prod.yml down` only when intentionally stopping the stack. Never add `--volumes` during routine operations because that deletes PostgreSQL and Admin key persistence.
