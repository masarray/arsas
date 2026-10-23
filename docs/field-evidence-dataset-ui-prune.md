# Static DataSet Signal Selection field evidence

Field validation on ARSAS `916a37e3d3c3364bc38a34e4ed2aaaa70a11cac6` with ARIEC61850 `761fa0df9ca84fbe15352d2670f03fddbe8784ba` showed:

- 2 static DataSets
- 58 static members
- 58 semantic descriptors
- only 5/58 represented after UI load
- 53 members reported as restored repeatedly by `Iec61850DataSetSignalInventoryService`

The repeated restore proved the authority service was adding the rows successfully and that a later lifecycle stage removed them.

Source audit identified `SasOperationalUiPolicy` as that stage. Its global `DataGrid.Loaded` handler scheduled a destructive prune against the underlying `IList`, removing every `SignalDefinition` rejected by `SasOperationalSignalPolicy.IsVisible`. Object-level Siemens FCDA/FCD members intentionally fail the exact-runtime-leaf policy, so mandatory static DataSet rows were deleted directly from `device.Signals`.

The fix changes this contract from source mutation to presentation-only filtering and explicitly keeps rows with static `DataSetReference` visible. The authoritative signal collection is no longer changed by DataGrid policy.
