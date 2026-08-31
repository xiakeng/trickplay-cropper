# SkiaSharp JPEG region decoding for Jellyfin 10.11.11

## Decision

Use SkiaSharp 3.116.1's scanline decoder, not `GetPixels` subset decoding, for the v1 JPEG crop path.

The public API can produce the exact requested cell without allocating a decoded bitmap for the whole sprite:

1. Create one `SKCodec` for the sprite file.
2. Keep the decoder `SKImageInfo` at the full sprite dimensions.
3. Pass a full-height horizontal subset `(cropX, 0, cropWidth, spriteHeight)` to `StartScanlineDecode`.
4. Call `SkipScanlines(cropY)`.
5. Decode `cropHeight` rows with `GetScanlines` into a bitmap allocated at only `cropWidth x cropHeight`.
6. Encode that bitmap directly to the cache output stream as JPEG quality 90.

This is a real partial decode in the useful resource sense, but it is not a strict "decode only these compressed bytes" operation. JPEG entropy dependencies still require sequential work. The implementation and product wording should promise that it avoids a full decoded sprite bitmap and avoids most pixel-domain work outside the target cell, not that the codec touches only the target rectangle.

## Pinned compatibility contract

Jellyfin 10.11.11 targets `net9.0` and pins `SkiaSharp`, `SkiaSharp.HarfBuzz`, and `SkiaSharp.NativeAssets.Linux` to 3.116.1 ([server target](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server/Jellyfin.Server.csproj#L8-L12), [central package versions](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Directory.Packages.props#L76-L79)). Its drawing project loads those packages into the server process ([drawing project references](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/src/Jellyfin.Drawing.Skia/Jellyfin.Drawing.Skia.csproj#L20-L27)). The conclusions below are pinned to that exact host-provided version.

The [SkiaSharp 3.116.1 NuGet package](https://www.nuget.org/packages/SkiaSharp/3.116.1) contains a `net8.0` compile/runtime assembly, which is compatible with the `net9.0` plugin target. Its package metadata points to SkiaSharp commit `e57e2a11dac4ccc72bea52939dede49816842005`; that commit pins the Skia fork at `c16e913577083761d847146db7a04b8d3b3bf755` ([submodule pointer](https://github.com/mono/SkiaSharp/tree/e57e2a11dac4ccc72bea52939dede49816842005/externals)). That Skia commit pins Chromium's libjpeg-turbo fork at `9b894306ec3b28cea46e84c32b56773a98c483da` ([Skia DEPS](https://github.com/mono/skia/blob/c16e913577083761d847146db7a04b8d3b3bf755/DEPS#L42)).

## What the public managed API actually exposes

SkiaSharp 3.116.1 exposes all required scanline operations:

- `SKCodec.StartScanlineDecode(SKImageInfo, SKCodecOptions)`;
- `SKCodec.SkipScanlines(int)`;
- `SKCodec.GetScanlines(IntPtr, int, int)`;
- `SKCodec.NextScanline` and `ScanlineOrder` for diagnostics.

The exact managed bindings are visible in [`SKCodec.cs`](https://github.com/mono/SkiaSharp/blob/e57e2a11dac4ccc72bea52939dede49816842005/binding/SkiaSharp/SKCodec.cs#L180-L225), while `SKCodecOptions` publicly carries a nullable `SKRectI Subset` ([definition](https://github.com/mono/SkiaSharp/blob/e57e2a11dac4ccc72bea52939dede49816842005/binding/SkiaSharp/Definitions.cs#L204-L253)).

There are three important traps:

1. **JPEG `GetValidSubset` is not the scanline capability test.** `SkJpegCodec` does not override the default `onGetValidSubset`, which returns false ([default implementation](https://github.com/mono/skia/blob/c16e913577083761d847146db7a04b8d3b3bf755/include/codec/SkCodec.h#L809-L812), [JPEG overrides](https://github.com/mono/skia/blob/c16e913577083761d847146db7a04b8d3b3bf755/src/codec/SkJpegCodec.h#L49-L141)). Calling managed `GetValidSubset` and treating false as rejection would incorrectly disable the working JPEG scanline path.
2. **JPEG `GetPixels` subset decoding is unavailable.** The general `getPixels` path first requires `getValidSubset`, and the JPEG implementation explicitly returns `kUnimplemented` when a subset is present ([general validation](https://github.com/mono/skia/blob/c16e913577083761d847146db7a04b8d3b3bf755/src/codec/SkCodec.cpp#L430-L475), [JPEG implementation](https://github.com/mono/skia/blob/c16e913577083761d847146db7a04b8d3b3bf755/src/codec/SkJpegCodec.cpp#L645-L686)).
3. **JPEG incremental subset decoding is unavailable.** The JPEG codec does not override the default incremental entry point, whose default is `kUnimplemented` ([default virtual methods](https://github.com/mono/skia/blob/c16e913577083761d847146db7a04b8d3b3bf755/include/codec/SkCodec.h#L963-L981)).

The full-height horizontal-subset shape is mandatory. Skia rejects a scanline subset unless its top is zero and its height equals the full decoder height; vertical selection is intentionally performed by `SkipScanlines` ([validation and state handling](https://github.com/mono/skia/blob/c16e913577083761d847146db7a04b8d3b3bf755/src/codec/SkCodec.cpp#L580-L674)). The crop rectangle from the product specification therefore must **not** be passed directly when `cropY > 0`.

Skia's own codec test exercises this exact partial-scanline contract for JPEG: it starts with a full-height horizontal subset and then reads partial scanlines ([upstream test](https://github.com/mono/skia/blob/c16e913577083761d847146db7a04b8d3b3bf755/tests/CodecTest.cpp#L358-L375)).

## Correct decode shape

The implementation should follow this logical shape (names are illustrative, not a committed API):

```text
codecInfo = codec.Info                         // full sprite width and height
horizontalSubset = Rect(cropX, 0,
                        cropX + cropWidth,
                        codecInfo.Height)

destination = Bitmap(cropWidth, cropHeight,
                     codecInfo color/alpha/color-space)

StartScanlineDecode(codecInfo,
                    CodecOptions(horizontalSubset))
SkipScanlines(cropY)
GetScanlines(destination.Pixels,
             cropHeight,
             destination.RowBytes)
```

Before starting, require all of the following with checked arithmetic:

- `codec.EncodedFormat == JPEG`;
- positive sprite and crop dimensions;
- `cropX >= 0`, `cropY >= 0`;
- `cropX + cropWidth <= codec.Info.Width`;
- `cropY + cropHeight <= codec.Info.Height`;
- a successfully allocated cell-sized destination bitmap.

Require `StartScanlineDecode == Success`, every skip to succeed, and the total number returned by `GetScanlines` to equal `cropHeight`. `getScanlines` fills missing rows after truncated input while returning the smaller decoded-row count ([incomplete-input behavior](https://github.com/mono/skia/blob/c16e913577083761d847146db7a04b8d3b3bf755/src/codec/SkCodec.cpp#L639-L655)); encoding that partially filled bitmap would conceal source corruption.

Use one codec instance per request/decode. The codec stores mutable current-scanline and native decompressor state, so it must not be shared across concurrent requests.

## Horizontal crop limits and actual JPEG work

After `jpeg_start_decompress`, Skia calls `jpeg_crop_scanline`. libjpeg-turbo may move the requested left edge down to an iMCU boundary and increase the decoder width so the requested right edge is preserved. Skia then creates a one-row swizzler when necessary and removes the extra pixels, so the caller still receives the exact requested horizontal range ([Skia JPEG setup](https://github.com/mono/skia/blob/c16e913577083761d847146db7a04b8d3b3bf755/src/codec/SkJpegCodec.cpp#L780-L843), [libjpeg crop implementation](https://chromium.googlesource.com/chromium/deps/libjpeg_turbo/+/9b894306ec3b28cea46e84c32b56773a98c483da/jdapistd.c#145)).

For the v1 320-pixel cells generated by Jellyfin, the horizontal origin is `column * 320`. Jellyfin creates the sprite through `SKBitmap.Encode(..., JPEG, quality)` ([Jellyfin tile encoder](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/src/Jellyfin.Drawing.Skia/SkiaEncoder.cs#L709-L779)); that overload uses `SKJpegEncoderOptions(quality)`, whose default is 4:2:0 subsampling ([managed encoder path](https://github.com/mono/SkiaSharp/blob/e57e2a11dac4ccc72bea52939dede49816842005/binding/SkiaSharp/SKPixmap.cs#L221-L246), [option defaults](https://github.com/mono/SkiaSharp/blob/e57e2a11dac4ccc72bea52939dede49816842005/binding/SkiaSharp/Definitions.cs#L533-L568)). At 1:1 decode, the usual 4:2:0 iMCU width is 16 pixels, and every 320-pixel cell boundary is aligned. This means the standard Jellyfin source should normally need no left-edge expansion, although the code must still allow Skia to handle it.

The work saved is stage-specific:

- **Output allocation:** only the cell bitmap is allocated; no full decoded sprite raster is required.
- **Horizontal pixel work:** libjpeg still entropy-decodes every MCU needed to advance through a row, but it runs IDCT only for MCU columns inside the crop region ([one-pass decoder](https://chromium.googlesource.com/chromium/deps/libjpeg_turbo/+/9b894306ec3b28cea46e84c32b56773a98c483da/jdcoefct.c#84)).
- **Vertical work for baseline JPEG:** `jpeg_skip_scanlines` can discard full iMCU rows without producing pixels, but it still advances the entropy stream; boundary rows may be read and discarded ([skip implementation](https://chromium.googlesource.com/chromium/deps/libjpeg_turbo/+/9b894306ec3b28cea46e84c32b56773a98c483da/jdapistd.c#405)).
- **Progressive or multi-scan JPEG:** libjpeg performs the entropy decode during `jpeg_start_decompress` before scanline skipping, so the CPU and coefficient-storage savings are smaller. The no-full-output-bitmap property still holds. Jellyfin's pinned encoder calls `jpeg_set_defaults` and does not enable a progressive scan script ([Skia encoder setup](https://github.com/mono/skia/blob/c16e913577083761d847146db7a04b8d3b3bf755/src/encode/SkJpegEncoderImpl.cpp#L140-L179), [libjpeg defaults](https://chromium.googlesource.com/chromium/deps/libjpeg_turbo/+/9b894306ec3b28cea46e84c32b56773a98c483da/jcparam.c#181)), so Jellyfin-generated v1 sprites are the baseline case.

Skia's JPEG wrapper allocates only row-oriented swizzle/color-transform scratch when those conversions are necessary ([allocation logic](https://github.com/mono/skia/blob/c16e913577083761d847146db7a04b8d3b3bf755/src/codec/SkJpegCodec.cpp#L689-L714)). Avoid the managed `GetPixels(out byte[])` helper, which immediately allocates `info.BytesSize` for the full dimensions ([managed allocation](https://github.com/mono/SkiaSharp/blob/e57e2a11dac4ccc72bea52939dede49816842005/binding/SkiaSharp/SKCodec.cs#L94-L110)), and avoid `SKBitmap.Decode`, which allocates a bitmap at the decoder dimensions.

## Input, output, cancellation, and resource limits

Prefer `SKCodec.Create(spritePath)` or a seekable file stream. The filename overload uses a native `SKFileStream`; the managed stream overload wraps a seekable stream without first copying the whole file, while non-seekable streams require front buffering ([creation and stream wrapping](https://github.com/mono/SkiaSharp/blob/e57e2a11dac4ccc72bea52939dede49816842005/binding/SkiaSharp/SKCodec.cs#L227-L289)).

Encode the cell bitmap directly to the cache file stream. The `SKPixmap.Encode(Stream, JPEG, quality)` path writes through an `SKManagedWStream` ([encode binding](https://github.com/mono/SkiaSharp/blob/e57e2a11dac4ccc72bea52939dede49816842005/binding/SkiaSharp/SKPixmap.cs#L221-L246)); using `SKData` followed by `ToArray` would introduce avoidable native and managed copies. Dispose the codec, destination bitmap/pixmap, output stream, and every temporary Skia object deterministically.

The public scanline methods are synchronous and expose no `CancellationToken`. Cancellation can therefore be cooperative only between native calls:

- honor cancellation while waiting for a bounded decode-concurrency gate;
- check it before open/start, between bounded skip batches, between decoded row batches, and before encode/publish;
- understand that cancellation cannot interrupt a currently executing native `SkipScanlines`, `GetScanlines`, or JPEG encode call.

Skipping in moderate batches rather than one unbounded `SkipScanlines(cropY)` call creates cancellation checkpoints without falling back to per-row overhead. Decoding the target cell in bounded row batches provides the same property. The exact batch size is an implementation tuning detail, not an API guarantee.

The server must also cap concurrent decodes, validate all dimension products with checked arithmetic, and reject implausible/corrupt dimensions before allocation. An async controller action by itself does not make this CPU-bound native work asynchronous or cancellable.

## Failure and fallback policy

Do **not** silently fall back to full-sprite `SKBitmap.Decode` in v1. The exact pinned JPEG codec supports the scanline path, while an unbounded full decode defeats the primary memory guarantee and turns malformed or unexpectedly large sprite dimensions into a native-memory risk.

Treat any of these as a decode failure: non-JPEG input, a non-success start result, a failed skip, a short row count, or a failed JPEG encode. Log the full diagnostic parameters required by the product specification, do not publish/cache a partial file, and return the existing unexpected-image-processing error (`500`).

A future full-decode fallback would be safe only behind both a hard decoded-byte limit and the same concurrency gate. It is not needed for the pinned v1 contract.

## Native dependency and plugin ZIP decision

SkiaSharp is host-provided for Jellyfin 10.11.11. The plugin should compile against **exactly 3.116.1** but must not ship its own `SkiaSharp.dll`, `libSkiaSharp`, `SkiaSharp.NativeAssets.*`, or `runtimes/` tree.

Use the Jellyfin plugin-template pattern of excluding host runtime assets from the compile-time package reference ([template package references](https://github.com/jellyfin/jellyfin-plugin-template/blob/master/Jellyfin.Plugin.Template/Jellyfin.Plugin.Template.csproj#L13-L20)) and whitelist only plugin-owned artifacts in packaging ([template artifact list](https://github.com/jellyfin/jellyfin-plugin-template/blob/master/build.yaml#L12-L15)). Do not add a direct `SkiaSharp.NativeAssets.Linux` dependency. That package carries RID-specific native libraries ([package project](https://github.com/mono/SkiaSharp/blob/e57e2a11dac4ccc72bea52939dede49816842005/binding/SkiaSharp.NativeAssets.Linux/SkiaSharp.NativeAssets.Linux.csproj#L7-L17)); bundling it would duplicate the server runtime, enlarge the ZIP, create version/loader conflicts, and make a supposedly platform-neutral plugin artifact Linux-specific.

Add a package-content assertion in CI: the plugin ZIP may contain the plugin assembly and other explicitly owned plugin files, but no file named `SkiaSharp.dll`, no `libSkiaSharp*`, and no `runtimes/` directory.

## Verification notes

The decision was verified against the exact Jellyfin, SkiaSharp, Skia, and libjpeg-turbo source revisions listed above, plus the first-party NuGet package metadata. A local executable smoke test was not run because the research environment has no .NET SDK installed. The upstream Skia codec test cited above directly covers partial JPEG scanlines, and the pinned implementation source is sufficient to determine the public contract and its limitations.
