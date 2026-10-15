# Live browser captures

Captured on 15 October 2026 from the running local browser host. Images are unaltered browser captures at its normal viewport; large application canvases extend beyond the visible area. These are browser observations, not native desktop reference images or pixel-parity claims.

## Calculator input

![The calculator showing 15 after entering 7 plus 8 in the browser.](calculator.jpg)

Interaction: click `7`, `+`, `8`, `=` in a fresh browser session. The display changes to `15`. The displayed load time is time to the first canvas frame in that session; it is not a cold-download benchmark.

## Photo Album drag

![Photo Album before dragging the dog photo.](photo-album-before.jpg)

![Photo Album after dragging the dog photo toward the upper right.](photo-album.jpg)

Interaction: drag the dog photo from the middle of the canvas to the upper right. The before and after captures show its changed position. The browser host shows loading and byte measurements above the canvas; cache state affects those measurements.

These external applications were supplied separately for local testing. Their files and conversion outputs are excluded from the repository. See [third-party notices](../../THIRD-PARTY-NOTICES.md).

## Base project

![The source-only base build showing one available form and a successful button click.](base-project.jpg)

This separate build used only the source distribution and restored build dependencies. The library contains the built-in form, with no external applications installed. Clicking its button increments the visible success counter.
