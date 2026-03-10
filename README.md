<p align="center">
  <img width="1101" height="238" alt="image" src="https://github.com/user-attachments/assets/b14165f4-24ff-4abe-8af6-3ca852e781d4" />
</p>

# Nzb Dav

NzbDav is a WebDAV server that allows you to mount and browse NZB documents as a virtual file system without downloading. It's designed to integrate with other media management tools, like Sonarr and Radarr, by providing a SABnzbd-compatible API. With it, you can build an infinite Plex or Jellyfin media library that streams directly from your usenet provider at maxed-out speeds, without using any storage space on your own server.

Check the video below for a demo:

https://github.com/user-attachments/assets/f14a0cf7-b19c-4b36-a909-59ca2a3771ef

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
    <img width="600" alt="settings-page" src="https://github.com/user-attachments/assets/91175920-5a7b-4a93-906d-b8432f35c809" />
</p>

You'll also want to set up a username and password for logging in to the webdav server

<p align="center">
    <img width="600" alt="webdav-settings" src="https://github.com/user-attachments/assets/833b382c-4e1d-480a-ac25-b9cc674baea4" />
</p>

# Comprehensive Setup Guide

If you'd like to get the most out of NzbDav, check out the [comprehensive guide](docs/setup-guide.md) for detailed instructions covering:
* **Docker Compose:** Full stack with Rclone sidecar and healthchecks.
* **Performance Tuning:** Benchmarking WebDAV connection limits.
* **Integrations:** Automating Radarr/Sonarr queue management and repairs.
* **Stremio:** Streaming Usenet directly via AIOStreams.

# More screenshots
<img width="300" alt="onboarding" src="https://github.com/user-attachments/assets/4ca1bfed-3b98-4ff2-8108-59ed07a25591" />
<img width="300" alt="queue and history" src="https://github.com/user-attachments/assets/912c0f02-e44e-49ea-b4c7-8a1a106e8a01" />
<img width="300" alt="dav-explorer" src="https://github.com/user-attachments/assets/54a1d49b-8a8d-4306-bcda-9740bd5c9f52" />
<img width="300" alt="health-page" src="https://github.com/user-attachments/assets/7815acb9-6696-49c3-88d6-ea673b52da1c" />

-------

**NOTE:**
**NZBDAV is intended for use with legally obtained content only. The project maintainers do not condone piracy and will not provide support for users suspected of engaging in copyright infringement.**

