# Bundled Inter fonts

ARSAS ships the static TrueType faces below as WPF application resources:

- Inter-Regular.ttf
- Inter-Medium.ttf
- Inter-SemiBold.ttf
- Inter-Bold.ttf

Upstream: https://github.com/rsms/inter  
Pinned release: v4.1  
Release archive: Inter-4.1.zip  
Release archive SHA-256: `9883fdd4a49d4fb66bd8177ba6625ef9a64aa45899767dde3d36aa425756b11e`

The files are extracted unmodified from the official release archive path `extras/ttf/`. The complete SIL Open Font License 1.1 is retained as `Inter-LICENSE.txt`.

These are static faces by design. WPF's variable-font support is not sufficient for ARSAS's deterministic weight-selection contract.
