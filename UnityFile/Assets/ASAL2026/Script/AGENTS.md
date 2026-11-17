# ASAL redesign scripts

Follow the [repository instructions](../../../../AGENTS.md) and [implementation architecture](../../../../docs/02-implementation-architecture.md). This directory is the source root for the redesign in `Assets/ASAL2026/Scene/Main.unity`. The legacy scripts live under `Assets/Script/`.

## Folder layout

Place scripts in the folder that owns their responsibility. The table shows where the existing scripts live.

| Folder | Scripts |
| --- | --- |
| `Gameplay/Instance/` | `GameplayInstance` |
| `Gameplay/Manager/` | `GameFlowManager`, `InputManager`, `SaveManager`, `PhotonManager` |
| `Gameplay/Account/` | `AccountProfile`, `AuthManager` |
| `Gameplay/Room/` | `RoomManager`, `RoomRules` |
| `Gameplay/Map/` | `MapManager` |
| `UI/` | `UIManager`, `UIShell`, `TabTransition` |
| `UI/SignIn/` | `SignInPanel` |
| `UI/Lobby/` | `LobbyPanel`, `LobbyRoomRow`, `RoomSetupPanel`, `RoomMemberRow` |
| `UI/CharacterPicker/` | `CharacterPickerPanel` |
| `UI/MapEditor/` | `MapEditorPanel` |
| `Tests/` | Edit Mode tests and their existing test assembly definition |

Use singular, PascalCase folder names. Keep an established `ASAL` type's namespace and assembly when moving its file. Keep new runtime code in the `ASAL` namespace. Move scripts through the Unity Project window or `AssetDatabase.MoveAsset` so their existing `.meta` GUIDs travel with them. Keep authored assets such as scenes and prefabs in their respective folders under `Assets/ASAL2026/`.

## Ownership and lifetime

- `GameplayInstance` registers scene managers, initializes input and UI first, activates the manager group and shell, and starts `GameFlowManager` last. Keep its startup and teardown explicit.
- `InputManager` owns shared input contexts. Consumers use its actions and release their subscriptions when their context ends. `UIShell` owns the screen root and the single EventSystem.
- `AuthManager` owns account identity. `SaveManager` owns Firebase record access. `PhotonManager` owns PUN transport. `RoomManager` owns room policy and uses `RoomRules` for deterministic decisions.
- `MapManager` owns the local map draft. `UIManager` owns screen transitions and prefab instances. Panels render state and forward user intent. Author layout and internal bindings on the source prefabs.
- Place new feature rules and state with their owner. Use `Gameplay/Manager/` for shared services and `UI/<Feature>/` for feature panels and rows. Introduce a shared base only when multiple implementations share a workflow.
- Pair subscriptions with cleanup. Guard asynchronous completions against a changed account or room, and clear room-scoped state on exit.
