<p align="center">
  <img width="1101" height="238" alt="image" src="https://github.com/user-attachments/assets/b14165f4-24ff-4abe-8af6-3ca852e781d4" />
</p>

# Nzb Dav

NzbDav is a WebDAV server that allows you to mount and browse NZB documents as a virtual file system without downloading. It's designed to integrate with other media management tools, like Sonarr and Radarr, by providing a SABnzbd-compatible API. With it, you can build an infinite Plex or Jellyfin media library that streams directly from your usenet provider at maxed-out speeds, without using any storage space on your own server.

Check the video below for a demo:

https://github.com/user-attachments/assets/be3e59bc-99df-440d-8144-43b030a4eaa4

> **Attribution**: The video above contains clips of [Sintel (2010)](https://studio.blender.org/projects/sintel/), by Blender Studios, used under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)


# Key Features

* 📁 **WebDAV Server** - *Host your virtual file system over HTTP(S)*
* ☁️ **Mount NZB Documents** - *Mount and browse NZB documents without downloading.*
* 📽️ **Full Streaming and Seeking Abilities** - *Jump ahead to any point in your video streams.*
* 🗃️ **Stream archived contents** - *View, stream, and seek content within RAR and 7z archives.*
* 🔓 **Stream password-protected content** - *View, stream, and seek within password-protected archives.*
* 💙 **Healthchecks & Repairs** - *Automatically replace content that has been removed from your usenet provider*
* 🧩 **SABnzbd-Compatible API** - *Use NzbDav as a drop-in replacement for sabnzbd.*
* 🙌 **Sonarr/Radarr Integration** - *Configure it once, and leave it unattended.*


# Getting Started

The easiest way to get started is by using the official Docker image.

To try it out, run the following command to pull and run the image with port `3000` exposed:

```bash
docker run --rm -it -p 3000:3000 nzbdav/nzbdav:alpha
```

And if you would like to persist saved settings, attach a volume at `/config`

```
mkdir -p $(pwd)/nzbdav && \
docker run --rm -it \
  -v $(pwd)/nzbdav:/config \
  -e PUID=1000 \
  -e PGID=1000 \
  -p 3000:3000 \
  nzbdav/nzbdav:alpha
```
After starting the container, be sure to navigate to the Settings page on the UI to finish setting up your usenet connection settings.

<p align="center">
    <img width="600" alt="settings-page" src="https://github.com/user-attachments/assets/894c9c12-364c-4a58-9b79-719cfa7a1f12" />
</p>

You'll also want to set up a username and password for logging in to the webdav server

<p align="center">
    <img width="600" alt="webdav-settings" src="https://github.com/user-attachments/assets/94dc7313-c766-4db0-b7f7-5cb601d02295" />
</p>

# RClone

In order to integrate with Plex, Radarr, and Sonarr, you'll need to mount the webdav server onto your filesystem. 

```
[nzb-dav]
type = webdav
url = // your endpoint
vendor = other
user = // your webdav user
pass = // your rclone-obscured password https://rclone.org/commands/rclone_obscure
```


Below are the RClone settings I use.  
```
--vfs-cache-mode=full
--buffer-size=1024
--dir-cache-time=1s
--vfs-cache-max-size=5G
--vfs-cache-max-age=180m
--links
--use-cookies
--allow-other
--uid=1000
--gid=1000
```

* The `--links` setting in RClone is important. It allows *.rclonelink files within the webdav to be translated to symlinks when mounted onto your filesystem.

    > NOTE: Be sure to use an updated version of rclone that supports the `--links` argument.
    > * Version `v1.70.3` has been known to support it.
    > * Version `v1.60.1-DEV` has been known _not_ to support it.

* The `--use-cookies` setting in RClone is also important. Without it, RClone is forced to re-authenticate on every single webdav request, slowing it down considerably.
* The `--allow-other` setting is not required, but it should help if you find that your containers are not able to see the mount contents due to permission issues.

**Optional**
* The `--vfs-cache-max-size=5G` Can be added to set the max total size of objects in the cache (default off), thus possibly consuming all free space.
* The `--vfs-cache-max-age=180m` Can be added to set the max time since last access of objects in the cache (default 1h0m0s). 


# Radarr / Sonarr

Once you have the webdav mounted onto your filesystem (e.g. accessible at `/mnt/nzbdav`), you can configure NZB-Dav as your download-client within Radarr and Sonarr, using the SABnzbd-compatible api.

<p align="center">
    <img width="600" alt="webdav-settings" src="https://github.com/user-attachments/assets/5ef6a362-7393-4b98-980a-a9e0e159ed72" />
</p>

### Steps
* Radar will send an *.nzb to NZB-Dav to "download"
* NZB-Dav will mount the nzb onto the webdav without actually downloading it.
* RClone will make the nzb contents available to your filesystem by streaming, without using any storage space on your server.
* NZB-Dav will tell Radarr that the "download" has completed within the `/mnt/nzbdav/completed-symlinks` folder.
* Radarr will grab the symlinks from `/mnt/nzbdav/completed-symlinks` and will move them to wherever you have your media library.
* The symlinks always point to the `/mnt/nzbdav/content` folder which contain the streamable content.
* Plex accesses one of the symlinks from your media library, it will automatically fetch and stream it from the mounted webdav.


# Example Docker Compose Setup
Fully containerized setup for docker compose. 

See rclone [docs](https://rclone.org/docker/) for more info.

Verify FUSER driver is installed:
```
$ fusermount3 --version
```

Install FUSER driver if needed:
- `sudo pacman -S fuse3` OR
- `sudo dnf install fuse3` OR
- `sudo apt install fuse3` OR
- `sudo apk add fuse3`
- etc...


Install the rclone volume plugin:
```
$ sudo mkdir -p /var/lib/docker-plugins/rclone/config
$ sudo mkdir -p /var/lib/docker-plugins/rclone/cache
$ docker plugin install rclone/docker-volume-rclone:amd64 args="-v --links --buffer-size=1024" --alias rclone --grant-all-permissions
```
You can set any options here in the `args="..."` section. The command above sets bare minimum, and must be accompanied with more options in the example compose file.

Move or create `rclone.conf` in `/var/lib/docker-plugins/rclone/config/`. Contents should follow the [example](https://github.com/nzbdav-dev/nzbdav?tab=readme-ov-file#rclone).

In your compose.yaml... **NOTE: Ubuntu container is not required, and is only included for testing the rclone volume.**
```yml
services:
  nzbdav:
    image: ghcr.io/nzbdav-dev/nzbdav
    environment:
      - PUID=1000
      - PGID=1000
    ports:
      - 3000:3000
    volumes:
      - /opt/stacks/nzbdav:/config
    restart: unless-stopped

  ubuntu:
    image: ubuntu
    command: sleep infinity
    volumes:
      - nzbdav:/mnt/nzbdav
    environment:
      - PUID=1000
      - PGID=1000

  radarr:
    volumes:
      - nzbdav:/mnt/nzbdav # Change target path based on SABnzbd rclone mount directory setting.

# the rest of your config ...

volumes:
  nzbdav:
    driver: rclone
    driver_opts:
      remote: 'nzb-dav:'
      allow_other: 'true'
      vfs_cache_mode: off
      dir_cache_time: 1s
      allow_non_empty: 'true'
      uid: 1000
      gid: 1000

```

To verify proper rclone volume creation:
```
$ docker exec -it <ubuntu container name> bash
$ ls -la /mnt/nzbdav
```

## Accessing the rclone volume from a separate stack.
Note: Your rclone volume **must** be already created by another stack, for example: 

- Media backend: nzbdav + sonarr + radarr <--- This stack is creating the rclone volume
- Media frontend: jellyfin <--- Mounts the external arrstack rclone volume

To do so, see the bottom 11 lines in the example compose file in the above section.

The example below uses ubuntu again, but the concept is the same for a different container such as sonarr.


Find the stack name that creates the rclone volume:
```
$ docker-compose ls
```

Combine in the new separate compose file:
```yml
services:
  ubuntu:
    image: ubuntu
    container_name: ubuntu
    command: sleep infinity
    volumes:
      - nzbdav:/mnt/nzbdav # -- IMPORTANT --
    environment:
      - PUID=1000 # Must match UID value from volume in the stack creating the volume (driver_opts).
      - PGID=1000 # Must match GID value from volume in the stack creating the volume (driver_opts).

volumes:
  nzbdav:
    name: <STACK NAME>_nzbdav # See above for finding the stack name. # -- IMPORTANT --
    external: true # -- IMPORTANT --
```


# More screenshots
<img width="300" alt="onboarding" src="https://github.com/user-attachments/assets/4ca1bfed-3b98-4ff2-8108-59ed07a25591" />
<img width="300" alt="queue and history" src="https://github.com/user-attachments/assets/6ae64b41-2ec4-4c40-9c40-de23e42a4178" />
<img width="300" alt="dav-explorer" src="https://github.com/user-attachments/assets/0e72e987-2fc1-44b2-9ced-17aebbfbf823" />
