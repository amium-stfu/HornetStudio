# Widgets

Widgets are documented through three complementary Markdown roles.

## Documentation Roles

- `../widgets/descriptions/<Type>.md` provides short picker text.
- `../widgets/help/<Type>.help.md` provides detailed widget help.
- `manual/` chapters provide cross-topic handbook context where multiple widgets or workflows relate to each other.

## Planned Topics

- when to use widget descriptions
- when to open detailed widget help
- how widget-specific help relates to broader editor guidance

## Folder-level Browser Workflows

Not every editor workflow should remain a placeable canvas widget.

- Functions are now managed primarily through the Admin-only `Browser` section in the right folder sidebar.
- UDL clients are now folder-local resources stored under `Clients/Udl/<client-id>.yaml`; the Admin-only `Browser` section now opens a generic `ClientsBrowser` shell that currently lists and edits those file-backed UDL clients.
- The same client YAML now stores attached module roots and structured `UdlModuleExposures`, so the browser `Edit` action no longer depends on a matching visible UDL client widget.
- `ClientsBrowser` is a folder-local `Clients/` inspector and editor only. Runtime start, projection, and attached item publishing continue to work independently from the browser window.
- UDL clients are managed through folder-local `Clients/Udl/<client-id>.yaml` files and the Clients Browser; the former visible UDL client widget has been removed.
- Signal widgets that display attached UDL values now use a shared runtime live-value store and a shared UI cadence for steady-state rendering, while target rebinds and metadata changes still flow through the structural registry path.
- Existing persisted `Functions` widgets still load for backward compatibility.
- New widget selection should stay focused on visual composition widgets instead of folder registries when a dedicated folder command exists.

For the available widget document structure, see `../widgets/index.md` and `../widgets/help/index.md`.
