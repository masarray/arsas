# ArIED 61850 installer

`ArIED61850.iss` is compiled by `scripts/build-windows-installer.ps1` after the self-contained Windows x64 application has been published.

Do not add application binaries to this directory. The installer always consumes the validated `dist/ArIED61850-<version>-win-x64` publish folder so portable and installed releases contain the same runtime files.
