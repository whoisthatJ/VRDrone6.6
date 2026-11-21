# Third-party components

What the program bundles or links that was not written for it, and under which licence. Art, textures and map data
are listed in `Assets/_Project/Art/ASSET_LICENSES.md`.

| Component | Used for | Licence | Note |
|---|---|---|---|
| Unity engine and packages (URP, Input System, Localization, Netcode for GameObjects, Splines, Cinemachine, TextMesh Pro) | The simulator itself | Unity Companion Licence / Unity Terms | Listed in `Packages/manifest.json` |
| Liberation Sans (font) | Text on screen (through TextMesh Pro) and inside PDF reports | SIL Open Font License 1.1 | The font file is embedded in each PDF |
| SQLite | The class database | Public domain | **Not bundled:** the copy that is part of Windows 10 and 11 (`winsqlite3.dll`) or of the Linux system is used |
| ffmpeg | Video export of flights and AI Bench attempts | LGPL / GPL, depending on the build | **Not bundled yet;** started as a separate program. Which build is shipped decides the licence text to include |
| MessagePack (format), MAVLink (protocol) | AI Bench API; ground-station link | Open specifications | Our own code implements both; no library is linked |

Not bundled yet, and to be added here when they are: Blockly (Apache-2.0), a Python runtime with numpy, OpenCV and
pymavlink, the installer tool.

Copyleft code is not linked into the program.
