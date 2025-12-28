# Comprehensive NzbDav Setup Guide

This guide is an opinionated, step-by-step walkthrough to setting up NzbDav for maximum performance ("infinite library" style) with Radarr, Sonarr, Plex/Jellyfin and Stremio.

## How the "Infinite Library" Works
Before configuring, it helps to understand the flow:

### Path A: The Automation Flow (Radarr/Sonarr + Plex/Jellyfin)
1. **Radarr** sends an `.nzb` file to NzbDav (acting as a download client) to "download".
2. **NzbDav** mounts the nzb onto the webdav without actually downloading it.
3. **NzbDav** tells Radarr the "download" is finished and points to a folder of **Symlinks** at `/mnt/remote/nzbdav/completed-symlinks`.
    * The **Symlinks** always point to the `/mnt/remote/nzbdav/.ids` folder which contains the streamable content.
4. **Radarr** imports these Symlinks into your library. For eg: `/mnt/media/movies`.
5. **Plex** reads the Symlink -> Rclone Mount -> WebDAV Stream -> Usenet Provider.
    * **RClone** will make the nzb contents available to your filesystem by streaming, without using any storage space on your server.

### Path B: The On-Demand Flow (Stremio)
1. **Stremio (via AIOStreams)** searches your indexers using the `Newznab` addon and finds a release.
2. **AIOStreams** sends the `.nzb` to NzbDav's API to mount it.
3. **NzbDav** mounts the file instantly via WebDAV.
4. **AIOStreams** generates a streamable URL.
   * *Note: If using the recommended Proxy setup, this URL points to AIOStreams, which tunnels the traffic from NzbDav.*
5. **Stremio** plays the video from that URL (bypassing Rclone/Symlinks entirely).

## Phase 1: Prerequisites

