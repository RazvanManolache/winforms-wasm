# gallery-contracts: real-browser app workflow checks (one runner, python+playwright)

## Run
`./scripts/test-gallery.ps1 [-Cases a,b] [-Port 5213] [-Negative]`
Generation pinned per run (TransformRevision + binary head hashes);
`verify.py` rejects stale/wrong-generation/missing/replayed evidence;
`selftest.py` proves the rejection set without a browser.

## Oracles used today
- Console lifecycle markers (`constructed`, `first-painted`, `[Scene]`
  bounds, `[Host] OnClick hit=`) â€” semantic routing proof.
- Region pixel diffs before/after real pointer/keyboard input.
- cv2.QRCodeDetector independent decode (barcode).
- Inventory count-based delta per case window (log dir is shared between
  :5210 and :5213; time/count attribution, limitation noted).

## Known gaps (do not weaken cases to green these)

**22 October follow-up:** a fresh manual Paint.NET brush/Ctrl+Z/Ctrl+Y roundtrip succeeded and was checked using saved document pixels. The older modifier observations below describe an earlier run. The gallery runner has not been rerun to establish its current automated behavior; see [the review](../../docs/review-2026-10-22.md).

- Modifier chords broken (2 witnesses): Ctrl+A types literal 'a' in
  fork TextBox (barcode input); Ctrl+Z does nothing in paint.net.
  Plain keys (Space/Enter/typing) work. Root cause in key dispatch or
  fork modifier tracking; fix unlocks barcode select-all + paint undo.
- No text/model readback bridge: exact counter values, list contents,
  MDI document identity are `blocked` (geometry/pixel diffs only).
- Paint undo currently keyboard-only in the case; toolbar/menu undo
  items parse now ([Item] support) but Undo identity unprobed.
- MDI File-menu create/close: menu items have no dumped bounds;
  tab-strip switching only.
- Performance/backendEvidence/hardware-GPU: untested in every row
  (separate gates own these).
