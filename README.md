# JellyPrecache

A Jellyfin plugin that makes thumbnails load instantly, even when the original images sit on a sleeping HDD.
Ever wondered why your thumbnails load slow even though you've set all of it to live on your cache?
Jellyfin requests images at the exact pixel size of the card on screen. Cards scale with the window, so almost every window size asks for a new width (187, 240, 280 px…). Each new size having a high chance to be unique, it misses the cache, so Jellyfin reads the original from disk and resizes it. When scrolling through a large libraries that's a lot of image resizes, which is slow and eats up cpu cycles. If you like to save the original on hdd for easy access, it's even worse.
Keeping all these unique sizes on cache is not reasonable either, as it just keeps growing. This is why by default jellyfin clears any cached item older than 30 days.

## What it does

- **Size snapping:** the client image requests are snapped to a set configurable widths : if the configured set is [300, 600], a 546px card will request a 600px rather than a 546px.
- **Pre-generation:** a scheduled task generates every sizes for the whole library : image requests are now always cache hits. Butter smooth navigation, even with images in their media folder.
- **Cache maintenance:** **Prune** deletes cached images nothing uses anymore; **Rebuild** starts the cache over.

## Configuration

**Dashboard → Plugins → JellyPrecache**:

- Widths per image type (Primary, Backdrop, Thumb, Logo, Banner, Disc). Each type can be disabled.
- Formats per image type: **WebP** (browsers and most apps, on by default) and **Fallback** (JPG, or PNG for images with transparency, for apps without WebP such as some TV apps). Formats left off are still served, just generated on first request.
- Quality (default 85). Keep it below 90: at 90 and above, Jellyfin serves originals smaller than the target straight from disk, bypassing the cache.
- **Generate cached images immediately when media is added** (off by default): also applies when an item gets new images.

Changing widths or quality changes the cache keys: run the task again to regenerate, then **Prune** to remove the old sizes.

### Cache section

The bottom of the config page shows the cache size per format and the last result of each task, with live progress while one runs.

- **Generate now** runs **Pre-generate image sizes**.
- **Prune** computes the name of every file Jellyfin can create for a configured width of a current library image (WebP and fallback, whether or not Fallback is ticked), then deletes every other file in the resized cache: old sizes, removed items, images replaced since, sizes from before snapping. It first asks Jellyfin for a random sample and checks the names match; on any difference, or if over half the cache would go, it stops without deleting anything. Images the plugin doesn't handle (chapter thumbnails, blurred images, other image types) are deleted too and recreated by Jellyfin on next view. Prune takes seconds and doesn't generate missing sizes.
- **Rebuild** (asks for confirmation) deletes every resized image, re-downloads originals whose file is missing from the metadata providers, then regenerates everything. Thumbnails load slowly until it finishes.

The three tasks also appear under **Dashboard → Scheduled Tasks** and never run at the same time. Only Pre-generate has a default schedule.

Notes:
- Pre-generated images are WebP (what browsers ask for). Clients without WebP support get a JPG generated on first request. Since these clients have more predictable sizes, these formats are disabled by default.
- On a ~10,000 items library, total size for all cached images with the default sizes (2 for posters & backdrop, 1 for the rest) amounts to less than 2GB.
- Jellyfin's built-in **Clean Cache Directory** task deletes cache files older than 30 days. The plugin regenerates them, so disabling jellyfin's cleanup task or making it far less frequent is recommended.
- Disabling the cleanup leaves all jellyfin's previously cached images in cache forever. The plugin comes with a "rebuild" button : delete the whole cache, re-download any missing original that Jellyfin may have lost to ensure all original exists, then cache everything.
- Check out my other plugins: [JellyRate](https://github.com/trecoool/jellyrate) and [JellyRequest](https://github.com/trecoool/jellyrequest).

## Requirements

- Jellyfin 10.11.x or later
- .NET 9.0

## Installation

### Via plugin repository (recommended)

1. In Jellyfin, go to **Dashboard → Plugins → Repositories**.
2. Click **+** and add:
   - **Name**: `JellyPrecache`
   - **URL**: `https://raw.githubusercontent.com/trecoool/jellyprecache/master/manifest.json`
3. Open the **Catalog** tab, find **JellyPrecache**, and install it.
4. Restart Jellyfin.

### Manual install

1. Download the latest `jellyprecache_<version>.zip` from the [Releases](https://github.com/trecoool/jellyprecache/releases) page.
2. Extract `JellyPrecache.dll` to `<jellyfin-config>/plugins/JellyPrecache/`.
3. Restart Jellyfin.

### Build from source

```bash
dotnet build -c Release
```

Copy `bin/Release/net9.0/JellyPrecache.dll` to `<jellyfin-config>/plugins/JellyPrecache/`, then restart Jellyfin.

### First run

Run **Pre-generate image sizes** once from **Dashboard → Scheduled Tasks** (or **Generate now** on the plugin page). The first run covers the whole library and can take a while.
