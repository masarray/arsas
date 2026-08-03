# IED protection relay fascia design QA

- Source visual truth: `C:\Users\me\AppData\Local\Temp\codex-clipboard-2ff37ea3-1b1d-4eb5-a340-121885a3c2f2.png`
- Implementation asset: `D:\Git\arsas\Assets\ied-protection-relay-fascia.png`
- WPF consumer: `IedRelayFrontPanelTemplate` in `App.xaml`, rendered at 50 x 50 device-independent pixels
- Source pixels: 400 x 400
- Implementation pixels: 1250 x 1250, normalized to 400 x 400 for comparison
- Density check: implementation was also downsampled to 50 x 50 and enlarged with nearest-neighbor only for legibility inspection
- State: static fascia artwork; LIVE/STOP and connection state remain data-driven WPF overlays

## Full-view comparison evidence

Comparison image: `C:\Users\me\.codex\visualizations\2026\08\03\019fc7bf-2e84-7492-be9f-2cb90454829e\arsas-relay-reference-vs-fascia-asset.png`

The implementation preserves the reference's defining protection-relay proportions: nearly square panel-mount bezel, warm-gray fascia, wide dark display surround, central pale LCD, left and right LED banks, cyan directional keypad, and a separate vertical function-key bank. Brand and model marks were intentionally omitted.

## Focused-region comparison evidence

Small-scale image: `C:\Users\me\.codex\visualizations\2026\08\03\019fc7bf-2e84-7492-be9f-2cb90454829e\arsas-relay-fascia-at-50px.png`

At the production 50 x 50 size, micro-copy is no longer readable, as expected, but the dark square bezel, LCD, LED columns, and cyan keypad remain distinct. The icon reads as a feeder protection relay rather than a calculator.

## Required fidelity surfaces

- Fonts and typography: no manufacturer or product typography was copied; micro-measurements remain screen texture at icon scale.
- Spacing and layout rhythm: the bezel, display block, and lower keypad retain the reference's upper-display/lower-controls hierarchy.
- Colors and visual tokens: charcoal, warm gray, pale LCD blue, restrained status LEDs, and cyan keys match the source character without adding glow.
- Image quality and asset fidelity: a dedicated raster asset is used, with high-quality WPF bitmap scaling; no placeholder or approximate keypad geometry remains in XAML.
- Copy and content: no brand name or model identifier is included; ARSAS LIVE/STOP copy remains outside the artwork and data-driven.

## Findings

No actionable P0, P1, or P2 fidelity differences remain for the fascia component.

## Comparison history

- Initial vector implementation was too tall and contained a dial and terminal strip, so it did not match the selected square feeder-relay reference.
- Replaced it with a dedicated square fascia asset, removed the colored device glow, moved the state badge away from the LCD, and rechecked the asset at 50 x 50.
- Post-fix evidence is recorded in the full-view and focused comparison images above.

## Follow-up polish

- P3: capture a full in-app screenshot when desktop preview execution is available again to confirm the exact LIVE/STOP overlay position against real card content. Build and UI contract verification already pass.

final result: passed
