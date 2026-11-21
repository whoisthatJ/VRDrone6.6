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

| Blockly 13.5.0 | The block editor of Studio | Apache-2.0 | In the repository under `Assets/StreamingAssets/Edu/Studio/blockly` with its `LICENSE`; only the editor, the standard blocks, the Python generator, English and Russian texts and the media files are kept |
| Python 3.11.9 (Windows embeddable package) | Runs students' programs as a separate process | PSF License | Not in the repository: fetched into `ThirdParty/edu/python` |
| numpy 2.2.6 | Arrays and camera frames in students' programs | BSD-3-Clause (with bundled parts under 0BSD, MIT, Zlib, CC0) | Inside the Python runtime |
| opencv-python-headless 4.11.0.86 | Image processing in students' programs | Apache-2.0 (the wheel also carries FFmpeg libraries under LGPL and others; its own `LICENSE-3RD-PARTY.txt` lists them) | Inside the Python runtime; 108 MB, the largest part |
| pymavlink 2.4.50 | MAVLink from students' programs (task 9) | **LGPL-3.0** | Inside the Python runtime, unmodified, as a separate replaceable package. LGPL asks for the licence text and a way to get the source to be shipped with it. **To be confirmed by whoever is responsible for licensing before the first installer goes out** |
| lxml 6.1.3, fastcrc 0.5.0 | Needed by pymavlink | BSD-3-Clause, MIT | Inside the Python runtime |

All of the above are fetched by `Tools/Edu/fetch_third_party.py` at pinned versions and checked against the hashes in
`Tools/Edu/third_party_hashes.json`. Not bundled yet: the installer tool.

Copyleft code is not linked into the program.
