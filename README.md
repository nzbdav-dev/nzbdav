
<p align="center">
  <img width="1101" height="238" alt="image" src="https://github.com/user-attachments/assets/b14165f4-24ff-4abe-8af6-3ca852e781d4" />
</p>

# Nzb Dav

NzbDav is a WebDAV server that allows you to mount NZB documents and access your Usenet downloads as a standard file system. It's designed to integrate with other media management tools like Sonarr and Radarr, by providing a SABnzbd-compatible API. With it, you can build an infinite Plex or Jellyfin media library that streams directory from your usenet provider at maxed-out speeds, without using any storage space on your own server.

Check the video below for a demo:

https://github.com/user-attachments/assets/d9f8caea-bb65-422e-831d-61d626d5b453


# Key Features

* 📁 **WebDAV Server** - *Provides a WebDAV endpoint for seamless integration.*
* ☁️ **Mount NZB Documents** - *Mount and browse NZB documents as a virtual file system without downloading.*
* 📽️ **Full Streaming and Seeking Abilities** - *Jump ahead to any point in your video streams.*
* 🗃️ **Automatic Unrar** - *View, stream, and seek content within RAR archives*
* 🧩 **SABnzbd-Compatible API** - *Integrate with Sonarr/Radarr and other tools using a compatible API.*


# Getting Started

The easiest way to get started is by using the official Docker image.

To try it out, run the following command to pull and run the image with port `3000` exposed:

```bash
docker run --rm -it -e PUID=1000 -e PGID=1000 -p 3000:3000 ghcr.io/nzbdav-dev/nzbdav:pre-alpha
```

And if you would like to persist saved settings, attach a volume at `/config`

```
mkdir -p $(pwd)/nzbdav && \
docker run --rm -it \
  -v $(pwd)/nzbdav:/config \
  -e PUID=1000 \
  -e PGID=1000 \
  -p 3000:3000 \
  ghcr.io/nzbdav-dev/nzbdav:pre-alpha
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
This setup disables Rclone's caching and streams directly, since the  end-client (Plex/VLC/Chrome/etc) will already buffer-ahead anyway
```
--vfs-cache-mode=off
--buffer-size=1024
--dir-cache-time=1s
--links
```

The "--links" setting in RClone is important. It allows *.rclonelink files within the webdav to be translated to symlinks when mounted onto your filesystem.

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
* The symlinks always point to the `/mnt/nzbdav/completed` folder which contain the streamable content.
* Plex accesses one of the symlinks from your media library, it will automatically fetch and stream it from the mounted webdav.

# More screenshots
<img width="300" alt="onboarding" src="https://github.com/user-attachments/assets/4ca1bfed-3b98-4ff2-8108-59ed07a25591" />
<img width="300" alt="queue and history" src="https://github.com/user-attachments/assets/6ae64b41-2ec4-4c40-9c40-de23e42a4178" />
<img width="300" alt="dav-explorer" src="https://github.com/user-attachments/assets/0e72e987-2fc1-44b2-9ced-17aebbfbf823" />
