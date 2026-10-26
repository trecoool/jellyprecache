# JellyPrecache

A Jellyfin plugin that makes thumbnails load instantly, even when the original images sit on a sleeping HDD.
Ever wondered why your thumbnails load slow even though you've set all of it to live on your cache?
Jellyfin requests images at the exact pixel size of the card on screen. Cards scale with the window, so almost every window size asks for a new width (187, 240, 280 px…). Each new size having a high chance to be unique, it misses the cache, so Jellyfin reads the original from disk and resizes it. When scrolling through a large libraries that's a lot of image resizes, which is slow and eats up cpu cycles. If you like to save the original on hdd for easy access, it's even worse. Keeping all this multitude of sizes on cache is not reasonable either, hence why jellyfin clears it every 30 days by default.

## What it does

- **Size snapping:** all image requests are rounded up to a few fixed configurable widths, so that any request is now served at a predetermined size. Requests without a size pass through untouched.
- **Pre-generation:** scheduled task that generates every predetermined sizes for the whole library ahead of time, for butter smooth navigation, even with originals on hdd.

## Configuration

**Dashboard → Plugins → JellyPrecache**:

- Widths per image type (Primary, Backdrop, Thumb, Logo, Banner, Disc). Each type can be disabled.
- Quality (default 85). Keep it below 90: at 90 and above, Jellyfin serves originals smaller than the target straight from disk, bypassing the cache.

Changing widths or quality changes the cache keys: run the task again to regenerate.

Notes:
- Pre-generated images are WebP (what browsers ask for). Clients without WebP support get a JPG generated on first request.
- On a 10,000 items library, total size for all cached images with the 2 defaults sizes (posters & backdrop) and one size for the rest, amounts to less than 2GB.
- Jellyfin's built-in **Clean Cache Directory** task deletes cache files older than 30 days. The nightly task regenerates them, but disabling the cleanup avoids that churn.
- Disabling the cleanup would leave all pre-generated and now useless images in cache forever, so the plugin comes with a full rebuild button : delete the whole cache, re-download any missing original that jellyfin may have lost, then cache everything.
- Check out my other plugins: [JellyRate](https://github.com/trecoool/jellyrate) and [JellyRequest](https://github.com/trecoool/jellyrequest).

## Requirements

- Jellyfin 10.11.x or later
- .NET 9.0

## Installation

### Build from source

```bash
dotnet build -c Release
```

Copy `bin/Release/net9.0/JellyPrecache.dll` to `<jellyfin-config>/plugins/JellyPrecache/`, restart Jellyfin, then run **Pre-generate image sizes** once from **Dashboard → Scheduled Tasks**. The first run covers the whole library and can take a while.
