# About `/content`

This directory contains all streamable files that have finished processing from the nzbdav queue.

It is mostly read-only. Files cannot be created, renamed, or moved within this directory.

However, files can be deleted from this directory if you no longer want them, using any compatible webdav client (or using Rclone). But only when the setting under `Settings -> WebDAV -> Enforce Read-Only` is disabled. Deleting items from this directory may break symlinks that point to those items, so be certain you no longer need them before doing so.

> Note: You can run the maintenance task under `Settings -> Maintenance -> Remove Orphaned Files` to remove all files from the `/content` folder that are no longer symlinked by your media library.

---

# About `/completed-symlinks`

This directory contains symlinks for items that have finished processing the nzbdav queue and are still present in the nzbdav history table.

It is read-only. Files cannot be created, renamed, moved, or deleted from this directory. Files in this directory can only be read and copied out of the webdav.

All items under this directory have the *.rclonelink extension, since true symlinks cannot exist on a webdav. Instead, the *.rclonelink files are simple text files whose contents contain the target path of where the symlink should point to.

If using Rclone to mount the webdav onto your filesystem, then Rclone will take care of translating these *.rclonelink files to actual symlinks. You'll need to use the `--links` argument for Rclone to perform this translation.

> NOTE: Be sure to use an updated version of rclone that supports the `--links` argument.
> * Version `v1.70.3` has been known to support it.
> * Version `v1.60.1-DEV` has been known _not_ to support it.

---

# About `/nzbs`

This directory mirrors the nzbdav queue
* Any nzb currently in the queue can be retrieved from this directory.
* You can remove items from the queue by deleting the corresponding nzb from this directory
* You can add items to the queue by uploading nzb files to this directory

> Note: You must perform file operations using any compatible webdav client (or Rclone). The "Dav Explore" page on nzbdav UI does not currently support file operations.