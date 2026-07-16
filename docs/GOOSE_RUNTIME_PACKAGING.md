# GOOSE runtime packaging

The Windows portable release is published as a self-contained **multi-file** folder. Keep the complete extracted folder together.

GOOSE capture uses the ARIEC61850 Npcap transport, SharpPcap, and PacketDotNet. Publishing these managed transport assemblies beside `ArIED61850.exe` avoids optional-transport assembly resolution failures and the startup/extraction overhead of a compressed single-file executable.

The packaging script fails the release when any required GOOSE transport assembly is absent. Npcap must also be installed on the Windows workstation before capture adapters can be enumerated.

Model binding from SCL or live discovery is enrichment rather than a capture prerequisite. When model binding cannot be built, the subscriber remains available in unbound mode and displays values in exact GOOSE `allData` order.
