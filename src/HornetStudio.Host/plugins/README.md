Create one subfolder per plugin in this directory.

Expected shape:
- `src/Hornetstudio.Host/plugins/Amium.UdlClient/Amium.UdlClient.dll`
- optional additional managed/native dependencies can stay next to the plugin DLL
- a plugin assembly references `Hornetstudio.Contracts.dll`
- it contains one or more non-abstract types implementing `Hornetstudio.Contracts.IHostPlugin`
- the host scans this directory recursively and loads those plugins on startup/build
