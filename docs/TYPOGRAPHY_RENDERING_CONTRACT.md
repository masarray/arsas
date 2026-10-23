# ARSAS Typography Rendering Contract

## Purpose

ARSAS is a dense WPF engineering workstation. Typography must stay compact, readable and visually stable on Windows displays without depending on a font installed on the operator PC.

This contract governs application-shell and workstation UI typography only. It must not change IEC 61850 acquisition, engineering values, evidence, protocol semantics, report facts, or device behavior.

## Typeface authority

The application UI uses **Inter 4.1 static TrueType faces** bundled as WPF `Resource` items:

- `Assets/Fonts/Inter-Regular.ttf`
- `Assets/Fonts/Inter-Medium.ttf`
- `Assets/Fonts/Inter-SemiBold.ttf`
- `Assets/Fonts/Inter-Bold.ttf`

Source archive:

- upstream: `rsms/inter`
- release: `v4.1`
- archive: `Inter-4.1.zip`
- archive SHA-256: `9883fdd4a49d4fb66bd8177ba6625ef9a64aa45899767dde3d36aa425756b11e`
- license: SIL Open Font License 1.1
- retained license: `Assets/Fonts/Inter-LICENSE.txt`

Static faces are intentional. WPF does not expose reliable arbitrary variable-font axis selection, so the workstation must not use `InterVariable.ttf` as the primary UI font.

## WPF resource contract

The font files are compiled with the WPF `Resource` build action. Do not change them to `EmbeddedResource`; WPF font resolution does not use that build action.

The canonical family resource is:

```xml
<FontFamily x:Key="AppFontFamily">./Assets/Fonts/#Inter</FontFamily>
```

Protocol/hex/code-oriented text may use the separate `AppCodeFontFamily` resource.

## Rendering contract

Primary UI text uses:

```xml
TextOptions.TextFormattingMode="Ideal"
TextOptions.TextRenderingMode="ClearType"
TextOptions.TextHintingMode="Fixed"
UseLayoutRounding="True"
SnapsToDevicePixels="True"
```

Intent:

- `Ideal` preserves smoother glyph shape and spacing instead of forcing compact text into display-pixel metrics;
- `ClearType` keeps sub-pixel antialiasing for the opaque workstation surfaces;
- `Fixed` prevents animated hinting behavior for static engineering text;
- layout rounding and pixel snapping are retained for borders, rows and chrome rather than using text glyph distortion to make controls look sharp.

Do not reintroduce `TextFormattingMode="Display"` into the shared workspace typography styles without a measured visual reason and a DPI regression check.

## Weight hierarchy

Use a restrained hierarchy:

- Regular: tables, body text, captions and descriptions;
- Medium: compact control labels when Regular lacks emphasis;
- SemiBold: section titles, selected/navigation labels, DataGrid headers and primary actions;
- Bold: exceptional emphasis only; do not use Bold as the normal workstation heading weight.

The UI should remain dense and calm. Larger or heavier text is not a substitute for hierarchy.

## DPI acceptance matrix

Visual validation for a typography change must cover at least:

- 100% scaling;
- 125% scaling;
- 150% scaling;
- 200% scaling.

At each scale check:

1. workspace title edges and diagonal strokes;
2. DataGrid column headers;
3. compact button captions;
4. IED card name and endpoint text;
5. live-value badges and small captions;
6. no clipping after font-metric changes;
7. no fallback to Aptos/Segoe UI because the embedded Inter resource failed.

A release candidate should also be checked after moving the application between monitors with different scaling when practical.

## Regression boundary

Typography changes are presentation-only. They must not:

- change DataGrid schemas or engineering columns;
- disable virtualization;
- alter live acquisition cadence;
- modify FAT evidence state;
- modify serialized project data;
- touch IEC 61850 protocol mapping;
- change COMTRADE, SCL or report factual content.

## Future changes

If Inter is upgraded:

1. pin the upstream release;
2. verify the archive checksum;
3. retain the complete OFL license;
4. keep static WPF faces unless WPF variable-font support is explicitly qualified;
5. rerun typography regression tests and the DPI acceptance matrix;
6. compare packaged output to ensure the font resources and license are present.
