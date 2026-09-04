# Nginx reverse proxy examples

Ready-to-copy Nginx configuration files for running nzbdav behind a reverse
proxy. Pick whichever layout matches your setup.

## Files

- [`subfolder.conf`](./subfolder.conf) — run nzbdav at a subfolder
  (e.g. `https://example.com/nzbdav/`). Requires the `URL_BASE` setting (see
  below).
- [`subdomain.conf`](./subdomain.conf) — run nzbdav on its own subdomain
  (e.g. `https://nzbdav.example.com/`). No special build args required.

## Quick start

1. Choose the config file that matches your layout.
2. Copy it to `/etc/nginx/sites-available/nzbdav.conf`.
3. Update `server_name`, the SSL certificate paths, and the upstream address
   to match your deployment.
4. Enable it:
   ```sh
   sudo ln -s /etc/nginx/sites-available/nzbdav.conf /etc/nginx/sites-enabled/
   sudo nginx -t
   sudo systemctl reload nginx
   ```

## nzbdav configuration

### Subfolder setup

Both halves of `URL_BASE` must be set — the Docker **build arg** (so React
Router, Vite, and the client bundle know the prefix) and the **runtime env
var** (so the Express server mounts middleware at the right place). See
[`docs/url-base.md`](../../docs/url-base.md) for the full explanation.

```yaml
# docker-compose.yml
services:
  nzbdav:
    build:
      context: .
      args:
        URL_BASE: /nzbdav
    environment:
      URL_BASE: /nzbdav
    ports:
      - "3000:3000"
```

### Subdomain setup

No build arg, no env var — that's the default. Just point nginx at port
`3000`.

## Sonarr / Radarr settings

In Sonarr/Radarr **Settings → Download Clients → SABnzbd**:

| Field        | Subfolder                          | Subdomain                       |
| ------------ | ---------------------------------- | ------------------------------- |
| Host         | `example.com`                      | `nzbdav.example.com`            |
| Port         | `443`                              | `443`                           |
| URL Base     | `/nzbdav`                          | *(leave blank)*                 |
| Use SSL      | ✓                                  | ✓                               |
| API Key      | from nzbdav Settings → SABnzbd     | from nzbdav Settings → SABnzbd  |

## rclone (WebDAV) settings

In your `rclone.conf` remote:

| Field | Subfolder                                | Subdomain                              |
| ----- | ---------------------------------------- | -------------------------------------- |
| url   | `https://example.com/nzbdav/content`     | `https://nzbdav.example.com/content`   |
| user  | from nzbdav Settings → WebDAV            | same                                   |
| pass  | from nzbdav Settings → WebDAV (obscured) | same                                   |

## Why these configs look the way they do

A few decisions worth calling out:

- **`^~` prefix match on the API and WebDAV blocks** outranks the web-UI
  block, so longer regex locations elsewhere in your config can't capture
  the API traffic by accident.
- **`auth_basic off` + `auth_request off`** on the API and WebDAV blocks
  lets Sonarr/Radarr/rclone bypass your proxy-level auth — they authenticate
  to nzbdav directly with the API key or WebDAV credentials. Disable
  whichever of the two applies to your setup; having both there is harmless.
- **`proxy_buffering off`** plus the longer `proxy_read_timeout` /
  `proxy_send_timeout` on the WebDAV block keep range requests (`Range:
  bytes=…`) flowing intact, which is what makes seek work on a streamed
  RAR-archived video.
- **No `sub_filter`, no `proxy_redirect`, no `njs` manifest rewriting** —
  the historical workarounds for nzbdav's lack of native sub-path support
  are gone. nzbdav now emits correctly-prefixed URLs at the source.

## Troubleshooting

See the **Troubleshooting** section of
[`docs/url-base.md`](../../docs/url-base.md#troubleshooting).
