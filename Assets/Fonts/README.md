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

The files are extracted unmodified from the official release archive path `extras/ttf/` and pinned by SHA-256:

| Face | SHA-256 |
| --- | --- |
| Inter-Regular.ttf | `40d692fce188e4471e2b3cba937be967878f631ad3ebbbdcd587687c7ebe0c82` |
| Inter-Medium.ttf | `97ad806f526e41546d46365bb3a393145f75b7b1568913db74549ad8b8dba872` |
| Inter-SemiBold.ttf | `78a843fade9d4612a5567302fb595b56976eb5fcebf4fea5a5912d638bafcde3` |
| Inter-Bold.ttf | `288316099b1e0a47a4716d159098005eef7c0066921f34e3200393dbdb01947f` |

The complete SIL Open Font License 1.1 is retained as `Inter-LICENSE.txt`.

These are static faces by design. WPF's variable-font support is not sufficient for ARSAS's deterministic weight-selection contract.