### 1. Usenet Provider
You need an usenet provider to download content. Consult the [Usenet Providers Wiki](https://www.reddit.com/r/usenet/wiki/providerdeals/) for a full list.

### 2. Indexers
You need usenet indexers to find content. Consult the [Usenet Indexers Wiki](https://www.reddit.com/r/usenet/wiki/indexers/) for a full list.

Add these to Prowlarr and sync them to your Radarr/Sonarr instances.

---

## Phase 2: Initial Deployment

We start with a basic NzbDav container.

### 1. `docker-compose.yml` (Part 1)

Create the file structure like below:
```
your-root-docker-folder/
├── apps
│   ├── nzbdav
│   │   └── docker-compose.yml   👈 Create this file now
│   └── ...
```

Update `PUID`, `PGID`, `TZ`, and volume paths as needed.
You can get your PUID/PGID by running `id` in your terminal.

```yaml
services:
  nzbdav:
    image: nzbdav/nzbdav:latest
    container_name: nzbdav
    restart: unless-stopped
    healthcheck:
      test: curl -f http://localhost:3000/health || exit 1
      # Check every 1 minute
      interval: 1m
      # If it fails 3 times (3 minutes total), restart it
      retries: 3
      # Give it 5 seconds to boot up
      start_period: 5s
      # If it doesn't answer in 5 seconds, assume it's frozen
      timeout: 5s
    ports:
      - "3000:3000"
    environment:
      # Change these IDs to match your Docker user that you got from above
      - PUID=1000
      - PGID=1000
    volumes:
      - ./config:/config
      - /mnt:/mnt
```

Run the container
```bash
docker compose up -d
```

### 2. Core Configuration

Navigate to `http://your-server-ip:3000`.

**A. Create Admin Account**

Set your username and password.

**B. Usenet Settings (`Settings` > `Usenet`)**

* **Host:** `news.newshosting.com` (put your provider here).
* **Port:** `563`
* **Username / Password:** Your Usenet creds.
* **Max Connections:** `100` (Set to your provider's max allowed).
* **Type:** `Pool Connections`.
* **Use SSL:** Checked.

**C. WebDAV Settings (`Settings` > `WebDAV`)**

* **Set WebDAV Password:** Create a password (you will need this for Rclone).
* **Enforce Read-Only:** Uncheck it if you'd like to delete files from terminal. Otherwise, leave it checked.

### 3. Speed Tuning (Optional)

_Note: The default Max Download Connections setting of `15` works perfectly for most users (handling ~1Gbps). You only need to touch this if you are experiencing speed issues._

You can find the optimal **Max Download Connections** for your network (`Settings > WebDAV > Max Download Connections`) using the steps below:

1. **Baseline Test:** Run this on your server to check raw bandwidth.
   ```bash
   wget -O /dev/null https://ash-speed.hetzner.com/10GB.bin --report-speed=bits
   ```
2. **NzbDav Internal Test:**
   * In one Terminal window, run below command to monitor CPU usage:
     ```bash
     docker stats nzbdav
     ```
   * Download a movie `.nzb` via your indexer website and upload it to NzbDav.
   * In NzbDav UI: Go to `Dav Explore` > `Content` > `Movies` > Pick the movie you just downloaded > Right click the **video file** and click `Copy Link Address`. Now paste it in a text editor where you can see the whole thing.
   * Now construct test command like below and run it in another terminal window:
     ```bash
     docker exec nzbdav sh -c "apk add --no-cache wget > /dev/null 2>&1 && timeout 20s wget -O /dev/null --report-speed=bits --progress=bar:force:noscroll 'http://localhost:8080/view/content/Movies/<Movie Folder>/<Movie Name>.mkv?downloadKey=<download-key>'"
     ```
     Take a look at the speed it reports and also notice the CPU usage of the container.
3. **Adjust & Repeat:**
   * Set `Max Download Connections` to `10`. Test speed. (e.g., 500Mbps @ 70% CPU)
   * Set `Max Download Connections` to `15`. Test speed. (e.g., 1Gbps @ 85% CPU)
   * *Sweet Spot:* Stop when speed plateaus. For me, **15** (the default value) was the magic number.

---

## Phase 3: The Full Stack (Rclone Sidecar)

Now we mount the NzbDav web dav to the host file system using a sidecar container.

### 1. Prepare Host Directory

```bash
sudo mkdir -p /mnt/remote/nzbdav # Create mount folder
sudo chown -R $(id -u):$(id -g) -R /mnt/remote/nzbdav # Give ownership of the folder to your user
```

### 2. Generate Rclone Config

```
your-root-docker-folder/
├── apps
│   ├── nzbdav
│   │   ├── docker-compose.yml
│   │   └── rclone.conf          👈 Create this empty file now
│   └── ...
```

*Generate obscured password:* `docker run --rm -it rclone/rclone obscure "<the-webdav-password-you-set-in-nzbdav-earlier>"`

Now populate `rclone.conf` with:
```ini
[nzbdav]
type = webdav
url = http://nzbdav:3000/
vendor = other
user = admin
pass = <PASTE_OBSCURED_PASSWORD_HERE_WITHOUT_ANGLE_BRACKETS>
```

### 3. Update `docker-compose.yml`

Add the Rclone sidecar service to your existing `apps/nzbdav/docker-compose.yml`.

Update `PUID`, `PGID`, `TZ`, and volume paths as needed.
You can get your PUID/PGID by running `id` in your terminal.

```yaml
nzbdav_rclone:
  image: rclone/rclone:latest
  container_name: nzbdav_rclone
  restart: unless-stopped
  environment:
    # Change these IDs to match your Docker user that you got from above
    - PUID=1000
    - PGID=1000
    # Set the time zone to match your location
    - TZ=America/New_York
  volumes:
    # Host Path : Container Path : Propagation
    - /mnt:/mnt:rshared
    - ./rclone.conf:/config/rclone/rclone.conf
  cap_add:
    - SYS_ADMIN
  security_opt:
    - apparmor:unconfined
  devices:
    - /dev/fuse:/dev/fuse:rwm
  depends_on:
    nzbdav:
      condition: service_healthy
      restart: true
  # Optimized mounting flags for streaming
  # 0M buffer size prevents double-caching (Kernel + RClone)
  # 512M read-ahead ensures smooth playback
  command: >
    mount nzbdav: /mnt/remote/nzbdav
      --uid=1000
      --gid=1000
      --allow-other
      --links
      --use-cookies
      --vfs-cache-mode=full
      --vfs-cache-max-size=20G
      --vfs-cache-max-age=24h
      --buffer-size=0M
      --vfs-read-ahead=512M
      --dir-cache-time=20s
```

Start `nzbdav_rclone`
```bash
$ docker compose up -d nzbdav_rclone
```

If you make some rclone config changes or other changes in the compose file, apply the changes like this
```bash
$ docker compose up -d --force-recreate nzbdav_rclone
```

Check out the mount is working
```bash
ls -la /mnt/remote/nzbdav
# Should show: .ids, completed-symlinks, content, nzbs
```

#### Understanding the Flags
* **`--links`**: **Crucial**. This allows `*.rclonelink` files within the webdav to be translated to symlinks when mounted onto your filesystem.
  > *Note: Requires Rclone v1.70.3+.*
* **`--use-cookies`**: **Performance**. Without this, Rclone re-authenticates on every single request, causing massive slowdowns.
* **`--allow-other`**: **Permissions**. Ensures other containers (like Radarr/Plex) can see the mounted files.
* **`--vfs-cache-mode=full`**: **Performance**. Enables the full VFS cache, which is required for seeking and proper file handling.
* **`--buffer-size 0M`**: **Stability**. Prevents double-caching (RAM + Disk). 
* **`--vfs-read-ahead=512M`**: **Smooth Playback**. Buffers 512MB into VFS disk cache ahead of the current position to handle high-bitrate spikes without stuttering.
* **`--vfs-cache-max-size=20G`**: **Disk Management**. Limits the local disk space used by the cache. Adjust based on your available storage.
* **`--dir-cache-time=20s`**: **Responsiveness**. Keeps the directory cache short so new downloads/links appear quickly in the mount.

These flags are optimized for streaming. 

Remember: `unnecessary flags = potential pitfalls`.

#### Rclone flags reference
* [Rclone Forum Discussion on Buffer Size](https://forum.rclone.org/t/whats-the-suitable-value-to-set-for-buffer-size-with-vfs-read-ahead/39971/4)

---

## Phase 4: Integrations

### 1. Add NzbDav Download Client to Radarr/Sonarr

Go to Radarr/Sonarr > `Settings` > `Download Clients` > `Add Download Client`

* Client: **SABnzbd**
* Name: `NzbDav`
* Host: `nzbdav` 
* Port: `3000`
* API Key: Found in NzbDav `Settings` > `SABnzbd`.

### 2. Configure NzbDav for Radarr/Sonarr

Go to NzbDav `Settings` > `Radarr/Sonarr`.

1. **Radarr Instances > Add**
   * **Host:** `http://radarr:7878`
   * **API Key:** (Radarr > Settings > General > Security > API Key)
2. **Sonarr Instances > Add**
   * **Host:** `http://sonarr:8989`
   * **API Key:** (Sonarr > Settings > General > Security > API Key)
3. **Automatic Queue Management:**

   Configure these rules to handle failed or bad releases, keeping your queue clean with as little manual intervention as possible. 
   Feel free to experiment and adjust these rules to your liking.

   * **Do Nothing:**
       * Found matching series via grab history, but release was matched to series by ID. Automatic import is not possible.
       * Found matching movie via grab history, but release was matched to movie by ID. Manual Import required.
       * Episode was not found in the grabbed release.
       * Episode was unexpected considering the folder name.
       * Invalid season or episode.
       * Unable to determine if file is a sample.
   * **Remove, Blocklist, and Search:**
       * No files found are eligible for import.
       * No audio tracks detected.
       * Sample.
   * **Remove and Blocklist:**
       * Not an upgrade for existing episode file(s).
       * Not an upgrade for existing movie file.
       * Not a Custom Format upgrade.
   * **Remove:**
       * Episode file already imported.

### 3. Configure Mount & Repairs

1. **Mount Directory (`Settings` > `SABnzbd`):**
   * **Rclone Mount Directory:** `/mnt/remote/nzbdav`
   * *Note: This tells NzbDav where the files physically exist on your host system so it can pass the correct path to Radarr/Sonarr.*
2. **Repairs (`Settings` > `Repairs`):**
   * **Library Directory:** `/mnt/media`
     *(Point this to the root folder where your actual Movie/TV libraries live on the host)*.
   * **Enable Background Repairs:** Checked.
     *(This allows NzbDav to monitor for dead links in your library and trigger redownloads automatically).*

---

## Phase 5: Usenet Streaming in Stremio (via AIOStreams)

You can stream your Usenet content directly in Stremio using [AIOStreams](https://github.com/Viren070/AIOStreams).

For more info, check out their [Usenet Wiki](https://github.com/Viren070/AIOStreams/wiki/Usenet).

### 1. Configure NzbDav Service

In the AIOStreams UI:

1. Go to the **Services** menu and select **NzbDav**.
2. Enter the details:
   * **NzbDAV URL:** `http://nzbdav:3000` (Use your public URL if accessing remotely).
   * **NzbDAV API Key:** (From NzbDav `Settings` > `SABnzbd`).
   * **NzbDAV WebDAV Username:** (From NzbDav `Settings` > `WebDAV`).
   * **NzbDAV WebDAV Password:** (From NzbDav `Settings` > `WebDAV`).
   * **AIOStreams Auth Token (Recommended):** Get it from your self-hosted AIOStreams' `.env` file's `AIOSTREAMS_AUTH` environment variable. (e.g., `user:pass`).

### 2. Configure Newznab Addon

In the AIOStreams UI:

1. Go to **Addons** > **Marketplace** > From the Types dropdown, select **Usenet**.
2. Find the **Newznab** addon and click **Configure**.
3. Add your indexers (repeat for each one):
   * **Name:** `NZBGeek` (or similar).
   * **Newznab URL:** Select `NZBgeek` from dropdown.
   * **API Key:** Your indexer's API key.
   * **AIOStreams Proxy Auth (Recommended):** Get it from your self-hosted AIOStreams' `.env` file's `AIOSTREAMS_AUTH` environment variable. (e.g., `user:pass`).
   * **Search Mode:** **Forced Query** (was `Auto` by default)
   * **Timeout:** `5000` ms (was `7000` by default)
4. Leave everything else as default and click **Install**

### 3. Install to Stremio

Go to the **Save & Install** tab, click **Save**, and then install the addon to Stremio.


