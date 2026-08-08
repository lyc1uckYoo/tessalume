# Shell composition

`Shell/` contains window-lifetime coordinators that must interact with WPF dialogs,
application shutdown, navigation, or native Windows integration. Shell files may compose
feature services, but they do not own feature controls or low-level storage/network work.

New product behavior belongs in `Features/`. A Shell partial is appropriate only when the
operation must cross a feature boundary or control the process lifetime.
