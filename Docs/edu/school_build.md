# The school build

The version of the simulator that is installed in classrooms. It is the whole program with one difference: **nothing in
it can talk to a server on the internet** (`EDU_PLAN.md` F5). Races between PCs in the classroom's own network still
work.

## What is left out
| Left out | What it did | How it is left out |
|---|---|---|
| Unity services: sign-in, internet races through Unity's servers, online lap boards | Needed an internet connection and an anonymous account | The packages `com.unity.services.multiplayer` and `com.unity.services.leaderboards` are not in the project while the build is made |
| The engine's analytics module | Usage statistics to Unity | The module `com.unity.modules.unityanalytics` is not in the project while the build is made |
| Unity's crash and performance reporting | Reports to Unity's cloud | Switched off in the build settings for this build, and put back afterwards |
| **Engine Diagnostics** (Project Settings > Services > Insights; on by default in this Unity version) | Sends data about the running program to `engine-data.unity3d.com` | Switched off in the project settings for this build, and put back afterwards |
| The engine's device statistics and its link to Unity's services | Start-up calls to `config.uca.cloud.unity3d.com` and `cdp.cloud.unity3d.com` | Switched off in the settings for this build, and again by the program itself before anything else runs (`SchoolPrivacy`, compiled only into the school build) |

The code is the same for both versions. The network assembly (`Ushqyr.Net`) knows whether the services packages are
there (the define `USHQYR_CLOUD`, set by the assembly's "version defines"). Without them `OnlineServices.Available` is
false, the Online page offers "Host a LAN race" and "Join LAN" only, and the setting "Post lap times online" is not
shown.

## How to make it
1. `python -I Tools/Edu/school_manifest.py strip` (keeps a copy of the two package files beside them).
2. Wait until the Unity editor has finished re-importing.
3. Menu **Ushqyr > Build > Build School Version (Windows)**, or `BuildScript.BuildSchoolWindows` from the command line.
   The result is in `Builds/School/Windows`, and `Builds/build-result.txt` ends with the line of the school check.
4. `python -I Tools/Edu/school_manifest.py restore`, and wait for the editor again.

The build refuses to start while the internet packages are still in the project. After the build it looks into the
program's `Managed` folder and fails the check if a library of Unity services, analytics, Unity Connect, crash reporting
or performance reporting is there. `BuildScript.BuildSchoolProbe` does the same on a small set of scenes, for a quick
check of the list of libraries.

## What was measured (21 November 2026, Unity 6000.6.0f1, a Unity Personal licence)
A small build with the school settings (`BuildSchoolProbe`: start-up and AI Bench scenes) was run for 30 seconds and its
connections listed with `netstat -ano` for its process:

| Build | Connections held by the program |
|---|---|
| Services packages removed, nothing else | 3: `config.uca.cloud.unity3d.com`, `cdp.cloud.unity3d.com`, `engine-data.unity3d.com` |
| + analytics, crash and performance reporting off in the settings | 3, the same |
| + the program switches statistics off at start (`SchoolPrivacy`) | 1: `engine-data.unity3d.com` |
| + Engine Diagnostics off | **0** |

So removing the packages is not enough by itself: the engine has its own reporting, and all of the steps above are
needed. The build script does them together.

**Not measured yet**
- **The whole school build** (every scene). Only the small probe was built and run; the list of libraries and the
  settings are the same, but the full program was not watched.
- **Longer than 30 seconds, and what happens on quitting.** Some reporting is sent when a program closes.
- **Other kinds of traffic.** `netstat` shows connections that are open; it does not show names being looked up or
  single packets. A check with a traffic recorder on a second PC belongs to the classroom test (ED10).

**To check a build yourself:** start it, then in a command window run `netstat -ano` and look for the lines ending in the
program's process number (Task Manager > Details). There should be none, apart from LAN races you started.

**Libraries that stay in the program.** The engine's reporting modules (`UnityEngine.UnityAnalyticsModule.dll`,
`UnityConnectModule`, `CrashReportingModule`, `PerformanceReportingModule`) are part of the engine's own layer and cannot
be left out of a program built this way; they are switched off as described. The build's check reports them, and fails
only if a Unity services library is found.

**The other builds are not changed.** The ordinary game and the AI Bench Team Kit are built as before, with Engine
Diagnostics on (Unity's default). Whether the Team Kit should also be silent is the owner's decision.

## What stays, and why
- **Netcode for GameObjects and Unity Transport:** the race network inside the classroom. They open a connection only to
  the address a pilot types (or, later, the teacher's PC).
- **UnityWebRequest modules:** used to read files shipped with the program on some platforms; no address on the internet
  is in the code. A check of the running program's connections is in the results below.
